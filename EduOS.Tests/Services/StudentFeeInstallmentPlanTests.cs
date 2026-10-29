using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Finance;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentFeeInstallmentPlanTests
{
    [Fact]
    public async Task Draft_freezes_gross_monthly_fee_heads_and_clamps_february_due_day()
    {
        await using var db = Database();
        Seed(db);
        var request = Request(db);
        var result = await Service(db).CreateInstallmentPlanAsync(request);
        result.StatusCode.Should().Be(200);
        result.Data!.StateCode.Should().Be(1);
        result.Data.GrossAmount.Should().Be(300m);
        result.Data.LineCount.Should().Be(3);
        result.Data.Lines[0].DueDate.Should().Be(new DateOnly(2026, 2, 28));
        result.Data.Lines[1].DueDate.Should().Be(new DateOnly(2026, 3, 31));
        (await db.StudentInvoices.CountAsync()).Should().Be(0);
        (await db.StudentPayments.CountAsync()).Should().Be(0);
        (await db.Journals.CountAsync()).Should().Be(0);
        (await db.StudentFeeInstallmentPlanLines.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task Weighted_instalments_preserve_fee_head_total_and_replay_contract()
    {
        await using var db = Database();
        Seed(db);
        var request = Request(db);
        request.MonthWeightsBasisPoints = [5000, 2500, 2500];
        var service = Service(db);
        var first = await service.CreateInstallmentPlanAsync(request);
        first.StatusCode.Should().Be(200);
        first.Data!.GrossAmount.Should().Be(300m);
        first.Data.Lines.Select(x => x.GrossAmount).Should().Equal(150m, 75m, 75m);
        (await service.CreateInstallmentPlanAsync(request)).StatusCode.Should().Be(200);
        request.MonthWeightsBasisPoints = [4000, 3000, 3000];
        (await service.CreateInstallmentPlanAsync(request)).StatusCode.Should().Be(409);
        (await db.StudentFeeInstallmentPlans.CountAsync()).Should().Be(1);
        (await db.StudentInvoices.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Invalid_instalment_weights_are_rejected_without_writes()
    {
        await using var db = Database();
        Seed(db);
        var request = Request(db);
        request.MonthWeightsBasisPoints = [5000, 2500];
        (await Service(db).CreateInstallmentPlanAsync(request)).StatusCode.Should().Be(400);
        request.MonthWeightsBasisPoints = [5000, 2500, 2400];
        (await Service(db).CreateInstallmentPlanAsync(request)).StatusCode.Should().Be(400);
        request.MonthWeightsBasisPoints = [10000, 0, 0];
        (await Service(db).CreateInstallmentPlanAsync(request)).StatusCode.Should().Be(400);
        (await db.StudentFeeInstallmentPlans.CountAsync()).Should().Be(0);
    }

    [Fact]
    public void Weighted_cent_distribution_is_deterministic_and_conserves_every_head()
    {
        var rows = FeeInstallmentDistribution.Allocate([0.01m, 0.02m], 3, [9998, 1, 1]);
        rows.Should().BeNull(); // A near-zero share cannot create a positive cent in every month.
        rows = FeeInstallmentDistribution.Allocate([0.02m, 0.03m], 3, [3334, 3333, 3333]);
        rows.Should().NotBeNull();
        rows!.Sum(x => x[0]).Should().Be(0.06m);
        rows.Sum(x => x[1]).Should().Be(0.09m);
        rows.Select(x => x[0]).Should().Equal(0.02m, 0.02m, 0.02m);
        var odd = FeeInstallmentDistribution.Allocate([0.05m], 3, [5000, 2500, 2500]);
        odd.Should().NotBeNull();
        odd!.Select(x => x[0]).Should().Equal(0.07m, 0.04m, 0.04m);
    }

    [Fact]
    public async Task Planned_month_blocks_legacy_invoice_preflight_and_generation_until_linked_billing()
    {
        await using var db = Database();
        Seed(db);
        var service = Service(db);
        (await service.CreateInstallmentPlanAsync(Request(db))).StatusCode.Should().Be(200);
        var batch = new GenerateStudentInvoiceBatchRequestDto {
            ClientRequestId = Guid.NewGuid(), AcademicBatchId = 1, FeeStructureId = 1,
            Year = 2026, Month = 2, InvoiceDate = new DateOnly(2026, 2, 1),
            DueDate = new DateOnly(2026, 2, 28)
        };
        (await service.PreviewInvoiceBatchAsync(batch)).StatusCode.Should().Be(409);
        (await service.GenerateInvoicesAsync(batch)).StatusCode.Should().Be(409);
        (await db.StudentInvoices.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Same_request_replays_and_different_request_cannot_overlap()
    {
        await using var db = Database();
        Seed(db);
        var request = Request(db);
        var service = Service(db);
        var first = await service.CreateInstallmentPlanAsync(request);
        var replay = await service.CreateInstallmentPlanAsync(request);
        first.StatusCode.Should().Be(200);
        replay.StatusCode.Should().Be(200);
        replay.Data!.Reference.Should().Be(first.Data!.Reference);
        request.ClientRequestId = Guid.NewGuid();
        (await service.CreateInstallmentPlanAsync(request)).StatusCode.Should().Be(409);
        (await db.StudentFeeInstallmentPlans.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Campus_grant_and_tenant_ownership_are_enforced()
    {
        await using var db = Database();
        Seed(db);
        var request = Request(db);
        (await Service(db, "Accountant").CreateInstallmentPlanAsync(request))
            .StatusCode.Should().Be(404);
        db.UserCampusAccesses.Add(new UserCampusAccess {
            TenantId = 41, UserId = 9, CampusId = 1, IsActive = true
        });
        await db.SaveChangesAsync();
        (await Service(db, "Accountant").CreateInstallmentPlanAsync(request))
            .StatusCode.Should().Be(200);
        (await Service(db, "Student").GetInstallmentPlansAsync()).StatusCode.Should().Be(403);
        (await Service(db, "Accountant").GetInstallmentPlansAsync(999)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Independent_checker_required_and_approval_does_not_issue_invoice()
    {
        await using var db = Database();
        Seed(db);
        var plan = new StudentFeeInstallmentPlan {
            TenantId = 41, StudentEnrollmentId = 1, FeeStructureId = 1,
            CampusId = 1, AcademicYearId = 2, StartMonth = new DateOnly(2026, 2, 1),
            EndMonth = new DateOnly(2026, 2, 1), GrossAmount = 100m,
            LineCount = 1, PreparedByUserId = 9, RowVersion = [1,2,3,4]
        };
        db.StudentFeeInstallmentPlans.Add(plan);
        await db.SaveChangesAsync();
        db.StudentFeeInstallmentPlanLines.Add(new StudentFeeInstallmentPlanLine {
            TenantId = 41, StudentFeeInstallmentPlanId = plan.Id,
            FeeHeadId = 1, MonthStart = plan.StartMonth,
            DueDate = new DateOnly(2026, 2, 28),
            FeeHeadCodeSnapshot = "TUITION", FeeHeadNameSnapshot = "Tuition",
            GrossAmount = 100m
        });
        await db.SaveChangesAsync();
        var version = Convert.ToBase64String(plan.RowVersion);
        var submit = new StudentFeeInstallmentTransitionDto {
            ClientRequestId = Guid.NewGuid(), RowVersion = version
        };
        (await Service(db).TransitionInstallmentPlanAsync(plan.PublicId, 2, submit))
            .StatusCode.Should().Be(200);
        (await Service(db).TransitionInstallmentPlanAsync(plan.PublicId, 3,
            new StudentFeeInstallmentTransitionDto {
                ClientRequestId = Guid.NewGuid(), RowVersion = version
            })).StatusCode.Should().Be(403);
        var approval = new StudentFeeInstallmentTransitionDto {
            ClientRequestId = Guid.NewGuid(), RowVersion = version
        };
        (await Service(db, userId: 10).TransitionInstallmentPlanAsync(plan.PublicId, 3, approval))
            .StatusCode.Should().Be(200);
        (await Service(db, userId: 10).TransitionInstallmentPlanAsync(plan.PublicId, 3, approval))
            .StatusCode.Should().Be(200);
        (await db.StudentFeeInstallmentPlanEvents.CountAsync()).Should().Be(2);
        (await db.StudentInvoices.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Approved_evidence_cannot_be_silently_mutated_or_seen_by_foreign_tenant()
    {
        await using var db = Database();
        Seed(db);
        var created = await Service(db).CreateInstallmentPlanAsync(Request(db));
        created.StatusCode.Should().Be(200);
        var line = await db.StudentFeeInstallmentPlanLines.OrderBy(x => x.Id).FirstAsync();
        line.GrossAmount = 1m;
        await FluentActions.Awaiting(() => db.SaveChangesAsync())
            .Should().ThrowAsync<InvalidOperationException>();
        db.Entry(line).State = EntityState.Unchanged;
        var foreign = Guid.NewGuid();
        (await Service(db).GetInstallmentPlanAsync(foreign)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Existing_invoice_or_undefined_due_day_blocks_new_agreement()
    {
        await using var db = Database();
        Seed(db);
        var line = await db.FeeStructureLines.SingleAsync();
        line.DueDayOfMonth = null;
        await db.SaveChangesAsync();
        (await Service(db).CreateInstallmentPlanAsync(Request(db))).StatusCode.Should().Be(409);
        line.DueDayOfMonth = 31;
        await db.SaveChangesAsync();
        db.StudentInvoices.Add(new StudentInvoice {
            TenantId = 41, StudentEnrollmentId = 1,
            InvoiceNumber = "INV-TEST", InvoiceDate = new DateOnly(2026, 2, 1),
            DueDate = new DateOnly(2026, 2, 28), TotalAmount = 100m, DueAmount = 100m
        });
        await db.SaveChangesAsync();
        (await Service(db).CreateInstallmentPlanAsync(Request(db))).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Approved_month_issues_frozen_invoice_and_replays_without_double_billing()
    {
        await using var db = Database();
        Seed(db);
        var plan = new StudentFeeInstallmentPlan {
            TenantId = 41, StudentEnrollmentId = 1, FeeStructureId = 1,
            CampusId = 1, AcademicYearId = 2, StartMonth = new DateOnly(2026, 2, 1),
            EndMonth = new DateOnly(2026, 3, 1), GrossAmount = 200m,
            LineCount = 2, PreparedByUserId = 9, RowVersion = [1, 2, 3, 4]
        };
        db.StudentFeeInstallmentPlans.Add(plan);
        await db.SaveChangesAsync();
        foreach (var month in new[] { new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1) })
            db.StudentFeeInstallmentPlanLines.Add(new StudentFeeInstallmentPlanLine {
                TenantId = 41, StudentFeeInstallmentPlanId = plan.Id, FeeHeadId = 1,
                MonthStart = month, DueDate = month.AddMonths(1).AddDays(-1),
                FeeHeadCodeSnapshot = "TUITION", FeeHeadNameSnapshot = "Tuition", GrossAmount = 100m
            });
        db.StudentFeeInstallmentPlanEvents.AddRange(
            new StudentFeeInstallmentPlanEvent { TenantId = 41, StudentFeeInstallmentPlanId = plan.Id,
                ActionCode = 2, ClientRequestId = Guid.NewGuid(), ActorUserId = 9 },
            new StudentFeeInstallmentPlanEvent { TenantId = 41, StudentFeeInstallmentPlanId = plan.Id,
                ActionCode = 3, ClientRequestId = Guid.NewGuid(), ActorUserId = 10 });
        await db.SaveChangesAsync();
        var request = new IssueStudentFeeInstallmentMonthDto {
            ClientRequestId = Guid.NewGuid(), MonthStart = new DateOnly(2026, 2, 1),
            RowVersion = Convert.ToBase64String(plan.RowVersion)
        };
        var service = Service(db);
        var first = await service.IssueInstallmentMonthAsync(plan.PublicId, request);
        first.StatusCode.Should().Be(200);
        first.Data!.Existing.Should().BeFalse();
        first.Data.GrossAmount.Should().Be(100m);
        request.ClientRequestId = Guid.NewGuid();
        var replay = await service.IssueInstallmentMonthAsync(plan.PublicId, request);
        replay.StatusCode.Should().Be(200);
        replay.Data!.Existing.Should().BeTrue();
        replay.Data.InvoiceReference.Should().Be(first.Data.InvoiceReference);
        (await db.StudentInvoices.CountAsync()).Should().Be(1);
        (await db.StudentInvoiceLines.CountAsync()).Should().Be(1);
        (await db.StudentPayments.CountAsync()).Should().Be(0);
        (await db.Journals.CountAsync()).Should().Be(0);
        var detail = await service.GetInstallmentPlanAsync(plan.PublicId);
        detail.Data!.Lines[0].InvoiceReference.Should().Be(first.Data.InvoiceReference);
        request.MonthStart = new DateOnly(2026, 3, 1);
        (await service.IssueInstallmentMonthAsync(plan.PublicId, request)).StatusCode.Should().Be(200);
        (await db.StudentInvoices.CountAsync()).Should().Be(2);
        (await Service(db, userId: 11).TransitionInstallmentPlanAsync(plan.PublicId, 4,
            new StudentFeeInstallmentTransitionDto { ClientRequestId = Guid.NewGuid(),
                RowVersion = request.RowVersion, Reason = "Cancel" })).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Draft_and_foreign_plan_cannot_be_billed_or_enumerated()
    {
        await using var db = Database();
        Seed(db);
        var created = await Service(db).CreateInstallmentPlanAsync(Request(db));
        var request = new IssueStudentFeeInstallmentMonthDto {
            ClientRequestId = Guid.NewGuid(), MonthStart = new DateOnly(2026, 2, 1),
            RowVersion = created.Data!.RowVersion
        };
        (await Service(db).IssueInstallmentMonthAsync(created.Data.Reference, request)).StatusCode
            .Should().Be(400); // Empty InMemory rowversion is rejected, never bypassed.
        request.RowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 });
        (await Service(db).IssueInstallmentMonthAsync(created.Data.Reference, request)).StatusCode
            .Should().Be(409);
        (await Service(db).IssueInstallmentMonthAsync(Guid.NewGuid(), request)).StatusCode
            .Should().Be(404);
        (await Service(db, "Student").IssueInstallmentMonthAsync(created.Data.Reference, request))
            .StatusCode.Should().Be(403);
        (await db.StudentInvoices.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Billing_audit_flags_competing_invoice_and_mismatched_fee_head_without_mutation()
    {
        await using var db = Database();
        Seed(db);
        var plan = new StudentFeeInstallmentPlan {
            TenantId = 41, StudentEnrollmentId = 1, FeeStructureId = 1,
            CampusId = 1, AcademicYearId = 2,
            StartMonth = new DateOnly(2026, 2, 1), EndMonth = new DateOnly(2026, 2, 1),
            GrossAmount = 100m, LineCount = 1, PreparedByUserId = 9,
            RowVersion = [1, 2, 3, 4]
        };
        db.StudentFeeInstallmentPlans.Add(plan);
        await db.SaveChangesAsync();
        db.StudentFeeInstallmentPlanLines.Add(new StudentFeeInstallmentPlanLine {
            TenantId = 41, StudentFeeInstallmentPlanId = plan.Id, FeeHeadId = 1,
            MonthStart = plan.StartMonth, DueDate = new DateOnly(2026, 2, 28),
            FeeHeadCodeSnapshot = "TUITION", FeeHeadNameSnapshot = "Tuition",
            GrossAmount = 100m
        });
        db.StudentFeeInstallmentPlanEvents.AddRange(
            new StudentFeeInstallmentPlanEvent {
                TenantId = 41, StudentFeeInstallmentPlanId = plan.Id,
                ActionCode = 2, ActorUserId = 9, ClientRequestId = Guid.NewGuid()
            },
            new StudentFeeInstallmentPlanEvent {
                TenantId = 41, StudentFeeInstallmentPlanId = plan.Id,
                ActionCode = 3, ActorUserId = 10, ClientRequestId = Guid.NewGuid()
            });
        var invoice = new StudentInvoice {
            TenantId = 41, StudentEnrollmentId = 1, ClientRequestId = Guid.NewGuid(),
            InvoiceNumber = "INV-LEGACY", InvoiceDate = plan.StartMonth,
            DueDate = new DateOnly(2026, 2, 28), Subtotal = 100m,
            TotalAmount = 100m, DueAmount = 100m
        };
        db.StudentInvoices.Add(invoice);
        await db.SaveChangesAsync();
        db.StudentInvoiceLines.Add(new StudentInvoiceLine {
            TenantId = 41, StudentInvoiceId = invoice.Id, FeeHeadId = 1,
            Description = "Tuition", Amount = 99m, NetAmount = 99m
        });
        await db.SaveChangesAsync();

        var audit = await Service(db).GetInstallmentBillingAuditAsync(plan.PublicId);
        audit.StatusCode.Should().Be(200);
        audit.Data!.ReviewMonths.Should().Be(1);
        audit.Data.BilledMonths.Should().Be(0);
        audit.Data.Rows[0].Issues.Should().Contain(x => x.Contains("fee-head"));
        (await db.StudentInvoices.CountAsync()).Should().Be(1);
        (await Service(db, "Accountant").GetInstallmentBillingAuditAsync(plan.PublicId))
            .StatusCode.Should().Be(404);
        (await Service(db).GetInstallmentBillingAuditAsync(Guid.NewGuid()))
            .StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Pending_approval_queue_is_server_filtered_and_actor_scoped()
    {
        await using var db = Database();
        Seed(db);
        var plan = new StudentFeeInstallmentPlan {
            TenantId = 41, StudentEnrollmentId = 1, FeeStructureId = 1,
            CampusId = 1, AcademicYearId = 2,
            StartMonth = new DateOnly(2026, 2, 1), EndMonth = new DateOnly(2026, 2, 1),
            GrossAmount = 100m, LineCount = 1, PreparedByUserId = 9,
            RowVersion = [1, 2, 3, 4]
        };
        db.StudentFeeInstallmentPlans.Add(plan);
        await db.SaveChangesAsync();
        db.StudentFeeInstallmentPlanLines.Add(new StudentFeeInstallmentPlanLine {
            TenantId = 41, StudentFeeInstallmentPlanId = plan.Id,
            FeeHeadId = 1, MonthStart = plan.StartMonth,
            DueDate = new DateOnly(2026, 2, 28),
            FeeHeadCodeSnapshot = "TUITION", FeeHeadNameSnapshot = "Tuition",
            GrossAmount = 100m
        });
        db.StudentFeeInstallmentPlanEvents.Add(new StudentFeeInstallmentPlanEvent {
            TenantId = 41, StudentFeeInstallmentPlanId = plan.Id,
            ActionCode = 2, ActorUserId = 9, ClientRequestId = Guid.NewGuid()
        });
        await db.SaveChangesAsync();
        var checker = Service(db, userId: 10);
        var pending = await checker.GetInstallmentPlansAsync(
            search: "ST-1", stateCode: 2, myApprovalQueue: true);
        pending.StatusCode.Should().Be(200);
        pending.Data!.TotalCount.Should().Be(1);
        pending.Data.Items.Single().CanReviewForApproval.Should().BeTrue();
        (await checker.GetInstallmentPlanAsync(plan.PublicId)).Data!
            .CanReviewForApproval.Should().BeTrue();
        (await Service(db).GetInstallmentPlansAsync(myApprovalQueue: true))
            .Data!.TotalCount.Should().Be(0);
        (await Service(db, "Accountant").GetInstallmentPlansAsync(myApprovalQueue: true))
            .StatusCode.Should().Be(403);
        (await checker.GetInstallmentPlansAsync(search: "UNKNOWN", stateCode: 2))
            .Data!.TotalCount.Should().Be(0);
        (await checker.GetInstallmentPlansAsync(stateCode: 5)).StatusCode.Should().Be(400);
        (await checker.GetInstallmentPlansAsync(search: new string('x', 101)))
            .StatusCode.Should().Be(400);
        (await checker.GetInstallmentPlansAsync(campusId: 999)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Approved_month_preview_uses_invoice_discounts_without_writing_or_exposing_foreign_plans()
    {
        await using var db = Database();
        Seed(db);
        var plan = new StudentFeeInstallmentPlan {
            TenantId = 41, StudentEnrollmentId = 1, FeeStructureId = 1,
            CampusId = 1, AcademicYearId = 2,
            StartMonth = new DateOnly(2026, 2, 1), EndMonth = new DateOnly(2026, 2, 1),
            GrossAmount = 100m, LineCount = 1, PreparedByUserId = 9,
            RowVersion = [1, 2, 3, 4]
        };
        db.StudentFeeInstallmentPlans.Add(plan);
        await db.SaveChangesAsync();
        db.StudentFeeInstallmentPlanLines.Add(new StudentFeeInstallmentPlanLine {
            TenantId = 41, StudentFeeInstallmentPlanId = plan.Id, FeeHeadId = 1,
            MonthStart = plan.StartMonth, DueDate = new DateOnly(2026, 2, 28),
            FeeHeadCodeSnapshot = "TUITION", FeeHeadNameSnapshot = "Tuition",
            GrossAmount = 100m
        });
        db.StudentFeeInstallmentPlanEvents.AddRange(
            new StudentFeeInstallmentPlanEvent {
                TenantId = 41, StudentFeeInstallmentPlanId = plan.Id,
                ActionCode = 2, ActorUserId = 9, ClientRequestId = Guid.NewGuid()
            },
            new StudentFeeInstallmentPlanEvent {
                TenantId = 41, StudentFeeInstallmentPlanId = plan.Id,
                ActionCode = 3, ActorUserId = 10, ClientRequestId = Guid.NewGuid()
            });
        var rule = new DiscountRule {
            TenantId = 41, FeeHeadId = 1, Name = "Tuition award",
            IsPercentage = true, Value = 20m, IsActive = true
        };
        db.DiscountRules.Add(rule);
        await db.SaveChangesAsync();
        db.StudentDiscounts.Add(new StudentDiscount {
            TenantId = 41, StudentEnrollmentId = 1, DiscountRuleId = rule.Id,
            EffectiveFrom = plan.StartMonth, ApprovedByUserId = 10, IsActive = true
        });
        await db.SaveChangesAsync();
        var request = new IssueStudentFeeInstallmentMonthDto {
            ClientRequestId = Guid.NewGuid(), MonthStart = plan.StartMonth,
            RowVersion = Convert.ToBase64String(plan.RowVersion)
        };
        var service = Service(db);
        var quote = await service.PreviewInstallmentMonthAsync(plan.PublicId, request);
        quote.StatusCode.Should().Be(200);
        quote.Data!.GrossAmount.Should().Be(100m);
        quote.Data.DiscountAmount.Should().Be(20m);
        quote.Data.NetAmount.Should().Be(80m);
        quote.Data.DueDate.Should().Be(new DateOnly(2026, 2, 28));
        quote.Data.Lines.Single().NetAmount.Should().Be(80m);
        (await db.StudentInvoices.CountAsync()).Should().Be(0);
        (await db.StudentPayments.CountAsync()).Should().Be(0);
        (await db.Journals.CountAsync()).Should().Be(0);
        (await Service(db, "Student").PreviewInstallmentMonthAsync(plan.PublicId, request))
            .StatusCode.Should().Be(403);
        (await Service(db).PreviewInstallmentMonthAsync(Guid.NewGuid(), request))
            .StatusCode.Should().Be(404);
        var issued = await service.IssueInstallmentMonthAsync(plan.PublicId, request);
        issued.StatusCode.Should().Be(200);
        issued.Data!.NetAmount.Should().Be(quote.Data.NetAmount);
        issued.Data.DiscountAmount.Should().Be(quote.Data.DiscountAmount);
        (await service.PreviewInstallmentMonthAsync(plan.PublicId, request))
            .StatusCode.Should().Be(409);
        (await db.StudentInvoices.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Cash_preflight_and_collection_reject_cross_campus_receipts_without_writes()
    {
        await using var db = Database();
        Seed(db);
        db.Campuses.Add(new Campus { Id = 2, TenantId = 41, Code = "C2", Name = "Second" });
        db.StudentEnrollments.Add(new StudentEnrollment {
            Id = 2, TenantId = 41, StudentId = 1, CampusId = 2,
            AcademicYearId = 2, AcademicBatchId = 1, RollNo = "02"
        });
        var first = new StudentInvoice {
            TenantId = 41, StudentEnrollmentId = 1, InvoiceNumber = "INV-A",
            InvoiceDate = new DateOnly(2026, 2, 1), DueDate = new DateOnly(2026, 2, 28),
            TotalAmount = 100m, DueAmount = 100m, CurrencyCode = "BDT"
        };
        var second = new StudentInvoice {
            TenantId = 41, StudentEnrollmentId = 2, InvoiceNumber = "INV-B",
            InvoiceDate = new DateOnly(2026, 2, 1), DueDate = new DateOnly(2026, 2, 28),
            TotalAmount = 100m, DueAmount = 100m, CurrencyCode = "BDT"
        };
        db.StudentInvoices.AddRange(first, second);
        await db.SaveChangesAsync();
        // Simulate a fresh HTTP request: production DbContexts do not retain
        // the invoice seed instances when the receipt command attaches updates.
        db.ChangeTracker.Clear();
        var request = new CreateStudentPaymentRequestDto {
            ClientRequestId = Guid.NewGuid(),
            PaymentDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
            PaymentMethod = PaymentMethodType.Cash, Amount = 50m,
            Allocations = [
                new CreatePaymentAllocationRequestDto {
                    InvoiceReference = first.PublicId, Amount = 25m
                },
                new CreatePaymentAllocationRequestDto {
                    InvoiceReference = second.PublicId, Amount = 25m
                }
            ]
        };
        var service = Service(db);
        (await service.PreviewCashCollectionAsync(request)).StatusCode.Should().Be(409);
        (await service.CollectPaymentAsync(request)).StatusCode.Should().Be(409);
        (await db.StudentPayments.CountAsync()).Should().Be(0);
        (await db.PaymentAllocations.CountAsync()).Should().Be(0);
        (await db.StudentInvoices.SumAsync(x => x.DueAmount)).Should().Be(200m);
        request.Allocations = [
            new CreatePaymentAllocationRequestDto {
                InvoiceReference = first.PublicId, Amount = 50m
            }
        ];
        var quote = await service.PreviewCashCollectionAsync(request);
        quote.StatusCode.Should().Be(200);
        quote.Data!.Invoices.Single().DueAfter.Should().Be(50m);
        (await db.StudentPayments.CountAsync()).Should().Be(0);
        (await service.CollectPaymentAsync(request)).StatusCode.Should().Be(200);
        var replayQuote = await service.PreviewCashCollectionAsync(request);
        replayQuote.StatusCode.Should().Be(200);
        replayQuote.Data!.AlreadyRecorded.Should().BeTrue();
        (await service.CollectPaymentAsync(request)).StatusCode.Should().Be(200);
        (await db.StudentPayments.CountAsync()).Should().Be(1);
        (await db.PaymentAllocations.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Cash_preflight_denies_foreign_invoice_and_ungranted_campus()
    {
        await using var db = Database();
        Seed(db);
        db.StudentInvoices.Add(new StudentInvoice {
            TenantId = 41, StudentEnrollmentId = 1, InvoiceNumber = "INV-1",
            InvoiceDate = new DateOnly(2026, 2, 1), DueDate = new DateOnly(2026, 2, 28),
            TotalAmount = 100m, DueAmount = 100m
        });
        await db.SaveChangesAsync();
        var invoice = await db.StudentInvoices.SingleAsync();
        var request = new CreateStudentPaymentRequestDto {
            ClientRequestId = Guid.NewGuid(),
            PaymentDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
            PaymentMethod = PaymentMethodType.Cash, Amount = 20m,
            Allocations = [new CreatePaymentAllocationRequestDto {
                InvoiceReference = invoice.PublicId, Amount = 20m
            }]
        };
        (await Service(db, "Student").PreviewCashCollectionAsync(request))
            .StatusCode.Should().Be(403);
        (await Service(db, "Accountant").PreviewCashCollectionAsync(request))
            .StatusCode.Should().Be(404);
        request.Allocations = [new CreatePaymentAllocationRequestDto {
            InvoiceReference = Guid.NewGuid(), Amount = 20m
        }];
        (await Service(db).PreviewCashCollectionAsync(request))
            .StatusCode.Should().Be(404);
        (await db.StudentPayments.CountAsync()).Should().Be(0);
    }

    private static CreateStudentFeeInstallmentPlanDto Request(EduOSDbContext db) => new() {
        ClientRequestId = Guid.NewGuid(),
        StudentReference = db.Students.Single().PublicId,
        AcademicBatchId = 1, FeeStructureId = 1,
        StartMonth = new DateOnly(2026, 2, 1), Months = 3
    };

    private static void Seed(EduOSDbContext db)
    {
        db.Tenants.Add(new Tenant { Id = 41, Name = "Institute", Code = "INST-41" });
        db.Campuses.Add(new Campus { Id = 1, TenantId = 41, Code = "C1", Name = "Main" });
        db.AcademicYears.Add(new AcademicYear {
            Id = 2, TenantId = 41, Name = "2026", Code = "2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31)
        });
        db.AcademicBatches.Add(new AcademicBatch {
            Id = 1, TenantId = 41, CampusId = 1, AcademicYearId = 2,
            AcademicProgramId = 3, AcademicLevelId = 4, Code = "B1", Name = "Batch"
        });
        db.Students.Add(new Student {
            Id = 1, TenantId = 41, PersonId = 1, StudentCode = "ST-1",
            FullName = "Learner", StatusCode = "Active"
        });
        db.StudentEnrollments.Add(new StudentEnrollment {
            Id = 1, TenantId = 41, StudentId = 1, CampusId = 1,
            AcademicYearId = 2, AcademicBatchId = 1, AcademicProgramId = 3,
            AcademicLevelId = 4, AcademicCurriculumId = 5, RollNo = "01",
            IsCurrent = true, State = EnrollmentState.Active
        });
        db.FeeStructures.Add(new FeeStructure {
            Id = 1, TenantId = 41, CampusId = 1, AcademicYearId = 2,
            AcademicBatchId = 1, Name = "Monthly", CurrencyCode = "BDT",
            EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        db.FeeHeads.Add(new FeeHead { Id = 1, TenantId = 41, Code = "TUITION", Name = "Tuition" });
        db.FeeStructureLines.Add(new FeeStructureLine {
            Id = 1, TenantId = 41, FeeStructureId = 1, FeeHeadId = 1,
            Frequency = FeeFrequencyType.Monthly, Amount = 100m, DueDayOfMonth = 31
        });
        db.SaveChanges();
    }

    private static FeeBillingService Service(EduOSDbContext db, string role = "TenantAdmin",
        long userId = 9) => new(
        new GenericRepository<FeeStructure>(db), new GenericRepository<FeeStructureLine>(db),
        new GenericRepository<FeeHead>(db), new GenericRepository<StudentDiscount>(db),
        new GenericRepository<DiscountRule>(db), new GenericRepository<StudentInvoice>(db),
        new GenericRepository<StudentInvoiceLine>(db), new GenericRepository<StudentPayment>(db),
        new GenericRepository<PaymentAllocation>(db), new GenericRepository<StudentEnrollment>(db),
        new GenericRepository<Student>(db), new GenericRepository<AcademicYear>(db),
        new GenericRepository<AcademicLevel>(db), new GenericRepository<AcademicBatch>(db),
        new GenericRepository<Campus>(db), new GenericRepository<Tenant>(db),
        db, new User(role, userId), TimeProvider.System, NullLogger<FeeBillingService>.Instance);

    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("fee-plan-" + Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, accessor);
    }
    private sealed class User(string role, long userId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => 41;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => value == role;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
