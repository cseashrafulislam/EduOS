using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Finance;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.App;

// Uses the existing disposable SQL Server fixture: real rowversions, filtered unique
// indexes, tenant query filters, FKs and independent DbContexts are essential here.
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    [Fact]
    public async Task Approved_auto_extension_invoice_posts_only_with_successful_operating_change()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var assignment = Request(fixture, 0, fixture.Start, fixture.Start.AddDays(12));
        assignment.MonthlyFare = 2500m;
        var booked = await AssignAsync(fixture, assignment);
        Assert.True(booked.Success, booked.Message);
        var source = booked.Data!;
        var proposedEnd = source.EndDate!.Value.AddDays(10);
        var checker = await CreateFinanceCheckerAsync(fixture);
        long periodId, headId;
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var suffix = Guid.NewGuid().ToString("N")[..10];
            var receivable = new Account { TenantId = fixture.TenantId, Code = "AR-" + suffix,
                Name = "Reservation student receivable", AccountType = AccountType.Asset,
                IsActive = true, AllowsPosting = true };
            var income = new Account { TenantId = fixture.TenantId, Code = "INC-" + suffix,
                Name = "Transport reservation income", AccountType = AccountType.Income,
                IsActive = true, AllowsPosting = true };
            db.Accounts.AddRange(receivable, income);
            await db.SaveChangesAsync();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var period = new AccountingPeriod { TenantId = fixture.TenantId,
                Name = "Transport posting period " + suffix,
                StartDate = today.AddDays(-2), EndDate = today.AddDays(30) };
            var head = new FeeHead { TenantId = fixture.TenantId, Name = "Transport extension " + suffix,
                Code = "TR-" + suffix, IncomeAccountId = income.Id,
                ReceivableAccountId = receivable.Id, IsActive = true };
            db.AddRange(period, head);
            await db.SaveChangesAsync();
            periodId = period.Id; headId = head.Id;
        }
        long id;
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var requested = await Finance(db, fixture.TenantId, fixture.UserId)
                .RequestTransportClearanceAsync(new RequestTransportClearanceDto
                {
                    ClientRequestId = Guid.NewGuid(), SourceAssignmentReference = source.Reference,
                    SourceRowVersion = source.RowVersion,
                    Operation = TransportClearanceOperation.UpcomingRiderExtension,
                    EffectiveDate = proposedEnd, Reason = "Additional future bus service charge"
                });
            Assert.True(requested.Success, requested.Message);
            Assert.True(requested.Data!.ApprovedNetAdjustment > 0m);
            id = requested.Data.Id;
        }
        long invoiceId, journalId;
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, checker))
        {
            var row = await db.TransportFinanceClearances.AsNoTracking().SingleAsync(x => x.Id == id);
            var approved = await Finance(db, fixture.TenantId, checker)
                .ReviewTransportClearanceAsync(id, new ReviewTransportClearanceDto
                {
                    RowVersion = Convert.ToBase64String(row.RowVersion), Approve = true,
                    AutoPostInvoice = true, AccountingPeriodId = periodId, AutoFeeHeadId = headId,
                    ReconciliationNote = "Independent Finance reviewer approved the exact additional charge."
                });
            Assert.True(approved.Success, approved.Message);
            journalId = approved.Data!.SettlementJournalId!.Value;
            var invoice = await db.StudentInvoices.SingleAsync(x => x.JournalId == journalId);
            invoiceId = invoice.Id;
            Assert.Equal(InvoiceState.Draft, invoice.State);
            Assert.Equal(JournalState.Approved, (await db.Journals.SingleAsync(x => x.Id == journalId)).State);
        }
        var mutation = new ExtendUpcomingTransportAssignmentRequestDto
        {
            ClientRequestId = Guid.NewGuid(), EndDate = proposedEnd,
            RowVersion = source.RowVersion, FinanceClearanceId = id
        };
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var rejected = await Service(db, fixture.TenantId, fixture.UserId)
                .ExtendUpcomingRiderAsync(source.Reference, new ExtendUpcomingTransportAssignmentRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), EndDate = proposedEnd,
                    RowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }),
                    FinanceClearanceId = id
                });
            Assert.Equal(409, rejected.StatusCode);
            Assert.Equal(InvoiceState.Draft, (await db.StudentInvoices.AsNoTracking()
                .SingleAsync(x => x.Id == invoiceId)).State);
            Assert.Equal(JournalState.Approved, (await db.Journals.AsNoTracking()
                .SingleAsync(x => x.Id == journalId)).State);
        }
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var period = await db.AccountingPeriods.SingleAsync(x => x.Id == periodId);
            period.IsClosed = true;
            await db.SaveChangesAsync();
        }
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var blocked = await Service(db, fixture.TenantId, fixture.UserId)
                .ExtendUpcomingRiderAsync(source.Reference, mutation);
            Assert.Equal(409, blocked.StatusCode);
            Assert.Equal(InvoiceState.Draft, (await db.StudentInvoices.AsNoTracking()
                .SingleAsync(x => x.Id == invoiceId)).State);
            Assert.Equal(JournalState.Approved, (await db.Journals.AsNoTracking()
                .SingleAsync(x => x.Id == journalId)).State);
        }
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var period = await db.AccountingPeriods.SingleAsync(x => x.Id == periodId);
            period.IsClosed = false;
            await db.SaveChangesAsync();
        }
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var extended = await Service(db, fixture.TenantId, fixture.UserId)
                .ExtendUpcomingRiderAsync(source.Reference, mutation);
            Assert.True(extended.Success, extended.Message);
            var replay = await Service(db, fixture.TenantId, fixture.UserId)
                .ExtendUpcomingRiderAsync(source.Reference, mutation);
            Assert.True(replay.Success, replay.Message);
        }
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var invoice = await db.StudentInvoices.SingleAsync(x => x.Id == invoiceId);
            var journal = await db.Journals.SingleAsync(x => x.Id == journalId);
            var lines = await db.JournalLines.Where(x => x.JournalId == journalId).ToArrayAsync();
            Assert.Equal(InvoiceState.Issued, invoice.State);
            Assert.Equal(JournalState.Posted, journal.State);
            Assert.Equal("StudentInvoice", journal.SourceType);
            Assert.Equal(invoice.Id, journal.SourceId);
            Assert.Equal(fixture.UserId, journal.CreatedBy);
            Assert.Equal(checker, journal.ApprovedByUserId);
            Assert.NotEqual(journal.CreatedBy, journal.ApprovedByUserId);
            Assert.Equal(fixture.UserId, journal.PostedByUserId);
            Assert.Equal(2, lines.Length);
            Assert.Equal(invoice.TotalAmount, lines.Sum(x => x.DebitAmount));
            Assert.Equal(invoice.TotalAmount, lines.Sum(x => x.CreditAmount));
            Assert.Equal(1, await db.StudentInvoices.CountAsync(x => x.ClientRequestId ==
                ReservationAutomaticInvoicePosting.InvoiceKey(fixture.TenantId, "TRANSPORT", id)));
        }
    }

    [Fact]
    public async Task Upcoming_paid_rider_extension_consumes_only_approved_single_use_finance_clearance()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var assignment = Request(fixture, 0, fixture.Start, fixture.Start.AddDays(12));
        assignment.MonthlyFare = 2500m;
        var booked = await AssignAsync(fixture, assignment);
        Assert.True(booked.Success, booked.Message);
        var source = booked.Data!;
        var proposedEnd = source.EndDate!.Value.AddDays(10);
        var checker = await CreateFinanceCheckerAsync(fixture);
        var mutation = new ExtendUpcomingTransportAssignmentRequestDto
        {
            ClientRequestId = Guid.NewGuid(), EndDate = proposedEnd,
            RowVersion = source.RowVersion
        };
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var blocked = await Service(db, fixture.TenantId, fixture.UserId)
                .ExtendUpcomingRiderAsync(source.Reference, mutation);
            Assert.Equal(409, blocked.StatusCode);
        }
        long id;
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var quote = await Finance(db, fixture.TenantId, fixture.UserId)
                .RequestTransportClearanceAsync(new RequestTransportClearanceDto
                {
                    ClientRequestId = Guid.NewGuid(), SourceAssignmentReference = source.Reference,
                    SourceRowVersion = source.RowVersion,
                    Operation = TransportClearanceOperation.UpcomingRiderExtension,
                    EffectiveDate = proposedEnd, ManualNetAdjustment = 0m,
                    OverrideReason = "An authorized institution fare waiver for this extension",
                    Reason = "Finance-reviewed upcoming charged rider extension"
                });
            Assert.True(quote.Success, quote.Message);
            id = quote.Data!.Id;
            Assert.Equal(TransportClearanceState.Pending, quote.Data.State);
            Assert.Equal(0m, quote.Data.ApprovedNetAdjustment);
            mutation.FinanceClearanceId = id;
            Assert.Equal(409, (await Service(db, fixture.TenantId, fixture.UserId)
                .ExtendUpcomingRiderAsync(source.Reference, mutation)).StatusCode);
            Assert.Equal(403, (await Finance(db, fixture.TenantId, fixture.UserId)
                .ReviewTransportClearanceAsync(id, new ReviewTransportClearanceDto
                {
                    RowVersion = quote.Data.RowVersion, Approve = true,
                    NoAdjustmentRequired = true,
                    ReconciliationNote = "Self-approval must not bypass independent Finance review."
                })).StatusCode);
        }
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, checker))
        {
            var row = await db.TransportFinanceClearances.AsNoTracking().SingleAsync(x => x.Id == id);
            var approved = await Finance(db, fixture.TenantId, checker)
                .ReviewTransportClearanceAsync(id, new ReviewTransportClearanceDto
                {
                    RowVersion = Convert.ToBase64String(row.RowVersion), Approve = true,
                    NoAdjustmentRequired = true,
                    ReconciliationNote = "Independent waiver approved, no accounting adjustment required."
                });
            Assert.True(approved.Success, approved.Message);
        }
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var service = Service(db, fixture.TenantId, fixture.UserId);
            var extended = await service.ExtendUpcomingRiderAsync(source.Reference, mutation);
            Assert.True(extended.Success, extended.Message);
            Assert.Equal(proposedEnd, extended.Data!.EndDate);
            var replay = await service.ExtendUpcomingRiderAsync(source.Reference, mutation);
            Assert.True(replay.Success, replay.Message);
            Assert.Equal(proposedEnd, replay.Data!.EndDate);
        }
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var state = await db.TransportFinanceClearances.AsNoTracking().SingleAsync(x => x.Id == id);
            Assert.Equal(TransportClearanceState.Consumed, state.State);
            mutation.ClientRequestId = Guid.NewGuid();
            var same = await Service(db, fixture.TenantId, fixture.UserId)
                .ExtendUpcomingRiderAsync(source.Reference, mutation);
            Assert.True(same.Success, same.Message);
            Assert.Equal(proposedEnd, same.Data!.EndDate);
            Assert.Single(await db.StudentTransports.AsNoTracking().Where(x =>
                x.TenantId == fixture.TenantId && x.PublicId == source.Reference).ToListAsync());
        }
    }

    [Fact]
    public async Task Finance_approved_transport_transfer_is_single_use_tenant_scoped_and_preserves_lineage()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var source = await PrepareOperatingFinanceRiderAsync(fixture);
        var checkerId = await CreateFinanceCheckerAsync(fixture);
        var request = new RequestTransportClearanceDto
        {
            ClientRequestId = Guid.NewGuid(), SourceAssignmentReference = source.Reference,
            SourceRowVersion = source.Version, Operation = TransportClearanceOperation.VehicleTransfer,
            DestinationVehicleReference = source.Destination, EffectiveDate = source.Today,
            Reason = "Documented temporary vehicle replacement for the assigned route"
        };
        long clearanceId;
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var finance = Finance(db, fixture.TenantId, fixture.UserId);
            var requested = await finance.RequestTransportClearanceAsync(request);
            Assert.True(requested.Success, requested.Message);
            clearanceId = requested.Data!.Id;
            Assert.Equal(TransportClearanceState.Pending, requested.Data.State);
            Assert.Equal(0m, requested.Data.ApprovedFareCredit);
            var replay = await finance.RequestTransportClearanceAsync(request);
            Assert.True(replay.Success, replay.Message);
            Assert.Equal(clearanceId, replay.Data!.Id);
            request.Reason = "Changed purpose for the same request key is rejected";
            Assert.Equal(409, (await finance.RequestTransportClearanceAsync(request)).StatusCode);
            request.Reason = "Documented temporary vehicle replacement for the assigned route";
            var selfReview = await finance.ReviewTransportClearanceAsync(clearanceId,
                new ReviewTransportClearanceDto
                {
                    RowVersion = requested.Data.RowVersion, Approve = true,
                    NoAdjustmentRequired = true,
                    ReconciliationNote = "Independent settlement verification required before execution."
                });
            Assert.Equal(403, selfReview.StatusCode);
            Assert.Equal(0, await db.Journals.CountAsync(x => x.TenantId == fixture.TenantId));
        }
        await using (var foreignDb = ScopedContext(fixture.Options, fixture.TenantId + 10_000, fixture.UserId))
        {
            var foreign = Finance(foreignDb, fixture.TenantId + 10_000, fixture.UserId);
            Assert.Equal(404, (await foreign.GetTransportBillingPolicyAsync(source.CampusId)).StatusCode);
            Assert.Equal(404, (await foreign.ReviewTransportClearanceAsync(clearanceId,
                new ReviewTransportClearanceDto
                {
                    RowVersion = source.Version, Approve = false,
                    ReconciliationNote = "This institution does not own the selected clearance."
                })).StatusCode);
            Assert.Empty((await foreign.GetTransportClearancesAsync(null, 1, 25)).Data!.Items);
        }
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, checkerId))
        {
            var finance = Finance(db, fixture.TenantId, checkerId);
            var list = await finance.GetTransportClearancesAsync(source.CampusId, 1, 25);
            Assert.True(list.Success, list.Message);
            var pending = Assert.Single(list.Data!.Items, x => x.Id == clearanceId);
            var approved = await finance.ReviewTransportClearanceAsync(clearanceId,
                new ReviewTransportClearanceDto
                {
                    RowVersion = pending.RowVersion, Approve = true,
                    NoAdjustmentRequired = true,
                    ReconciliationNote = "Fare unchanged, settlement journal not needed; independently verified."
                });
            Assert.True(approved.Success, approved.Message);
            Assert.Equal(TransportClearanceState.Approved, approved.Data!.State);
            Assert.Equal(409, (await finance.ReviewTransportClearanceAsync(clearanceId,
                new ReviewTransportClearanceDto
                {
                    RowVersion = pending.RowVersion, Approve = true,
                    NoAdjustmentRequired = true,
                    ReconciliationNote = "Duplicate independent review must be rejected with conflict."
                })).StatusCode);
        }
        var transfer = new TransferOperatingTransportVehicleRequestDto
        {
            ClientRequestId = Guid.NewGuid(), VehicleReference = source.Destination,
            EffectiveDate = source.Today, RowVersion = source.Version, FinanceClearanceId = clearanceId
        };
        Guid successorReference;
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var transport = Service(db, fixture.TenantId, fixture.UserId);
            var moved = await transport.TransferOperatingRiderAsync(source.Reference, transfer);
            Assert.True(moved.Success, moved.Message);
            successorReference = moved.Data!.Reference;
        }
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var transport = Service(db, fixture.TenantId, fixture.UserId);
            var replay = await transport.TransferOperatingRiderAsync(source.Reference, transfer);
            Assert.True(replay.Success, replay.Message);
            Assert.Equal(successorReference, replay.Data!.Reference);
            transfer.ClientRequestId = Guid.NewGuid();
            Assert.Equal(409, (await transport.TransferOperatingRiderAsync(source.Reference, transfer)).StatusCode);
            var original = await db.StudentTransports.AsNoTracking().SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.Id == source.Id);
            var successor = await db.StudentTransports.AsNoTracking().SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.PreviousAssignmentId == source.Id);
            var clearance = await db.TransportFinanceClearances.AsNoTracking().SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.Id == clearanceId);
            Assert.Equal(source.Today.AddDays(-1), original.EndDate);
            Assert.Equal(source.Today, successor.StartDate);
            Assert.Equal(original.MonthlyFare, successor.MonthlyFare);
            Assert.Equal(successorReference, successor.PublicId);
            Assert.Equal(TransportClearanceState.Consumed, clearance.State);
            Assert.Equal(successor.Id, clearance.SuccessorAssignmentId);
            Assert.Equal(checkerId, clearance.ReviewedByUserId);
            Assert.Null(clearance.SettlementJournalId);
            Assert.Equal(2, await db.StudentTransports.CountAsync(x =>
                x.TenantId == fixture.TenantId && x.StudentId == original.StudentId));
        }
    }

    [Fact]
    public async Task Concurrent_transport_clearance_consumption_creates_exactly_one_successor()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var source = await PrepareOperatingFinanceRiderAsync(fixture);
        var checkerId = await CreateFinanceCheckerAsync(fixture);
        long clearanceId;
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var requested = await Finance(db, fixture.TenantId, fixture.UserId).RequestTransportClearanceAsync(
                new RequestTransportClearanceDto
                {
                    ClientRequestId = Guid.NewGuid(), SourceAssignmentReference = source.Reference,
                    SourceRowVersion = source.Version, Operation = TransportClearanceOperation.VehicleTransfer,
                    DestinationVehicleReference = source.Destination, EffectiveDate = source.Today,
                    Reason = "Independent review of concurrent bus transfer requests"
                });
            Assert.True(requested.Success, requested.Message);
            clearanceId = requested.Data!.Id;
        }
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, checkerId))
        {
            var approved = await Finance(db, fixture.TenantId, checkerId).ReviewTransportClearanceAsync(clearanceId,
                new ReviewTransportClearanceDto
                {
                    RowVersion = Convert.ToBase64String((await db.TransportFinanceClearances.AsNoTracking()
                        .SingleAsync(x => x.Id == clearanceId)).RowVersion),
                    Approve = true, NoAdjustmentRequired = true,
                    ReconciliationNote = "Two independent submissions must consume one approval exactly once."
                });
            Assert.True(approved.Success, approved.Message);
        }
        var request = new TransferOperatingTransportVehicleRequestDto
        {
            ClientRequestId = Guid.NewGuid(), VehicleReference = source.Destination,
            EffectiveDate = source.Today, RowVersion = source.Version,
            FinanceClearanceId = clearanceId
        };
        async Task<ApiResponse<StudentTransportDto>> RaceAsync()
        {
            await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
            return await Service(db, fixture.TenantId, fixture.UserId)
                .TransferOperatingRiderAsync(source.Reference, request);
        }
        var responses = await Task.WhenAll(RaceAsync(), RaceAsync());
        Assert.All(responses, x => Assert.True(x.Success || x.StatusCode == 409,
            $"Expected success or retryable conflict, got {x.StatusCode}: {x.Message}"));
        Assert.Contains(responses, x => x.Success);
        await using var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        var successor = await verify.StudentTransports.AsNoTracking().SingleAsync(x =>
            x.TenantId == fixture.TenantId && x.PreviousAssignmentId == source.Id);
        var clearance = await verify.TransportFinanceClearances.AsNoTracking().SingleAsync(x =>
            x.TenantId == fixture.TenantId && x.Id == clearanceId);
        Assert.Equal(TransportClearanceState.Consumed, clearance.State);
        Assert.Equal(successor.Id, clearance.SuccessorAssignmentId);
        Assert.Equal(2, await verify.StudentTransports.CountAsync(x =>
            x.TenantId == fixture.TenantId && x.StudentId == successor.StudentId));
    }

    [Fact]
    public async Task Charged_transport_closure_rejects_unposted_settlement_and_requires_independent_zero_attestation()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var source = await PrepareOperatingFinanceRiderAsync(fixture);
        var checkerId = await CreateFinanceCheckerAsync(fixture);
        long clearanceId;
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var request = await Finance(db, fixture.TenantId, fixture.UserId).RequestTransportClearanceAsync(
                new RequestTransportClearanceDto
                {
                    ClientRequestId = Guid.NewGuid(), SourceAssignmentReference = source.Reference,
                    SourceRowVersion = source.Version, Operation = TransportClearanceOperation.Closure,
                    EffectiveDate = source.Today, ManualFareCredit = 0m,
                    OverrideReason = "Approved institutional waiver for this documented closure",
                    Reason = "Guardian-requested service stop with independently attested zero credit"
                });
            Assert.True(request.Success, request.Message);
            clearanceId = request.Data!.Id;
            var before = await Service(db, fixture.TenantId, fixture.UserId).CloseAsync(
                source.Reference, new CloseStudentTransportRequestDto
                { EndDate = source.Today, RowVersion = source.Version, FinanceClearanceId = clearanceId });
            Assert.Equal(409, before.StatusCode);
        }
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, checkerId))
        {
            var finance = Finance(db, fixture.TenantId, checkerId);
            var current = await db.TransportFinanceClearances.AsNoTracking().SingleAsync(x => x.Id == clearanceId);
            var invalidJournal = await finance.ReviewTransportClearanceAsync(clearanceId,
                new ReviewTransportClearanceDto
                {
                    RowVersion = Convert.ToBase64String(current.RowVersion), Approve = true,
                    NoAdjustmentRequired = false, SettlementJournalId = 987654321,
                    ManualFareCredit = 100m,
                    OverrideReason = "Finance reviewer adjustment to verify missing journal handling",
                    ReconciliationNote = "Unposted or unowned journals must not pass independent finance review."
                });
            Assert.Equal(409, invalidJournal.StatusCode);
            var approved = await finance.ReviewTransportClearanceAsync(clearanceId,
                new ReviewTransportClearanceDto
                {
                    RowVersion = Convert.ToBase64String(current.RowVersion), Approve = true,
                    NoAdjustmentRequired = true,
                    ReconciliationNote = "Zero approved credit manually checked against the account evidence."
                });
            Assert.True(approved.Success, approved.Message);
        }
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var closed = await Service(db, fixture.TenantId, fixture.UserId).CloseAsync(source.Reference,
                new CloseStudentTransportRequestDto
                { EndDate = source.Today, RowVersion = source.Version, FinanceClearanceId = clearanceId });
            Assert.True(closed.Success, closed.Message);
        }
        await using var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        var rider = await verify.StudentTransports.AsNoTracking().SingleAsync(x => x.Id == source.Id);
        var clearance = await verify.TransportFinanceClearances.AsNoTracking().SingleAsync(x => x.Id == clearanceId);
        Assert.Equal(TransportAssignmentState.Closed, rider.State);
        Assert.Equal(source.Today, rider.EndDate);
        Assert.Equal(TransportClearanceState.Consumed, clearance.State);
        Assert.True(clearance.NoAdjustmentRequired);
        Assert.Null(clearance.SettlementJournalId);
        Assert.Null(clearance.SuccessorAssignmentId);
    }

    private static FinanceAdjustmentService Finance(EduOSDbContext db, long tenant, long user) =>
        new(db, new TenantAdmin(tenant, user), TimeProvider.System);

    private static async Task<long> CreateFinanceCheckerAsync(Fixture fixture)
    {
        await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var checker = new ApplicationUser
        {
            UserName = "fin-check-" + suffix, NormalizedUserName = ("fin-check-" + suffix).ToUpperInvariant(),
            Email = "fin-check-" + suffix + "@example.test",
            NormalizedEmail = ("fin-check-" + suffix + "@example.test").ToUpperInvariant(),
            FullName = "Independent Transport Finance reviewer", EmailConfirmed = true, IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(checker);
        await db.SaveChangesAsync();
        return checker.Id;
    }

    private static async Task<(Guid Reference, Guid Destination, string Version, DateOnly Today, long Id, long CampusId)>
        PrepareOperatingFinanceRiderAsync(Fixture fixture)
    {
        var booking = await AssignAsync(fixture, Request(fixture, 0, fixture.Start, fixture.Start.AddDays(16)));
        Assert.True(booking.Success, booking.Message);
        await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        var source = await db.StudentTransports.SingleAsync(x =>
            x.TenantId == fixture.TenantId && x.PublicId == booking.Data!.Reference);
        var campusId = await db.StudentEnrollments.AsNoTracking().Where(x =>
            x.TenantId == fixture.TenantId && x.Id == source.StudentEnrollmentId)
            .Select(x => x.CampusId).SingleAsync();
        var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        source.StartDate = today.AddDays(-12);
        source.EndDate = today.AddDays(16);
        source.MonthlyFare = 2500m;
        var destination = new Vehicle
        {
            TenantId = fixture.TenantId, PublicId = Guid.NewGuid(),
            VehicleNumber = "CI-FIN-" + Guid.NewGuid().ToString("N")[..12],
            VehicleType = "Bus", Capacity = 2, IsActive = true, CampusId = campusId
        };
        db.Vehicles.Add(destination);
        await db.SaveChangesAsync();
        db.RouteVehicleAssignments.Add(new RouteVehicleAssignment
        {
            TenantId = fixture.TenantId, RouteId = source.RouteId, VehicleId = destination.Id,
            EffectiveFrom = today.AddDays(-20), IsCurrent = true
        });
        await db.SaveChangesAsync();
        var version = await db.StudentTransports.AsNoTracking().Where(x => x.Id == source.Id)
            .Select(x => x.RowVersion).SingleAsync();
        return (source.PublicId, destination.PublicId, Convert.ToBase64String(version),
            today, source.Id, campusId);
    }
}
