using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Finance;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>Real SQL Server proof of plan FKs, rowversion, approval events and billing guard.</summary>
[Collection("SqlServerIntegration")]
public sealed class SqlServerStudentFeeInstallmentPlanIntegrationTests
{
    [Fact]
    public async Task Gross_plan_is_persisted_approved_independently_and_blocks_unlinked_invoice()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long tenantId, makerId, checkerId, amenderId, batchId, structureId;
        Guid studentRef;
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant {
                Code = "CI-PLAN-" + suffix, Name = "Plan CI", Email = "plan-" + suffix + "@example.test",
                State = TenantState.Active
            };
            var person = new Person { FullName = "Plan CI Learner" };
            var maker = new ApplicationUser {
                UserName = "plan-maker-" + suffix, NormalizedUserName = ("PLAN-MAKER-" + suffix).ToUpperInvariant(),
                Email = "maker-" + suffix + "@example.test", SecurityStamp = Guid.NewGuid().ToString("N")
            };
            var checker = new ApplicationUser {
                UserName = "plan-checker-" + suffix, NormalizedUserName = ("PLAN-CHECKER-" + suffix).ToUpperInvariant(),
                Email = "checker-" + suffix + "@example.test", SecurityStamp = Guid.NewGuid().ToString("N")
            };
            var amender = new ApplicationUser {
                UserName = "plan-amender-" + suffix,
                NormalizedUserName = ("PLAN-AMENDER-" + suffix).ToUpperInvariant(),
                Email = "amender-" + suffix + "@example.test",
                SecurityStamp = Guid.NewGuid().ToString("N")
            };
            setup.AddRange(tenant, person, maker, checker, amender);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id; makerId = maker.Id; checkerId = checker.Id; amenderId = amender.Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Code = "MAIN", Name = "Main" };
                var year = new AcademicYear {
                    TenantId = tenantId, Name = "2026", Code = "2026",
                    StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31)
                };
                var program = new AcademicProgram { TenantId = tenantId, Name = "Secondary", Code = "SEC" };
                var student = new Student {
                    TenantId = tenantId, PersonId = person.Id,
                    StudentCode = "PL-" + suffix, FullName = "Plan CI Learner",
                    StatusCode = "Active", AdmissionDate = new DateOnly(2026, 1, 1)
                };
                var head = new FeeHead { TenantId = tenantId, Code = "TUITION", Name = "Tuition" };
                setup.AddRange(campus, year, program, student, head);
                await setup.SaveChangesAsync();
                studentRef = student.PublicId;
                var level = new AcademicLevel {
                    TenantId = tenantId, AcademicProgramId = program.Id,
                    Name = "Nine", Code = "G9", LevelNo = 9
                };
                var curriculum = new AcademicCurriculum {
                    TenantId = tenantId, AcademicProgramId = program.Id,
                    Name = "2026", Code = "SEC26",
                    EffectiveFrom = new DateOnly(2026, 1, 1), IsCurrent = true
                };
                setup.AddRange(level, curriculum);
                await setup.SaveChangesAsync();
                var batch = new AcademicBatch {
                    TenantId = tenantId, CampusId = campus.Id, AcademicYearId = year.Id,
                    AcademicProgramId = program.Id, AcademicLevelId = level.Id,
                    Code = "NINE-A", Name = "Nine A", Capacity = 30
                };
                setup.AcademicBatches.Add(batch);
                await setup.SaveChangesAsync();
                batchId = batch.Id;
                setup.StudentEnrollments.Add(new StudentEnrollment {
                    TenantId = tenantId, StudentId = student.Id, CampusId = campus.Id,
                    AcademicYearId = year.Id, AcademicProgramId = program.Id,
                    AcademicLevelId = level.Id, AcademicCurriculumId = curriculum.Id,
                    AcademicBatchId = batch.Id, RollNo = "01", IsCurrent = true,
                    State = EnrollmentState.Active, EnrollmentDate = new DateOnly(2026, 1, 1),
                    ClientRequestId = Guid.NewGuid()
                });
                var structure = new FeeStructure {
                    TenantId = tenantId, CampusId = campus.Id, AcademicYearId = year.Id,
                    AcademicBatchId = batch.Id, Name = "Monthly", CurrencyCode = "BDT",
                    EffectiveFrom = new DateOnly(2026, 1, 1)
                };
                setup.FeeStructures.Add(structure);
                await setup.SaveChangesAsync();
                structureId = structure.Id;
                setup.FeeStructureLines.Add(new FeeStructureLine {
                    TenantId = tenantId, FeeStructureId = structure.Id,
                    FeeHeadId = head.Id, Frequency = FeeFrequencyType.Monthly,
                    Amount = 100m, DueDayOfMonth = 31
                });
                await setup.SaveChangesAsync();
            }
        }

        await using var db = new EduOSDbContext(options, Accessor(tenantId));
        var makerService = Service(db, tenantId, makerId);
        var request = new CreateStudentFeeInstallmentPlanDto {
            ClientRequestId = Guid.NewGuid(), StudentReference = studentRef,
            AcademicBatchId = batchId, FeeStructureId = structureId,
            StartMonth = new DateOnly(2026, 2, 1), Months = 3
        };
        var draft = await makerService.CreateInstallmentPlanAsync(request);
        Assert.True(draft.Success, draft.Message);
        Assert.Equal(300m, draft.Data!.GrossAmount);
        Assert.Equal(3, draft.Data.LineCount);
        Assert.False(string.IsNullOrWhiteSpace(draft.Data.RowVersion));
        Assert.Equal(new DateOnly(2026, 2, 28), draft.Data.Lines[0].DueDate);
        var replay = await makerService.CreateInstallmentPlanAsync(request);
        Assert.True(replay.Success, replay.Message);
        Assert.Equal(draft.Data.Reference, replay.Data!.Reference);

        var invoice = new GenerateStudentInvoiceBatchRequestDto {
            ClientRequestId = Guid.NewGuid(), AcademicBatchId = batchId,
            FeeStructureId = structureId, Year = 2026, Month = 2,
            InvoiceDate = new DateOnly(2026, 2, 1), DueDate = new DateOnly(2026, 2, 28)
        };
        Assert.Equal(409, (await makerService.PreviewInvoiceBatchAsync(invoice)).StatusCode);
        Assert.Equal(409, (await makerService.GenerateInvoicesAsync(invoice)).StatusCode);

        var submitted = await makerService.TransitionInstallmentPlanAsync(draft.Data.Reference, 2,
            new StudentFeeInstallmentTransitionDto {
                ClientRequestId = Guid.NewGuid(), RowVersion = draft.Data.RowVersion
            });
        Assert.True(submitted.Success, submitted.Message);
        // Real SQL translation and actor isolation of the independent approval queue.
        var checkerQueue = await Service(db, tenantId, checkerId).GetInstallmentPlansAsync(
            search: "PL-" + suffix, stateCode: 2, myApprovalQueue: true);
        Assert.True(checkerQueue.Success, checkerQueue.Message);
        Assert.Single(checkerQueue.Data!.Items);
        Assert.True(checkerQueue.Data.Items[0].CanReviewForApproval);
        var makerQueue = await makerService.GetInstallmentPlansAsync(myApprovalQueue: true);
        Assert.True(makerQueue.Success, makerQueue.Message);
        Assert.Empty(makerQueue.Data!.Items);
        var denied = await makerService.TransitionInstallmentPlanAsync(draft.Data.Reference, 3,
            new StudentFeeInstallmentTransitionDto {
                ClientRequestId = Guid.NewGuid(), RowVersion = draft.Data.RowVersion
            });
        Assert.Equal(403, denied.StatusCode);
        var checkerService = Service(db, tenantId, checkerId);
        var approved = await checkerService.TransitionInstallmentPlanAsync(draft.Data.Reference, 3,
            new StudentFeeInstallmentTransitionDto {
                ClientRequestId = Guid.NewGuid(), RowVersion = draft.Data.RowVersion
            });
        Assert.True(approved.Success, approved.Message);
        Assert.Equal(3, approved.Data!.StateCode);
        Assert.Equal(2, await db.StudentFeeInstallmentPlanEvents.CountAsync());
        Assert.Equal(3, await db.StudentFeeInstallmentPlanLines.CountAsync());
        Assert.Equal(0, await db.StudentInvoices.CountAsync());
        Assert.Equal(0, await db.StudentPayments.CountAsync());
        Assert.Equal(0, await db.Journals.CountAsync());
        Assert.Equal(404, (await makerService.GetInstallmentPlanAsync(Guid.NewGuid())).StatusCode);
        Assert.Equal(403, (await Service(db, tenantId + 1, checkerId)
            .GetInstallmentPlanAsync(draft.Data.Reference)).StatusCode);
        // Real SQL proof: the approved frozen February month is invoiced once,
        // survives a different request key and is visible through plan readback.
        var issue = new IssueStudentFeeInstallmentMonthDto {
            ClientRequestId = Guid.NewGuid(), MonthStart = new DateOnly(2026, 2, 1),
            RowVersion = approved.Data.RowVersion
        };
        var firstInvoice = await checkerService.IssueInstallmentMonthAsync(draft.Data.Reference, issue);
        Assert.True(firstInvoice.Success, firstInvoice.Message);
        Assert.False(firstInvoice.Data!.Existing);
        Assert.Equal(100m, firstInvoice.Data.GrossAmount);
        issue.ClientRequestId = Guid.NewGuid();
        var sameMonth = await checkerService.IssueInstallmentMonthAsync(draft.Data.Reference, issue);
        Assert.True(sameMonth.Success, sameMonth.Message);
        Assert.True(sameMonth.Data!.Existing);
        Assert.Equal(firstInvoice.Data.InvoiceReference, sameMonth.Data.InvoiceReference);
        Assert.Equal(1, await db.StudentInvoices.CountAsync());
        Assert.Equal(1, await db.StudentInvoiceLines.CountAsync());
        var readback = await checkerService.GetInstallmentPlanAsync(draft.Data.Reference);
        Assert.Equal(firstInvoice.Data.InvoiceReference, readback.Data!.Lines[0].InvoiceReference);
        var audit = await checkerService.GetInstallmentBillingAuditAsync(draft.Data.Reference);
        Assert.True(audit.Success, audit.Message);
        Assert.Equal(3, audit.Data!.Months);
        Assert.Equal(1, audit.Data.BilledMonths);
        Assert.Equal(2, audit.Data.UnbilledMonths);
        Assert.Equal(0, audit.Data.ReviewMonths);
        Assert.Equal(100m, audit.Data.VerifiedInvoiceNet);
        Assert.Equal(403, (await Service(db, tenantId + 1, checkerId)
            .GetInstallmentBillingAuditAsync(draft.Data.Reference)).StatusCode);
        issue.MonthStart = new DateOnly(2026, 3, 1);
        var march = await checkerService.IssueInstallmentMonthAsync(draft.Data.Reference, issue);
        Assert.True(march.Success, march.Message);
        Assert.Equal(2, await db.StudentInvoices.CountAsync());
        Assert.Equal(2, await db.StudentInvoiceLines.CountAsync());
        var twoMonths = await checkerService.GetInstallmentBillingAuditAsync(draft.Data.Reference);
        Assert.Equal(2, twoMonths.Data!.BilledMonths);
        Assert.Equal(1, twoMonths.Data.UnbilledMonths);
        Assert.Equal(0, twoMonths.Data.ReviewMonths);
        Assert.Equal(0, await db.StudentPayments.CountAsync());
        Assert.Equal(0, await db.Journals.CountAsync());
        Assert.Equal(403, (await Service(db, tenantId + 1, checkerId)
            .IssueInstallmentMonthAsync(draft.Data.Reference, issue)).StatusCode);
        // A later, entirely unbilled approved agreement can be atomically replaced
        // by a third administrator; the original evidence remains immutable.
        var later = await makerService.CreateInstallmentPlanAsync(new CreateStudentFeeInstallmentPlanDto {
            ClientRequestId = Guid.NewGuid(), StudentReference = studentRef,
            AcademicBatchId = batchId, FeeStructureId = structureId,
            StartMonth = new DateOnly(2026, 5, 1), Months = 2
        });
        Assert.True(later.Success, later.Message);
        var laterSubmitted = await makerService.TransitionInstallmentPlanAsync(later.Data!.Reference, 2,
            new StudentFeeInstallmentTransitionDto {
                ClientRequestId = Guid.NewGuid(), RowVersion = later.Data.RowVersion
            });
        Assert.True(laterSubmitted.Success, laterSubmitted.Message);
        var laterApproved = await checkerService.TransitionInstallmentPlanAsync(later.Data.Reference, 3,
            new StudentFeeInstallmentTransitionDto {
                ClientRequestId = Guid.NewGuid(), RowVersion = laterSubmitted.Data!.RowVersion
            });
        Assert.True(laterApproved.Success, laterApproved.Message);
        var amenderService = Service(db, tenantId, amenderId);
        var amendmentRequest = new ReplaceStudentFeeInstallmentPlanDto {
            ClientRequestId = Guid.NewGuid(), RowVersion = laterApproved.Data!.RowVersion,
            FeeStructureId = structureId, Reason = "Reconfirm future due dates"
        };
        Assert.Equal(403, (await makerService.ReplaceInstallmentPlanAsync(
            later.Data.Reference, amendmentRequest)).StatusCode);
        var amended = await amenderService.ReplaceInstallmentPlanAsync(later.Data.Reference, amendmentRequest);
        Assert.True(amended.Success, amended.Message);
        Assert.Equal(2, amended.Data!.StateCode);
        Assert.Equal(4, (await checkerService.GetInstallmentPlanAsync(later.Data.Reference)).Data!.StateCode);
        var replayAmendment = await amenderService.ReplaceInstallmentPlanAsync(later.Data.Reference, amendmentRequest);
        Assert.True(replayAmendment.Success, replayAmendment.Message);
        Assert.Equal(amended.Data.Reference, replayAmendment.Data!.Reference);
        Assert.Equal(403, (await amenderService.TransitionInstallmentPlanAsync(amended.Data.Reference, 3,
            new StudentFeeInstallmentTransitionDto {
                ClientRequestId = Guid.NewGuid(), RowVersion = amended.Data.RowVersion
            })).StatusCode);
        Assert.Equal(403, (await Service(db, tenantId + 1, amenderId)
            .ReplaceInstallmentPlanAsync(later.Data.Reference, amendmentRequest)).StatusCode);
        var amendmentApproved = await makerService.TransitionInstallmentPlanAsync(amended.Data.Reference, 3,
            new StudentFeeInstallmentTransitionDto {
                ClientRequestId = Guid.NewGuid(), RowVersion = amended.Data.RowVersion
            });
        Assert.True(amendmentApproved.Success, amendmentApproved.Message);
        var mayInvoice = await makerService.IssueInstallmentMonthAsync(amended.Data.Reference,
            new IssueStudentFeeInstallmentMonthDto {
                ClientRequestId = Guid.NewGuid(), RowVersion = amendmentApproved.Data!.RowVersion,
                MonthStart = new DateOnly(2026, 5, 1)
            });
        Assert.True(mayInvoice.Success, mayInvoice.Message);
        Assert.Equal(3, await db.StudentInvoices.CountAsync());
        Assert.Equal(409, (await checkerService.ReplaceInstallmentPlanAsync(amended.Data.Reference,
            new ReplaceStudentFeeInstallmentPlanDto {
                ClientRequestId = Guid.NewGuid(), RowVersion = amendmentApproved.Data.RowVersion,
                FeeStructureId = structureId, Reason = "Attempt to rewrite billed month"
            })).StatusCode);
        Assert.Equal(0, await db.StudentPayments.CountAsync());
        Assert.Equal(0, await db.Journals.CountAsync());

        // The approving checker cannot cancel their own approved agreement.
        Assert.Equal(403, (await checkerService.TransitionInstallmentPlanAsync(draft.Data.Reference, 4,
            new StudentFeeInstallmentTransitionDto {
                ClientRequestId = Guid.NewGuid(), RowVersion = approved.Data.RowVersion,
                Reason = "Cancel after billing"
            })).StatusCode);
    }

    private static IHttpContextAccessor Accessor(long tenantId)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenantId;
        return accessor;
    }

    private static FeeBillingService Service(EduOSDbContext db, long tenantId, long userId) => new(
        new GenericRepository<FeeStructure>(db), new GenericRepository<FeeStructureLine>(db),
        new GenericRepository<FeeHead>(db), new GenericRepository<StudentDiscount>(db),
        new GenericRepository<DiscountRule>(db), new GenericRepository<StudentInvoice>(db),
        new GenericRepository<StudentInvoiceLine>(db), new GenericRepository<StudentPayment>(db),
        new GenericRepository<PaymentAllocation>(db), new GenericRepository<StudentEnrollment>(db),
        new GenericRepository<Student>(db), new GenericRepository<AcademicYear>(db),
        new GenericRepository<AcademicLevel>(db), new GenericRepository<AcademicBatch>(db),
        new GenericRepository<Campus>(db), new GenericRepository<Tenant>(db),
        db, new Actor(tenantId, userId), TimeProvider.System, NullLogger<FeeBillingService>.Instance);

    private sealed class Actor(long tenantId, long userId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenantId;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
