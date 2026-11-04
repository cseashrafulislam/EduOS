using EduOS.Core.DTOs.Facilities;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Facilities;
using EduOS.Core.Entities.Inventory;
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
public sealed class SqlServerFacilityVendorBillingTests
{
    [Fact]
    public async Task Verified_nonstock_service_posts_once_and_independent_contra_preserves_GL()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long tenantId, outsiderTenantId, campusId, workOrderId, makerId, checkerId, requesterId;
        long periodId, expenseId, payableId, bankId;
        var invoiceDate = DateOnly.FromDateTime(DateTime.UtcNow);
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "CI-FVB-" + suffix, Name = "Nonstock Facilities CI",
                Email = "fvb-" + suffix + "@example.test", State = TenantState.Active };
            var other = new Tenant { Code = "CI-FVO-" + suffix, Name = "Other Finance CI",
                Email = "fvo-" + suffix + "@example.test", State = TenantState.Active };
            var people = Enumerable.Range(1, 3).Select(n => {
                var email = "fvb-" + n + "-" + suffix + "@example.test";
                return new ApplicationUser { UserName = email,
                    NormalizedUserName = email.ToUpperInvariant(), Email = email,
                    NormalizedEmail = email.ToUpperInvariant(), FullName = "Facilities Finance CI",
                    EmailConfirmed = true, IsActive = true,
                    SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString() };
            }).ToArray();
            setup.AddRange(tenant, other); setup.AddRange(people);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id; outsiderTenantId = other.Id;
            makerId = people[0].Id; checkerId = people[1].Id; requesterId = people[2].Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Code = "FVB-" + suffix,
                    Name = "Vendor Service Campus" };
                setup.Campuses.Add(campus);
                await setup.SaveChangesAsync();
                campusId = campus.Id;
                var space = new FacilitySpace { TenantId = tenantId, CampusId = campusId,
                    Code = "SPACE-" + suffix, Name = "Generator Workshop" };
                var supplier = new ProcurementSupplier { TenantId = tenantId, CampusId = campusId,
                    ClientRequestId = Guid.NewGuid(), SupplierCode = "SUP-" + suffix,
                    Name = "Electrical service provider", StateCode = "Approved" };
                var expense = new Account { TenantId = tenantId, Code = "EX-" + suffix,
                    Name = "Facility external repair expense", AccountType = AccountType.Expense,
                    IsActive = true, AllowsPosting = true, CurrencyCode = "BDT" };
                var payable = new Account { TenantId = tenantId, Code = "AP-" + suffix,
                    Name = "Supplier trade payable", AccountType = AccountType.Liability,
                    IsActive = true, AllowsPosting = true, CurrencyCode = "BDT" };
                var bankLedger = new Account { TenantId = tenantId, Code = "BNK-" + suffix,
                    Name = "Vendor settlement bank", AccountType = AccountType.Asset,
                    IsActive = true, AllowsPosting = true, CurrencyCode = "BDT" };
                setup.Accounts.Add(bankLedger);
                await setup.SaveChangesAsync();
                var bank = new BankAccount { TenantId = tenantId, CampusId = campusId,
                    AccountId = bankLedger.Id, BankName = "CI Settlement Bank",
                    AccountName = "Facilities Disbursements", AccountNumber = "TEST-" + suffix,
                    IsActive = true, CurrencyCode = "BDT" };
                setup.BankAccounts.Add(bank);
                var period = new AccountingPeriod { TenantId = tenantId,
                    Name = "Facility test period " + suffix,
                    StartDate = invoiceDate.AddDays(-10), EndDate = invoiceDate.AddDays(20),
                    IsClosed = false };
                setup.AddRange(space, supplier, expense, payable, period);
                var accountantRole = new ApplicationRole { TenantId = tenantId,
                    Name = "Accountant", NormalizedName = "ACCOUNTANT", IsActive = true };
                var principalRole = new ApplicationRole { TenantId = tenantId,
                    Name = "Principal", NormalizedName = "PRINCIPAL", IsActive = true };
                setup.Roles.AddRange(accountantRole, principalRole);
                foreach (var person in people)
                {
                    setup.TenantMemberships.Add(new TenantMembership {
                        TenantId = tenantId, UserId = person.Id, Status = MembershipStatus.Active });
                    setup.UserCampusAccesses.Add(new UserCampusAccess {
                        TenantId = tenantId, UserId = person.Id, CampusId = campusId, IsActive = true });
                }
                await setup.SaveChangesAsync();
                expenseId = expense.Id; payableId = payable.Id; periodId = period.Id;
                bankId = bank.Id;
                setup.UserRoles.AddRange(
                    new IdentityUserRole<long> { UserId = makerId, RoleId = accountantRole.Id },
                    new IdentityUserRole<long> { UserId = checkerId, RoleId = principalRole.Id });
                var request = new FacilityServiceRequest { TenantId = tenantId,
                    CampusId = campusId, FacilitySpaceId = space.Id, ClientRequestId = Guid.NewGuid(),
                    RequesterUserId = requesterId, CategoryCode = "REPAIR", PriorityCode = "Normal",
                    Summary = "Generator coil repair", StateCode = "Approved",
                    ApprovedByUserId = checkerId, ApprovedAtUtc = DateTime.UtcNow };
                setup.FacilityServiceRequests.Add(request);
                await setup.SaveChangesAsync();
                var order = new FacilityWorkOrder { TenantId = tenantId,
                    FacilityServiceRequestId = request.Id, StateCode = "Closed",
                    VendorSupplierId = supplier.Id, VendorStateCode = "Approved",
                    VendorProposedByUserId = makerId, VendorApprovedByUserId = checkerId,
                    VendorStartUtc = DateTime.UtcNow.AddHours(-6),
                    VendorEndUtc = DateTime.UtcNow.AddHours(-1),
                    StartedAtUtc = DateTime.UtcNow.AddHours(-5),
                    CompletedAtUtc = DateTime.UtcNow.AddHours(-3),
                    VerifiedAtUtc = DateTime.UtcNow.AddHours(-2),
                    VerifiedByUserId = checkerId };
                setup.FacilityWorkOrders.Add(order);
                setup.FacilityWorkflowEvents.AddRange(
                    new FacilityWorkflowEvent { TenantId = tenantId,
                        FacilityServiceRequestId = request.Id, ClientCommandId = Guid.NewGuid(),
                        ActorUserId = requesterId, ActionCode = "complete",
                        FromState = "InProgress", ToState = "PendingVerification",
                        Note = "Service technician completed work.", CreatedAt = DateTime.UtcNow },
                    new FacilityWorkflowEvent { TenantId = tenantId,
                        FacilityServiceRequestId = request.Id, ClientCommandId = Guid.NewGuid(),
                        ActorUserId = makerId, ActionCode = "vendorattest",
                        FromState = "PendingVerification", ToState = "PendingVerification",
                        Note = "Supplier certificate submitted for inspection.", CreatedAt = DateTime.UtcNow },
                    new FacilityWorkflowEvent { TenantId = tenantId,
                        FacilityServiceRequestId = request.Id, ClientCommandId = Guid.NewGuid(),
                        ActorUserId = checkerId, ActionCode = "vendorverify",
                        FromState = "PendingVerification", ToState = "PendingVerification",
                        Note = "Independent vendor service verification.", CreatedAt = DateTime.UtcNow });
                await setup.SaveChangesAsync();
                workOrderId = order.Id;
            }
        }
        FacilityVendorBillDto bill;
        var createId = Guid.NewGuid();
        var invoiceNumber = "SERVICE-" + suffix;
        var create = new CreateFacilityVendorServiceBillDto {
            ClientRequestId = createId, FacilityWorkOrderId = workOrderId,
            SupplierInvoiceNumber = invoiceNumber, InvoiceDate = invoiceDate,
            DueDate = invoiceDate.AddDays(15), AmountBdt = 1200m,
            Reason = "Vendor completed the independently verified maintenance service."
        };
        await using (var db = Db(options, tenantId, makerId, "Accountant"))
        {
            var service = Service(db, tenantId, makerId, "Accountant");
            var made = await service.CreateAsync(create);
            Assert.True(made.Success, made.Message);
            bill = made.Data!;
            Assert.Equal(1200m, bill.AmountBdt);
            Assert.Equal("Draft", bill.StateCode);
            var repeated = await service.CreateAsync(create);
            Assert.True(repeated.Success, repeated.Message);
            Assert.Equal(bill.Reference, repeated.Data!.Reference);
            Assert.Equal(409, (await service.CreateAsync(new CreateFacilityVendorServiceBillDto {
                ClientRequestId = Guid.NewGuid(), FacilityWorkOrderId = workOrderId,
                SupplierInvoiceNumber = "ALT-" + suffix, InvoiceDate = invoiceDate,
                DueDate = invoiceDate, AmountBdt = 1250m, Reason = create.Reason
            })).StatusCode);
            var submit = new FacilityVendorBillSubmitDto {
                ClientRequestId = Guid.NewGuid(), RowVersion = bill.RowVersion
            };
            var sent = await service.SubmitAsync(bill.Reference, submit);
            Assert.True(sent.Success, sent.Message);
            bill = sent.Data!;
            Assert.Equal("Submitted", bill.StateCode);
            Assert.True((await service.SubmitAsync(bill.Reference, submit)).Success);
            Assert.Equal(403, (await service.ReviewAsync(bill.Reference,
                new FacilityVendorBillReviewDto { ClientRequestId = Guid.NewGuid(),
                    RowVersion = bill.RowVersion, Approve = true,
                    Reason = "Maker cannot approve the same invoice",
                    AccountingPeriodId = periodId, RepairsExpenseAccountId = expenseId,
                    SupplierPayableAccountId = payableId })).StatusCode);
        }
        await using (var db = Db(options, outsiderTenantId, makerId, "Accountant"))
            Assert.Equal(404, (await Service(db, outsiderTenantId, makerId, "Accountant")
                .GetAsync(bill.Reference)).StatusCode);
        await using (var db = Db(options, tenantId, checkerId, "Principal"))
        {
            var service = Service(db, tenantId, checkerId, "Principal");
            var decision = new FacilityVendorBillReviewDto {
                ClientRequestId = Guid.NewGuid(), RowVersion = bill.RowVersion,
                Approve = true, Reason = "Checked the work and source invoice against evidence.",
                AccountingPeriodId = periodId, RepairsExpenseAccountId = expenseId,
                SupplierPayableAccountId = payableId
            };
            var posted = await service.ReviewAsync(bill.Reference, decision);
            Assert.True(posted.Success, posted.Message);
            bill = posted.Data!;
            Assert.Equal("Posted", bill.StateCode);
            Assert.NotNull(bill.JournalReference);
            Assert.True((await service.ReviewAsync(bill.Reference, decision)).Success);
            var journal = await db.Journals.AsNoTracking().SingleAsync(x =>
                x.TenantId == tenantId && x.PublicId == bill.JournalReference);
            Assert.Equal("FacilityVendorServiceBill", journal.SourceType);
            Assert.Equal(JournalState.Posted, journal.State);
            var lines = await db.JournalLines.AsNoTracking().Where(x => x.TenantId == tenantId &&
                x.JournalId == journal.Id).OrderBy(x => x.LineNo).ToListAsync();
            Assert.Equal(2, lines.Count);
            Assert.Equal(1200m, lines.Sum(x => x.DebitAmount));
            Assert.Equal(1200m, lines.Sum(x => x.CreditAmount));
            Assert.Equal(expenseId, lines[0].AccountId);
            Assert.Equal(payableId, lines[1].AccountId);
        }
        // Payment instruction is NOT a bank transfer; only independently
        // approved and externally confirmed payment reduces payable aging.
        FacilityVendorPaymentDto payment;
        await using (var db = Db(options, tenantId, makerId, "Accountant"))
        {
            var service = Service(db, tenantId, makerId, "Accountant");
            var optionsResult = await service.PaymentOptionsAsync(bill.Reference);
            Assert.True(optionsResult.Success, optionsResult.Message);
            Assert.Equal(1200m, optionsResult.Data!.AvailableAmount);
            var instruction = new FacilityVendorPaymentInstructionRequest {
                ClientRequestId = Guid.NewGuid(), BillRowVersion = bill.RowVersion,
                BankAccountId = bankId, AccountingPeriodId = periodId,
                PlannedDate = invoiceDate, Amount = 400m,
                Reason = "Approved supplier invoice scheduled for partial bank disbursement."
            };
            var created = await service.CreatePaymentAsync(bill.Reference, instruction);
            Assert.True(created.Success, created.Message);
            payment = created.Data!;
            Assert.Equal("Submitted", payment.State);
            Assert.False(payment.BankStatementCleared);
            Assert.True((await service.CreatePaymentAsync(bill.Reference, instruction)).Success);
            Assert.Equal(800m, (await service.PaymentOptionsAsync(bill.Reference)).Data!.AvailableAmount);
            // A bill's remaining payable cannot be reserved twice when two
            // separate Finance requests arrive concurrently on real SQL Server.
            // The rejected attempt must leave no active reservation or GL row.
            async Task<FacilityVendorPaymentDto?> ReserveRemainingAsync(Guid commandId)
            {
                await using var concurrentDb = Db(options, tenantId, makerId, "Accountant");
                var concurrentService = Service(concurrentDb, tenantId, makerId, "Accountant");
                var attempted = await concurrentService.CreatePaymentAsync(bill.Reference,
                    new FacilityVendorPaymentInstructionRequest {
                        ClientRequestId = commandId, BillRowVersion = bill.RowVersion,
                        BankAccountId = bankId, AccountingPeriodId = periodId,
                        PlannedDate = invoiceDate, Amount = 800m,
                        Reason = "Concurrent reservation for the same verified vendor payable."
                    });
                Assert.Contains(attempted.StatusCode, new[] { 200, 409 });
                return attempted.Success ? attempted.Data : null;
            }
            var concurrent = await Task.WhenAll(
                Task.Run(() => ReserveRemainingAsync(Guid.NewGuid())),
                Task.Run(() => ReserveRemainingAsync(Guid.NewGuid())));
            var reservedPayment = Assert.Single(concurrent, x => x != null)!;
            // IHttpContextAccessor uses ambient AsyncLocal state. Parallel
            // fixture contexts intentionally must not be followed by reads
            // through the earlier request's accessor; open a fresh request.
            await using (var readbackDb = Db(options, tenantId, makerId, "Accountant"))
            {
                var reservedBalance = await Service(readbackDb, tenantId, makerId, "Accountant")
                    .PaymentOptionsAsync(bill.Reference);
                Assert.True(reservedBalance.Success, reservedBalance.Message);
                Assert.Equal(0m, reservedBalance.Data!.AvailableAmount);
            }
            await using (var foreignDb = Db(options, outsiderTenantId, makerId, "Accountant"))
            {
                var foreignService = Service(foreignDb, outsiderTenantId, makerId, "Accountant");
                Assert.False((await foreignService.PaymentOptionsAsync(bill.Reference)).Success);
                Assert.False((await foreignService.PaymentsAsync(bill.Reference, 1, 25)).Success);
            }
            await using (var reviewerDb = Db(options, tenantId, checkerId, "Principal"))
            {
                var declined = await Service(reviewerDb, tenantId, checkerId, "Principal")
                    .ReviewPaymentAsync(bill.Reference, reservedPayment.Reference,
                        new FacilityVendorPaymentReviewRequest {
                            RowVersion = reservedPayment.RowVersion, Approve = false,
                            Reason = "Duplicate pending disbursement cancelled before execution."
                        });
                Assert.True(declined.Success, declined.Message);
                Assert.Equal("Rejected", declined.Data!.State);
            }
            await using (var readbackDb = Db(options, tenantId, makerId, "Accountant"))
            {
                var readbackService = Service(readbackDb, tenantId, makerId, "Accountant");
                var available = await readbackService.PaymentOptionsAsync(bill.Reference);
                Assert.True(available.Success, available.Message);
                Assert.Equal(800m, available.Data!.AvailableAmount);
                Assert.Equal(409, (await readbackService.CreatePaymentAsync(bill.Reference,
                    new FacilityVendorPaymentInstructionRequest {
                        ClientRequestId = Guid.NewGuid(), BillRowVersion = bill.RowVersion,
                        BankAccountId = bankId, AccountingPeriodId = periodId,
                        PlannedDate = invoiceDate, Amount = 900m,
                        Reason = "A second instruction must not overreserve the same payable."
                    })).StatusCode);
                Assert.Equal(409, (await readbackService.RequestReversalAsync(bill.Reference,
                    new FacilityVendorBillReversalRequestDto {
                        ClientRequestId = Guid.NewGuid(), RowVersion = bill.RowVersion,
                        ReversalDate = invoiceDate, AccountingPeriodId = periodId,
                        Reason = "Cannot cancel invoice with a reserved supplier payment."
                    })).StatusCode);
            }
        }
        await using (var db = Db(options, tenantId, checkerId, "Principal"))
        {
            var approved = await Service(db, tenantId, checkerId, "Principal")
                .ReviewPaymentAsync(bill.Reference, payment.Reference,
                    new FacilityVendorPaymentReviewRequest {
                        RowVersion = payment.RowVersion, Approve = true,
                        Reason = "Independent checker verified approved supplier bank details."
                    });
            Assert.True(approved.Success, approved.Message);
            payment = approved.Data!;
            Assert.Equal("Approved", payment.State);
        }
        await using (var db = Db(options, tenantId, makerId, "Accountant"))
        {
            var service = Service(db, tenantId, makerId, "Accountant");
            var post = new FacilityVendorPaymentPostRequest {
                ClientRequestId = Guid.NewGuid(), RowVersion = payment.RowVersion,
                ExecutedDate = invoiceDate, BankTransactionReference = "BANK-" + suffix
            };
            var postedPayment = await service.PostPaymentAsync(bill.Reference, payment.Reference, post);
            Assert.True(postedPayment.Success, postedPayment.Message);
            payment = postedPayment.Data!;
            Assert.Equal("Posted", payment.State);
            Assert.False(payment.BankStatementCleared);
            Assert.True((await service.PostPaymentAsync(bill.Reference, payment.Reference, post)).Success);
            var aging = await service.SupplierAgingAsync(invoiceDate, campusId, 1, 25);
            Assert.True(aging.Success, aging.Message);
            var due = aging.Data!.Page.Items.Single(x =>
                x.InvoiceSource == "Service" && x.InvoiceReference == bill.Reference);
            Assert.Equal(400m, due.NetPostedPayments);
            Assert.Equal(800m, due.OpenBalance);
            Assert.Equal(409, (await service.RequestReversalAsync(bill.Reference,
                new FacilityVendorBillReversalRequestDto {
                    ClientRequestId = Guid.NewGuid(), RowVersion = bill.RowVersion,
                    ReversalDate = invoiceDate, AccountingPeriodId = periodId,
                    Reason = "Must reverse posted bank disbursement before bill reversal."
                })).StatusCode);
            var refund = new FacilityVendorPaymentReversalRequest {
                ClientRequestId = Guid.NewGuid(), RowVersion = payment.RowVersion,
                ReversalDate = invoiceDate, AccountingPeriodId = periodId,
                BankReversalReference = "BANK-REFUND-" + suffix,
                Reason = "Bank transaction was refunded and confirmed by bank reference."
            };
            var reversal = await service.RequestPaymentReversalAsync(
                bill.Reference, payment.Reference, refund);
            Assert.True(reversal.Success, reversal.Message);
            payment = reversal.Data!;
            Assert.Equal("Submitted", payment.ReversalState);
        }
        await using (var db = Db(options, tenantId, checkerId, "Principal"))
        {
            var service = Service(db, tenantId, checkerId, "Principal");
            var approved = await service.ReviewPaymentReversalAsync(bill.Reference,
                payment.Reference, new FacilityVendorPaymentReversalReviewRequest {
                    RowVersion = payment.ReversalRowVersion!, Approve = true,
                    Reason = "Independently checked externally confirmed bank refund."
                });
            Assert.True(approved.Success, approved.Message);
            payment = approved.Data!;
            Assert.Equal("Posted", payment.ReversalState);
            var aging = await service.SupplierAgingAsync(invoiceDate, campusId, 1, 25);
            Assert.True(aging.Success, aging.Message);
            var due = aging.Data!.Page.Items.Single(x =>
                x.InvoiceSource == "Service" && x.InvoiceReference == bill.Reference);
            Assert.Equal(0m, due.NetPostedPayments);
            Assert.Equal(1200m, due.OpenBalance);
            var payments = await db.Journals.AsNoTracking().Where(x =>
                x.TenantId == tenantId && x.State == JournalState.Posted &&
                (x.SourceType == "FacilityVendorServicePayment" ||
                 x.SourceType == "FacilityVendorServicePaymentReversal"))
                .Select(x => x.Id).ToListAsync();
            Assert.Equal(2, payments.Count);
            var net = await db.JournalLines.AsNoTracking().Where(x =>
                x.TenantId == tenantId && payments.Contains(x.JournalId))
                .GroupBy(x => x.AccountId)
                .Select(g => new { AccountId = g.Key,
                    Net = g.Sum(x => x.DebitAmount - x.CreditAmount) }).ToListAsync();
            Assert.Equal(2, net.Count);
            Assert.All(net, x => Assert.Equal(0m, x.Net));
        }
        await using (var db = Db(options, tenantId, makerId, "Accountant"))
        {
            var reversal = new FacilityVendorBillReversalRequestDto {
                ClientRequestId = Guid.NewGuid(), RowVersion = bill.RowVersion,
                ReversalDate = invoiceDate, AccountingPeriodId = periodId,
                Reason = "Supplier confirmed the invoice must be fully cancelled."
            };
            var requested = await Service(db, tenantId, makerId, "Accountant")
                .RequestReversalAsync(bill.Reference, reversal);
            Assert.True(requested.Success, requested.Message);
            bill = requested.Data!;
            Assert.Equal("Submitted", bill.ReversalState);
            Assert.True((await Service(db, tenantId, makerId, "Accountant")
                .RequestReversalAsync(bill.Reference, reversal)).Success);
        }
        await using (var db = Db(options, tenantId, checkerId, "Principal"))
        {
            var service = Service(db, tenantId, checkerId, "Principal");
            var review = new FacilityVendorBillReversalReviewDto {
                ClientRequestId = Guid.NewGuid(), RowVersion = bill.ReversalRowVersion!,
                Approve = true, Reason = "Independently reviewed and approved the full contra journal."
            };
            var finalized = await Service(db, tenantId, checkerId, "Principal")
                .ReviewReversalAsync(bill.Reference, review);
            Assert.True(finalized.Success, finalized.Message);
            bill = finalized.Data!;
            Assert.Equal("Reversed", bill.StateCode);
            Assert.Equal("Posted", bill.ReversalState);
            Assert.True((await Service(db, tenantId, checkerId, "Principal")
                .ReviewReversalAsync(bill.Reference, review)).Success);
            var journals = await db.Journals.AsNoTracking().Where(x => x.TenantId == tenantId &&
                (x.SourceType == "FacilityVendorServiceBill" ||
                 x.SourceType == "FacilityVendorServiceBillReversal") &&
                x.State == JournalState.Posted).OrderBy(x => x.Id).ToListAsync();
            Assert.Equal(2, journals.Count);
            Assert.Equal(journals[0].Id, journals[1].ReversalOfJournalId);
            var lines = await db.JournalLines.AsNoTracking().Where(x =>
                x.TenantId == tenantId && (x.JournalId == journals[0].Id ||
                 x.JournalId == journals[1].Id)).ToListAsync();
            Assert.Equal(0m, lines.Sum(x => x.DebitAmount - x.CreditAmount));
            Assert.Equal(2400m, lines.Sum(x => x.DebitAmount));
            Assert.Equal(2400m, lines.Sum(x => x.CreditAmount));
            Assert.Equal(0, await db.InventoryMovements.CountAsync(x =>
                x.TenantId == tenantId && x.SourceType == "FacilityVendorServiceBill"));
            // Historical/corrupt reference probe: a same-tenant posted GL
            // journal is not a valid substitute for the invoice's own source.
            // Roll back the corruption, retaining immutable posted evidence.
            await using (var tamper = await db.Database.BeginTransactionAsync())
            {
                var foreignSourceId = await db.Journals.AsNoTracking()
                    .Where(x => x.TenantId == tenantId &&
                        x.SourceType == "FacilityVendorServicePayment" &&
                        x.State == JournalState.Posted).Select(x => x.Id).SingleAsync();
                await db.FacilityVendorServiceBills.Where(x => x.TenantId == tenantId &&
                    x.PublicId == bill.Reference).ExecuteUpdateAsync(x => x
                    .SetProperty(v => v.JournalId, (long?)foreignSourceId));
                var corrupted = await service.SupplierAgingAsync(invoiceDate, campusId, 1, 25);
                Assert.Equal(409, corrupted.StatusCode);
                await tamper.RollbackAsync();
            }
            var restored = await service.SupplierAgingAsync(invoiceDate, campusId, 1, 25);
            Assert.True(restored.Success, restored.Message);
            Assert.Equal(0m, restored.Data!.Page.Items.Single(x =>
                x.InvoiceSource == "Service" && x.InvoiceReference == bill.Reference).OpenBalance);
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
    private static FacilityVendorServiceBillService Service(EduOSDbContext db,
        long tenantId, long actorId, string role) =>
        new(db, new Principal(tenantId, actorId, role), TimeProvider.System,
            NullLogger<FacilityVendorServiceBillService>.Instance);
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
