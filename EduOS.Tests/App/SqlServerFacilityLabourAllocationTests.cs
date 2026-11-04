using EduOS.Core.DTOs.Facilities;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Facilities;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.Payroll;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Facilities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.App;

[Collection("SqlServerIntegration")]
public sealed class SqlServerFacilityLabourAllocationTests
{
    [Fact]
    public async Task Posted_payroll_can_be_allocated_once_without_duplicate_salary_GL()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long tenantId, otherTenantId, campusId, workOrderId, payrollEmployeeId;
        long accountantId, principalId, techId, requesterId, runId, payrollJournalId;
        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "CI-FLC-" + suffix, Name = "Labour Cost CI",
                Email = "labour-" + suffix + "@example.test", State = TenantState.Active };
            var other = new Tenant { Code = "CI-FLO-" + suffix, Name = "Other Labour CI",
                Email = "labour-other-" + suffix + "@example.test", State = TenantState.Active };
            var users = Enumerable.Range(1, 4).Select(n => {
                var email = "labour-" + n + "-" + suffix + "@example.test";
                return new ApplicationUser { UserName = email, NormalizedUserName = email.ToUpperInvariant(),
                    Email = email, NormalizedEmail = email.ToUpperInvariant(),
                    FullName = "Labour cost test actor", IsActive = true, EmailConfirmed = true,
                    SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString() };
            }).ToArray();
            setup.AddRange(tenant, other); setup.AddRange(users);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id; otherTenantId = other.Id;
            accountantId = users[0].Id; principalId = users[1].Id;
            techId = users[2].Id; requesterId = users[3].Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Code = "FLC-" + suffix,
                    Name = "Maintenance Labour Campus" };
                setup.Campuses.Add(campus);
                await setup.SaveChangesAsync();
                campusId = campus.Id;
                var space = new FacilitySpace { TenantId = tenantId, CampusId = campusId,
                    Code = "SPACE-" + suffix, Name = "Maintenance Utility Space" };
                var designation = new Designation { TenantId = tenantId,
                    Code = "ELEC-" + suffix, Name = "Facilities Electrical Technician" };
                var person = new Person { FullName = "Maintenance Technician CI" };
                var accountantRole = new ApplicationRole { TenantId = tenantId,
                    Name = "Accountant", NormalizedName = "ACCOUNTANT", IsActive = true };
                var principalRole = new ApplicationRole { TenantId = tenantId,
                    Name = "Principal", NormalizedName = "PRINCIPAL", IsActive = true };
                setup.AddRange(space, designation, person, accountantRole, principalRole);
                foreach (var id in new[] { accountantId, principalId, techId, requesterId })
                {
                    setup.TenantMemberships.Add(new TenantMembership {
                        TenantId = tenantId, UserId = id, Status = MembershipStatus.Active });
                    setup.UserCampusAccesses.Add(new UserCampusAccess {
                        TenantId = tenantId, CampusId = campusId, UserId = id, IsActive = true });
                }
                await setup.SaveChangesAsync();
                setup.UserRoles.AddRange(
                    new IdentityUserRole<long> { UserId = accountantId, RoleId = accountantRole.Id },
                    new IdentityUserRole<long> { UserId = principalId, RoleId = principalRole.Id });
                var employee = new Employee { TenantId = tenantId, PersonId = person.Id,
                    UserId = techId, DesignationId = designation.Id,
                    EmployeeCode = "EM-" + suffix, FullName = person.FullName,
                    JoiningDate = day.AddDays(-100), State = EmployeeState.Active };
                setup.Employees.Add(employee);
                var period = new AccountingPeriod { TenantId = tenantId,
                    Name = "Payroll period " + suffix, StartDate = day.AddDays(-20),
                    EndDate = day.AddDays(20), IsClosed = false };
                var expense = new Account { TenantId = tenantId, Code = "PAY-EX-" + suffix,
                    Name = "Payroll expense", AccountType = AccountType.Expense,
                    IsActive = true, AllowsPosting = true };
                var payable = new Account { TenantId = tenantId, Code = "PAY-AP-" + suffix,
                    Name = "Payroll payable", AccountType = AccountType.Liability,
                    IsActive = true, AllowsPosting = true };
                setup.AddRange(period, expense, payable);
                await setup.SaveChangesAsync();
                var run = new PayrollRun { TenantId = tenantId,
                    ClientRequestId = Guid.NewGuid(), RunNumber = "PAY-" + suffix,
                    CampusId = campusId, Year = day.Year, Month = day.Month,
                    PeriodStart = day.AddDays(-3), PeriodEnd = day.AddDays(3),
                    State = PayrollRunState.Draft };
                setup.PayrollRuns.Add(run);
                await setup.SaveChangesAsync();
                runId = run.Id;
                var payroll = new PayrollEmployee { TenantId = tenantId,
                    PayrollRunId = run.Id, EmployeeId = employee.Id,
                    EmployeeCodeSnapshot = employee.EmployeeCode,
                    EmployeeNameSnapshot = employee.FullName,
                    GrossAmount = 10000m, DeductionAmount = 0m, NetAmount = 10000m };
                setup.PayrollEmployees.Add(payroll);
                var journal = new Journal { TenantId = tenantId,
                    PublicId = Guid.NewGuid(), IdempotencyKey = Guid.NewGuid(),
                    JournalNumber = "JRN-" + Guid.NewGuid().ToString("N"),
                    JournalDate = day, AccountingPeriodId = period.Id,
                    CampusId = campusId, CurrencyCode = "BDT", State = JournalState.Posted,
                    SourceType = "PayrollRun", SourceId = run.Id,
                    ApprovedByUserId = principalId, ApprovedAt = DateTime.UtcNow,
                    PostedByUserId = principalId, PostedAt = DateTime.UtcNow };
                setup.Journals.Add(journal);
                await setup.SaveChangesAsync();
                payrollEmployeeId = payroll.Id; payrollJournalId = journal.Id;
                setup.JournalLines.AddRange(
                    new JournalLine { TenantId = tenantId, JournalId = journal.Id,
                        AccountId = expense.Id, CampusId = campusId, LineNo = 1,
                        DebitAmount = 10000m, CreditAmount = 0m },
                    new JournalLine { TenantId = tenantId, JournalId = journal.Id,
                        AccountId = payable.Id, CampusId = campusId, LineNo = 2,
                        DebitAmount = 0m, CreditAmount = 10000m });
                run.JournalId = journal.Id;
                run.State = PayrollRunState.Posted;
                run.ApprovedByUserId = principalId;
                run.ApprovedAt = DateTime.UtcNow;
                run.PostedAt = DateTime.UtcNow;
                var serviceRequest = new FacilityServiceRequest {
                    TenantId = tenantId, CampusId = campusId, FacilitySpaceId = space.Id,
                    ClientRequestId = Guid.NewGuid(), RequesterUserId = requesterId,
                    CategoryCode = "ELECTRICAL", PriorityCode = "Normal",
                    Summary = "Fix campus electrical distribution board", StateCode = "Approved",
                    ApprovedByUserId = principalId, ApprovedAtUtc = DateTime.UtcNow };
                setup.FacilityServiceRequests.Add(serviceRequest);
                setup.TenantSettings.Add(new TenantSetting { TenantId = tenantId,
                    Key = "Facilities.PayrollStandardMinutes." + campusId,
                    Value = "9600", Category = "Facilities", CreatedAt = DateTime.UtcNow,
                    CreatedBy = accountantId });
                await setup.SaveChangesAsync();
                var order = new FacilityWorkOrder { TenantId = tenantId,
                    FacilityServiceRequestId = serviceRequest.Id, StateCode = "Closed",
                    AssignedTechnicianUserId = techId, StartedAtUtc = DateTime.UtcNow.AddHours(-4),
                    CompletedAtUtc = DateTime.UtcNow.AddHours(-1),
                    VerifiedAtUtc = DateTime.UtcNow.AddMinutes(-40),
                    VerifiedByUserId = principalId };
                setup.FacilityWorkOrders.Add(order);
                await setup.SaveChangesAsync();
                workOrderId = order.Id;
            }
        }
        var request = new CreateFacilityLabourAllocationDto {
            ClientRequestId = Guid.NewGuid(), FacilityWorkOrderId = workOrderId,
            PayrollEmployeeId = payrollEmployeeId, ActualWorkMinutes = 120,
            Evidence = "Technician spent two productive hours repairing the panel."
        };
        FacilityLabourAllocationDto allocation;
        await using (var db = Db(options, tenantId, accountantId, "Accountant"))
        {
            var service = Service(db, tenantId, accountantId, "Accountant");
            var submitted = await service.CreateAsync(request);
            Assert.True(submitted.Success, submitted.Message);
            allocation = submitted.Data!;
            Assert.Equal("Submitted", allocation.StateCode);
            Assert.Equal(125m, allocation.AllocatedAmountBdt);
            Assert.Equal(10000m, allocation.PayrollGrossSnapshotBdt);
            Assert.True((await service.CreateAsync(request)).Success);
            Assert.Equal(409, (await service.CreateAsync(new CreateFacilityLabourAllocationDto {
                ClientRequestId = Guid.NewGuid(), FacilityWorkOrderId = workOrderId,
                PayrollEmployeeId = payrollEmployeeId, ActualWorkMinutes = 120,
                Evidence = "Second allocation must be rejected as a duplicate."
            })).StatusCode);
            Assert.Equal(403, (await service.ReviewAsync(allocation.Reference,
                new ReviewFacilityLabourAllocationDto {
                    ClientRequestId = Guid.NewGuid(), RowVersion = allocation.RowVersion,
                    Approve = true, Reason = "Same accountant cannot self approve." })).StatusCode);
        }
        await using (var db = Db(options, otherTenantId, accountantId, "Accountant"))
        {
            Assert.Equal(404, (await Service(db, otherTenantId, accountantId, "Accountant")
                .GetAsync(allocation.Reference)).StatusCode);
        }
        await using (var db = Db(options, tenantId, principalId, "Principal"))
        {
            var service = Service(db, tenantId, principalId, "Principal");
            var review = new ReviewFacilityLabourAllocationDto {
                ClientRequestId = Guid.NewGuid(), RowVersion = allocation.RowVersion,
                Approve = true, Reason = "Checked posted payroll and actual maintenance time."
            };
            var approved = await service.ReviewAsync(allocation.Reference, review);
            Assert.True(approved.Success, approved.Message);
            allocation = approved.Data!;
            Assert.Equal("Approved", allocation.StateCode);
            Assert.True((await service.ReviewAsync(allocation.Reference, review)).Success);
            var rows = await service.ListAsync(campusId, 1, 25);
            Assert.True(rows.Success, rows.Message);
            Assert.Contains(rows.Data!.Items, x => x.Reference == allocation.Reference);
            Assert.False(rows.Data.Items.Single(x => x.Reference == allocation.Reference).PayrollSourceReversed);
            Assert.Equal(1, await db.Journals.CountAsync(x => x.TenantId == tenantId));
            Assert.Equal(2, await db.JournalLines.CountAsync(x => x.TenantId == tenantId));
            Assert.Equal(10000m, await db.JournalLines.Where(x =>
                x.TenantId == tenantId && x.DebitAmount > 0m).SumAsync(x => x.DebitAmount));
        }
        // Source reversal invalidates the current cost presentation, but does
        // not erase original historical allocation or silently post new GL.
        await using (var db = Db(options, tenantId, principalId, "Principal"))
        {
            db.Journals.Add(new Journal { TenantId = tenantId, PublicId = Guid.NewGuid(),
                JournalNumber = "REV-" + Guid.NewGuid().ToString("N"),
                JournalDate = day, AccountingPeriodId = (await db.Journals.SingleAsync(x =>
                    x.Id == payrollJournalId)).AccountingPeriodId,
                CampusId = campusId, SourceType = "PayrollRunReversal",
                SourceId = payrollJournalId, ReversalOfJournalId = payrollJournalId,
                State = JournalState.Posted, CurrencyCode = "BDT" });
            await db.SaveChangesAsync();
            var row = await Service(db, tenantId, principalId, "Principal")
                .GetAsync(allocation.Reference);
            Assert.True(row.Success, row.Message);
            Assert.True(row.Data!.PayrollSourceReversed);
        }
    }

    private static EduOSDbContext Db(DbContextOptions<EduOSDbContext> options,
        long tenantId, long actorId, string role)
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, actorId.ToString()),
             new Claim(ClaimTypes.Role, role)], "Test")) };
        http.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }
    private static FacilityLabourCostService Service(EduOSDbContext db,
        long tenantId, long actorId, string role) =>
        new(db, new Principal(tenantId, actorId, role), TimeProvider.System,
            NullLogger<FacilityLabourCostService>.Instance);
    private sealed class Principal(long tenant, long actorId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => actorId;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string candidate) => candidate == role;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
