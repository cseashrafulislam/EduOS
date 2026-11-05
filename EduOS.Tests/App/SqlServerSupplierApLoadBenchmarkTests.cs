using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Security.Claims;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Facilities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace EduOS.Tests.App;

/// <summary>
/// Opt-in, disposable SQL Server AP read-path load rehearsal. Inserts synthetic
/// posting lineages directly for speed; this does NOT replace end-to-end invoice
/// creation, maker-checker, payment or reversal acceptance tests.
/// </summary>
[Collection("SqlServerIntegration")]
public sealed class SqlServerSupplierApLoadBenchmarkTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(5001)]
    [InlineData(10001)]
    [InlineData(50001)]
    public async Task Opt_in_synthetic_posted_supplier_invoices_reconcile_at_scale(int invoiceCount)
    {
        if (Environment.GetEnvironmentVariable("EDUOS_SQL_AP_BENCHMARK") != "1") return;
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(connection), "Disposable SQL connection is required.");
        var target = new SqlConnectionStringBuilder(connection);
        Assert.True(target.DataSource.StartsWith("127.0.0.1,14333", StringComparison.Ordinal) &&
            target.InitialCatalog == "EduOS_MigrationSmoke",
            "AP load rehearsal must never run on a production or shared database.");

        const decimal invoiceAmount = 100m;
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        long tenantId, otherTenantId, campusId, otherCampusId, supplierId, poId, periodId;
        long inventoryId, payableId, adminId, otherAdminId;
        await using (var seed = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "APB-" + suffix, Name = "Disposable AP benchmark",
                Email = "apb-" + suffix + "@example.test", State = TenantState.Active };
            var other = new Tenant { Code = "APO-" + suffix, Name = "AP benchmark other tenant",
                Email = "apo-" + suffix + "@example.test", State = TenantState.Active };
            var actor = User("apb-" + suffix + "@example.test");
            var outsider = User("apo-" + suffix + "@example.test");
            seed.AddRange(tenant, other, actor, outsider);
            await seed.SaveChangesAsync();
            tenantId = tenant.Id; otherTenantId = other.Id;
            adminId = actor.Id; otherAdminId = outsider.Id;
            seed.ChangeTracker.Clear();
            using (seed.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Code = "APB-" + suffix,
                    Name = "AP load campus" };
                seed.Campuses.Add(campus);
                await seed.SaveChangesAsync();
                campusId = campus.Id;
                var supplier = new ProcurementSupplier { TenantId = tenantId, CampusId = campusId,
                    ClientRequestId = Guid.NewGuid(), SupplierCode = "APB-" + suffix,
                    Name = "Synthetic supplier", StateCode = "Approved" };
                var req = new ProcurementRequisition { TenantId = tenantId, CampusId = campusId,
                    ClientRequestId = Guid.NewGuid(), RequestNumber = "APB-R-" + suffix,
                    Purpose = "Disposable read benchmark", StateCode = "Approved" };
                var period = new AccountingPeriod { TenantId = tenantId, Name = "AP load " + suffix,
                    StartDate = asOf.AddDays(-7), EndDate = asOf.AddDays(7) };
                var inventory = new Account { TenantId = tenantId, Code = "APB-I-" + suffix,
                    Name = "Synthetic inventory value", AccountType = AccountType.Asset,
                    AllowsPosting = true, IsActive = true, CurrencyCode = "BDT" };
                var payable = new Account { TenantId = tenantId, Code = "APB-L-" + suffix,
                    Name = "Synthetic trade payable", AccountType = AccountType.Liability,
                    AllowsPosting = true, IsActive = true, CurrencyCode = "BDT" };
                var role = new ApplicationRole { TenantId = tenantId,
                    Name = "TenantAdmin", NormalizedName = "TENANTADMIN", IsActive = true };
                seed.AddRange(supplier, req, period, inventory, payable, role);
                seed.TenantMemberships.Add(new TenantMembership { TenantId = tenantId,
                    UserId = adminId, Status = MembershipStatus.Active });
                seed.UserCampusAccesses.Add(new UserCampusAccess { TenantId = tenantId,
                    UserId = adminId, CampusId = campusId, IsActive = true });
                await seed.SaveChangesAsync();
                supplierId = supplier.Id; periodId = period.Id;
                inventoryId = inventory.Id; payableId = payable.Id;
                seed.UserRoles.Add(new IdentityUserRole<long> { UserId = adminId, RoleId = role.Id });
                var po = new ProcurementPurchaseOrder { TenantId = tenantId,
                    CampusId = campusId, SupplierId = supplier.Id, RequisitionId = req.Id,
                    ClientRequestId = Guid.NewGuid(), OrderNumber = "APB-P-" + suffix,
                    StateCode = "Approved", CurrencyCode = "BDT" };
                seed.ProcurementPurchaseOrders.Add(po);
                await seed.SaveChangesAsync();
                poId = po.Id;
            }
            seed.ChangeTracker.Clear();
            using (seed.BeginSystemTenantScope(otherTenantId))
            {
                var campus = new Campus { TenantId = otherTenantId, Code = "APO-" + suffix,
                    Name = "Other tenant campus" };
                seed.Campuses.Add(campus);
                await seed.SaveChangesAsync();
                otherCampusId = campus.Id;
                var role = new ApplicationRole { TenantId = otherTenantId,
                    Name = "TenantAdmin", NormalizedName = "TENANTADMIN", IsActive = true };
                seed.Roles.Add(role);
                seed.TenantMemberships.Add(new TenantMembership { TenantId = otherTenantId,
                    UserId = otherAdminId, Status = MembershipStatus.Active });
                seed.UserCampusAccesses.Add(new UserCampusAccess { TenantId = otherTenantId,
                    UserId = otherAdminId, CampusId = otherCampusId, IsActive = true });
                await seed.SaveChangesAsync();
                seed.UserRoles.Add(new IdentityUserRole<long> { UserId = otherAdminId, RoleId = role.Id });
                await seed.SaveChangesAsync();
            }
        }

        var fixture = Stopwatch.StartNew();
        await using (var seed = new EduOSDbContext(options))
        {
            seed.Database.SetCommandTimeout(TimeSpan.FromSeconds(90));
            // The benchmark must not spend five minutes building one giant
            // fixture transaction. Commit bounded, fully linked synthetic
            // batches; audit timing starts only after all data is present.
            const int fixtureBatchSize = 2500;
            for (var offset = 0; offset < invoiceCount; offset += fixtureBatchSize)
            {
                await using var tx = await seed.Database.BeginTransactionAsync();
                await InsertSyntheticBatchAsync(seed, Math.Min(fixtureBatchSize, invoiceCount - offset),
                    "APB-" + suffix + "-" + offset + "-", asOf, tenantId, poId,
                    supplierId, campusId, periodId, inventoryId, payableId, invoiceAmount);
                await tx.CommitAsync();
            }
            using (seed.BeginSystemTenantScope(tenantId))
            {
                Assert.Equal(invoiceCount, await seed.ProcurementSupplierInvoices
                    .CountAsync(x => x.TenantId == tenantId && x.PurchaseOrderId == poId && x.JournalId != null));
                Assert.Equal(invoiceCount * 2, await seed.JournalLines.CountAsync(x =>
                    x.TenantId == tenantId && (x.AccountId == inventoryId || x.AccountId == payableId)));
            }
        }
        fixture.Stop();

        var sql = new SqlLatencyProbe();
        var measuredOptions = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer(connection).AddInterceptors(sql).Options;
        var audit = Stopwatch.StartNew();
        long concurrentWriteWaitMs = 0;
        await using (var db = Db(measuredOptions, tenantId, adminId))
        {
            var service = Service(db, tenantId, adminId);
            var auditTask = service.SupplierApInstitutionControlAsync(asOf, campusId);
            Task? competingPost = null;
            var writeWait = new Stopwatch();
            if (invoiceCount == 10001)
            {
                // Wait until the read's actual SQL population count has run.
                // Its serializable range locks must then exclude a same-tenant
                // posted invoice until the entire Finance GL check commits.
                await sql.PopulationCountObserved.WaitAsync(TimeSpan.FromSeconds(30));
                var writerStarted = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                writeWait.Start();
                competingPost = Task.Run(async () =>
                {
                    await using var writer = new EduOSDbContext(options);
                    await using var writeTx = await writer.Database.BeginTransactionAsync();
                    writerStarted.TrySetResult(true);
                    await InsertSyntheticBatchAsync(writer, 1, "APB-W-" + suffix + "-",
                        asOf, tenantId, poId, supplierId, campusId, periodId,
                        inventoryId, payableId, invoiceAmount);
                    await writeTx.CommitAsync();
                });
                await writerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await Task.Delay(250);
                Assert.False(competingPost.IsCompleted,
                    "Same-tenant invoice posting must not overtake the serializable AP control.");
            }
            var full = await auditTask;
            audit.Stop();
            Assert.True(full.Success, full.Message);
            Assert.NotNull(full.Data);
            Assert.Equal(invoiceCount, full.Data!.InvoiceCount);
            Assert.Equal(0, full.Data.InvalidSourceCount);
            Assert.Equal(invoiceCount * invoiceAmount, full.Data.SubledgerOutstanding);
            Assert.Equal(full.Data.SubledgerOutstanding, full.Data.LinkedPostedGlBalance);
            Assert.Equal(full.Data.SubledgerOutstanding, full.Data.ControlAccountGlBalance);
            Assert.Equal(0m, full.Data.SubledgerVsControlDifference);
            Assert.Equal(0m, full.Data.UnattributedGlBalance);
            Assert.True(full.Data.Reconciled);
            Assert.Single(full.Data.Accounts);
            if (competingPost != null)
            {
                await competingPost.WaitAsync(TimeSpan.FromSeconds(30));
                writeWait.Stop();
                concurrentWriteWaitMs = writeWait.ElapsedMilliseconds;
                await using var postCommitDb = Db(options, tenantId, adminId);
                var after = await Service(postCommitDb, tenantId, adminId)
                    .SupplierApInstitutionControlAsync(asOf, campusId);
                Assert.True(after.Success, after.Message);
                Assert.Equal(invoiceCount + 1, after.Data!.InvoiceCount);
                Assert.Equal((invoiceCount + 1) * invoiceAmount, after.Data.SubledgerOutstanding);
                Assert.Equal(after.Data.SubledgerOutstanding, after.Data.ControlAccountGlBalance);
                Assert.True(after.Data.Reconciled);
            }
        }
        // An institution administrator in a second tenant must not see even
        // one synthetic source or a cent of the first tenant's GL.
        await using (var db = Db(options, otherTenantId, otherAdminId))
        {
            var foreign = await Service(db, otherTenantId, otherAdminId)
                .SupplierApInstitutionControlAsync(asOf, otherCampusId);
            Assert.True(foreign.Success, foreign.Message);
            Assert.Equal(0, foreign.Data!.InvoiceCount);
            Assert.Equal(0m, foreign.Data.ControlAccountGlBalance);
            Assert.True(foreign.Data.Reconciled);
        }
        var durations = sql.Durations.Order().ToArray();
        Assert.NotEmpty(durations);
        Assert.Equal(1, sql.PopulationCounts);
        output.WriteLine($"SUPPLIER_AP_LOAD invoices={invoiceCount} fixture_ms={fixture.ElapsedMilliseconds} " +
            $"audit_ms={audit.ElapsedMilliseconds} sql_commands={durations.Length} " +
            $"sql_p50_ms={Percentile(durations, 50):F2} sql_p95_ms={Percentile(durations, 95):F2} " +
            $"sql_p99_ms={Percentile(durations, 99):F2} population_counts={sql.PopulationCounts} " +
            $"concurrent_writer_wait_ms={concurrentWriteWaitMs} " +
            $"reconciled=true cross_tenant_clean=true");
    }

    // Direct SQL is restricted to the disposable benchmark. All four writes
    // share the caller's transaction; no incomplete invoice is ever committed.
    private static Task<int> InsertSyntheticBatchAsync(EduOSDbContext db, int count,
        string prefix, DateOnly asOf, long tenantId, long poId, long supplierId,
        long campusId, long periodId, long inventoryId, long payableId, decimal amount) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            ;WITH N AS (
                SELECT TOP ({count}) ROW_NUMBER() OVER (ORDER BY a.object_id, b.object_id) AS n
                FROM sys.all_objects a CROSS JOIN sys.all_objects b
            )
            INSERT INTO dbo.ProcurementSupplierInvoices
              (PublicId, ClientRequestId, PurchaseOrderId, SupplierId, CampusId,
               SupplierInvoiceNumber, NormalizedSupplierInvoiceNumber, InvoiceDate, DueDate,
               StateCode, PriceMode, Reason, CreateFingerprint, TotalAmount, StockValue,
               PriceVariance, CreatedAt, IsDeleted, TenantId)
            SELECT NEWID(), NEWID(), {poId}, {supplierId}, {campusId},
               CONCAT({prefix}, n), CONCAT({prefix}, n), {asOf}, {asOf},
               N'Posted', N'Automatic', N'Disposable SQL-only performance fixture',
               N'SYNTHETIC-ONLY', {amount}, {amount}, 0,
               SYSUTCDATETIME(), 0, {tenantId}
            FROM N;

            INSERT INTO dbo.Journals
              (PublicId, JournalNumber, JournalDate, AccountingPeriodId, CampusId,
               CurrencyCode, State, SourceType, SourceId, CreatedAt, IsDeleted, TenantId)
            SELECT NEWID(), CONCAT({prefix}, i.Id), {asOf}, {periodId}, {campusId},
               N'BDT', {(int)JournalState.Posted}, N'ProcurementSupplierInvoice', i.Id,
               SYSUTCDATETIME(), 0, {tenantId}
            FROM dbo.ProcurementSupplierInvoices i
            WHERE i.TenantId = {tenantId} AND i.PurchaseOrderId = {poId}
              AND i.SupplierInvoiceNumber LIKE {prefix} + N'%';

            UPDATE i SET JournalId = j.Id
            FROM dbo.ProcurementSupplierInvoices i
            JOIN dbo.Journals j ON j.TenantId = i.TenantId
              AND j.SourceType = N'ProcurementSupplierInvoice' AND j.SourceId = i.Id
            WHERE i.TenantId = {tenantId} AND i.PurchaseOrderId = {poId}
              AND i.SupplierInvoiceNumber LIKE {prefix} + N'%';

            INSERT INTO dbo.JournalLines
              (JournalId, [LineNo], AccountId, CampusId, DebitAmount, CreditAmount,
               CreatedAt, IsDeleted, TenantId)
            SELECT j.Id, leg.[LineNo], leg.[AccountId], {campusId}, leg.[Debit], leg.[Credit],
               SYSUTCDATETIME(), 0, {tenantId}
            FROM dbo.Journals j
            CROSS APPLY (VALUES (1, {inventoryId}, {amount}, CAST(0 AS decimal(18,2))),
                                (2, {payableId}, CAST(0 AS decimal(18,2)), {amount}))
              leg([LineNo], [AccountId], [Debit], [Credit])
            WHERE j.TenantId = {tenantId} AND j.SourceType = N'ProcurementSupplierInvoice'
              AND j.SourceId IN (SELECT Id FROM dbo.ProcurementSupplierInvoices
                                WHERE TenantId = {tenantId} AND PurchaseOrderId = {poId}
                                  AND SupplierInvoiceNumber LIKE {prefix} + N'%');
            """);

    private static double Percentile(double[] sorted, int percentile) =>
        sorted[(int)Math.Ceiling(sorted.Length * percentile / 100.0) - 1];

    private static ApplicationUser User(string email) => new() {
        UserName = email, NormalizedUserName = email.ToUpperInvariant(),
        Email = email, NormalizedEmail = email.ToUpperInvariant(),
        FullName = "Disposable AP test actor", EmailConfirmed = true, IsActive = true,
        SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString()
    };

    private static EduOSDbContext Db(DbContextOptions<EduOSDbContext> options,
        long tenantId, long actorId)
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, actorId.ToString()),
             new Claim(ClaimTypes.Role, "TenantAdmin")], "Test")) };
        http.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private static FacilityVendorServiceBillService Service(EduOSDbContext db,
        long tenantId, long actorId) => new(db, new Principal(tenantId, actorId),
            TimeProvider.System, NullLogger<FacilityVendorServiceBillService>.Instance);

    private sealed class Principal(long tenant, long actorId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => actorId;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }

    private sealed class SqlLatencyProbe : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<double> durations = new();
        private readonly TaskCompletionSource<bool> countObserved = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int populationCounts;
        public double[] Durations => durations.ToArray();
        public int PopulationCounts => Volatile.Read(ref populationCounts);
        public Task PopulationCountObserved => countObserved.Task;

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            durations.Enqueue(eventData.Duration.TotalMilliseconds);
            if (command.CommandText.Contains("COUNT(", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("ProcurementSupplierInvoices", StringComparison.Ordinal) &&
                command.CommandText.Contains("FacilityVendorServiceBills", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref populationCounts);
                countObserved.TrySetResult(true);
            }
            return new ValueTask<DbDataReader>(result);
        }
    }
}
