using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Facilities;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Platform;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class CentralWorkCenterServiceTests
{
    [Fact]
    public async Task Finance_queue_includes_only_other_makers_submitted_institution_journals_and_reversals()
    {
        await using var db = Database();
        SeedIdentity(db, "TenantAdmin");
        SeedFinanceWork(db);
        await db.SaveChangesAsync();
        // Posted journals are immutable even in synthetic test contexts. Only
        // the submitted records need their historical maker attribution fixed.
        foreach (var journal in db.Journals.Local.Where(x => x.State == JournalState.Submitted))
            journal.CreatedBy = journal.Id == 102 ? 9 : 8;
        await db.SaveChangesAsync();

        var service = Service(db, "TenantAdmin", inventory: false, facilities: false, finance: true);
        var result = await service.GetPendingAsync();
        result.StatusCode.Should().Be(200);
        result.Data!.FinanceIncluded.Should().BeTrue();
        result.Data.Pending.TotalCount.Should().Be(2);
        result.Data.Pending.Items.Select(x => x.SourceId).Should().BeEquivalentTo(new long[] { 101, 103 });
        result.Data.Pending.Items.Should().OnlyContain(x =>
            x.Source == "AccountingJournal" && x.CampusId == null &&
            x.SourceReference.HasValue && x.State == "Submitted" &&
            x.WorkspaceUrl == "/AccountingJournals/Index");
        db.ChangeTracker.HasChanges().Should().BeFalse();

        var campus = await service.GetPendingAsync(campusId: 10);
        campus.StatusCode.Should().Be(200);
        campus.Data!.FinanceIncluded.Should().BeFalse();
        campus.Data.Pending.TotalCount.Should().Be(0);
        var disabled = await Service(db, "TenantAdmin", inventory: false, facilities: false,
            finance: false).GetPendingAsync();
        disabled.Data!.FinanceIncluded.Should().BeFalse();
        disabled.Data.Pending.TotalCount.Should().Be(0);
        var inventoryOnly = await Service(db, "InventoryManager", inventory: false,
            facilities: false, finance: true).GetPendingAsync();
        inventoryOnly.Data!.Pending.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Central_queue_shows_three_independent_sources_without_own_or_other_tenant_records()
    {
        var databaseName = "work-center-" + Guid.NewGuid().ToString("N");
        await using (var otherTenant = Database(databaseName, scoped: false))
        {
            otherTenant.Campuses.Add(new Campus { Id = 20, TenantId = 42, Code = "FOREIGN", Name = "Other institute" });
            otherTenant.ProcurementRequisitions.Add(new ProcurementRequisition { Id = 9, TenantId = 42,
                CampusId = 20, RequestNumber = "PR-9", Purpose = "Foreign tenant",
                StateCode = "Submitted", CreatedBy = 8 });
            await otherTenant.SaveChangesAsync();
        }
        await using var db = Database(databaseName);
        SeedIdentity(db, "TenantAdmin");
        SeedWork(db);
        db.ProcurementRequisitions.Add(new ProcurementRequisition { Id = 8, TenantId = 41,
            CampusId = 10, RequestNumber = "PR-8", Purpose = "Legacy creator not recorded",
            StateCode = "Submitted" });
        await db.SaveChangesAsync();
        await RestoreSeedMakerAuditAsync(db);

        var service = Service(db, "TenantAdmin");
        var first = await service.GetPendingAsync(1, 2);
        var second = await service.GetPendingAsync(2, 2);
        first.StatusCode.Should().Be(200);
        first.Data!.InventoryIncluded.Should().BeTrue();
        first.Data.FacilitiesIncluded.Should().BeTrue();
        first.Data.SourceCounts.Sum(x => x.Count).Should().Be(4);
        first.Data.SourceCounts.Should().Contain(x => x.Source == "ProcurementPurchaseOrder" && x.Count == 2);
        first.Data.Pending.TotalCount.Should().Be(4);
        second.Data!.Pending.TotalCount.Should().Be(4);
        var rows = first.Data.Pending.Items.Concat(second.Data.Pending.Items).ToArray();
        rows.Should().HaveCount(4);
        rows.Select(x => x.Source + ":" + x.SourceId).Should().OnlyHaveUniqueItems();
        rows.Select(x => x.Source).Should().Contain("ProcurementRequisition")
            .And.Contain("ProcurementPurchaseOrder").And.Contain("FacilityVisitorPolicy");
        rows.Should().OnlyContain(x => x.CampusId == 10 && x.SourceId != 2 && x.SourceId != 9);
        db.ChangeTracker.HasChanges().Should().BeFalse();
    }

    [Fact]
    public async Task Inventory_reviewer_sees_only_entitled_campus_and_cannot_review_own_request()
    {
        await using var db = Database();
        SeedIdentity(db, "InventoryManager");
        SeedWork(db);
        db.Campuses.Add(new Campus { Id = 11, TenantId = 41, Code = "C11", Name = "Other campus" });
        db.ProcurementRequisitions.Add(new ProcurementRequisition { Id = 7, TenantId = 41,
            CampusId = 11, RequestNumber = "PR-7", Purpose = "Other campus",
            StateCode = "Submitted", CreatedBy = 8 });
        await db.SaveChangesAsync();
        await RestoreSeedMakerAuditAsync(db);

        var service = Service(db, "InventoryManager");
        var result = await service.GetPendingAsync();
        result.StatusCode.Should().Be(200);
        result.Data!.InventoryIncluded.Should().BeTrue();
        result.Data.FacilitiesIncluded.Should().BeFalse();
        result.Data.Pending.TotalCount.Should().Be(3);
        result.Data.Pending.Items.Should().OnlyContain(x => x.CampusId == 10 &&
            x.Source != "FacilityVisitorPolicy");
        (await service.GetPendingAsync(campusId: 11)).StatusCode.Should().Be(403);
    }


    [Fact]
    public async Task Tenant_admin_requested_campus_filters_all_three_sources_before_count_and_paging()
    {
        await using var db = Database();
        SeedIdentity(db, "TenantAdmin");
        SeedWork(db);
        db.Campuses.Add(new Campus { Id = 11, TenantId = 41, Code = "SECOND", Name = "Second campus" });
        db.ProcurementRequisitions.Add(new ProcurementRequisition { Id = 11, TenantId = 41,
            CampusId = 11, RequestNumber = "PR-11", Purpose = "Second campus requisition",
            StateCode = "Submitted", CreatedBy = 8 });
        db.ProcurementPurchaseOrders.Add(new ProcurementPurchaseOrder { Id = 12, TenantId = 41,
            CampusId = 11, OrderNumber = "PO-12", StateCode = "Submitted", CreatedBy = 8 });
        db.FacilityVisitorPolicyChanges.Add(new FacilityVisitorPolicyChange { Id = 13, TenantId = 41,
            CampusId = 11, PolicyCode = "SecondCampusPolicy", ProposedValue = "30",
            RequestReason = "Second campus request", RequestedByUserId = 8,
            StateCode = "Submitted" });
        await db.SaveChangesAsync();
        await RestoreSeedMakerAuditAsync(db);
        // Synthetic insert audit sets the current actor. Restore the independent makers.
        foreach (var req in db.ProcurementRequisitions.Local.Where(x => x.Id == 11))
            req.CreatedBy = 8;
        foreach (var po in db.ProcurementPurchaseOrders.Local.Where(x => x.Id == 12))
            po.CreatedBy = 8;
        await db.SaveChangesAsync();

        var service = Service(db, "TenantAdmin");
        var all = await service.GetPendingAsync();
        all.StatusCode.Should().Be(200);
        all.Data!.Pending.TotalCount.Should().Be(7);

        var campus = await service.GetPendingAsync(page: 1, pageSize: 2, campusId: 11);
        var next = await service.GetPendingAsync(page: 2, pageSize: 2, campusId: 11);
        campus.StatusCode.Should().Be(200);
        next.StatusCode.Should().Be(200);
        campus.Data!.Pending.TotalCount.Should().Be(3);
        next.Data!.Pending.TotalCount.Should().Be(3);
        var rows = campus.Data.Pending.Items.Concat(next.Data.Pending.Items).ToArray();
        rows.Should().HaveCount(3);
        rows.Should().OnlyContain(x => x.CampusId == 11);
        rows.Select(x => x.Source).Should().Contain("ProcurementRequisition")
            .And.Contain("ProcurementPurchaseOrder").And.Contain("FacilityVisitorPolicy");

        var main = await service.GetPendingAsync(campusId: 10);
        main.StatusCode.Should().Be(200);
        main.Data!.Pending.TotalCount.Should().Be(4);
        main.Data.Pending.Items.Should().OnlyContain(x => x.CampusId == 10);
        (await service.GetPendingAsync(campusId: 999)).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task My_work_source_filter_counts_and_pages_on_server_without_leaking_other_sources()
    {
        await using var db = Database();
        SeedIdentity(db, "TenantAdmin");
        SeedWork(db);
        await db.SaveChangesAsync();
        await RestoreSeedMakerAuditAsync(db);
        var service = Service(db, "TenantAdmin");

        var all = await service.GetPendingAsync();
        all.Data!.Pending.TotalCount.Should().Be(4);
        var req = await service.GetPendingAsync(page: 1, pageSize: 1,
            source: "ProcurementRequisition");
        req.StatusCode.Should().Be(200);
        req.Data!.Pending.TotalCount.Should().Be(1);
        req.Data.SourceCounts.Sum(x => x.Count).Should().Be(4);
        req.Data.SourceCounts.Should().Contain(x => x.Source == "ProcurementRequisition" && x.Count == 1);
        req.Data.Pending.Items.Should().ContainSingle()
            .Which.Source.Should().Be("ProcurementRequisition");
        var orders = await service.GetPendingAsync(page: 1, pageSize: 1,
            source: "ProcurementPurchaseOrder");
        orders.Data!.Pending.TotalCount.Should().Be(2);
        orders.Data.Pending.Items.Should().ContainSingle()
            .Which.Source.Should().Be("ProcurementPurchaseOrder");
        var next = await service.GetPendingAsync(page: 2, pageSize: 1,
            source: "ProcurementPurchaseOrder");
        next.Data!.Pending.TotalCount.Should().Be(2);
        next.Data.Pending.Items.Should().ContainSingle()
            .Which.SourceId.Should().NotBe(orders.Data.Pending.Items.Single().SourceId);
        var policies = await service.GetPendingAsync(source: "FacilityVisitorPolicy");
        policies.Data!.Pending.TotalCount.Should().Be(1);
        var noFinance = await service.GetPendingAsync(source: "AccountingJournal");
        noFinance.Data!.Pending.TotalCount.Should().Be(0);
        noFinance.Data.SourceCounts.Should().Contain(x => x.Source == "ProcurementRequisition" && x.Count == 1);
        noFinance.Data.SourceCounts.Should().NotContain(x => x.Source == "AccountingJournal");
        (await service.GetPendingAsync(source: "BadSource")).StatusCode.Should().Be(400);
        (await service.GetPendingAsync(source: "ProcurementPurchaseOrder", campusId: 999))
            .StatusCode.Should().Be(403);
        db.ChangeTracker.HasChanges().Should().BeFalse();
    }

    [Fact]
    public async Task Disabled_module_pilot_and_revoked_role_never_expose_pending_work()
    {
        await using var db = Database();
        SeedIdentity(db, "TenantAdmin");
        SeedWork(db);
        await db.SaveChangesAsync();

        var none = await Service(db, "TenantAdmin", inventory: false, facilities: false).GetPendingAsync();
        none.StatusCode.Should().Be(200);
        none.Data!.Pending.TotalCount.Should().Be(0);
        (await Service(db, "TenantAdmin", pilot: true).GetPendingAsync())
            .Data!.Pending.TotalCount.Should().Be(0);
        (await Service(db, "TenantAdmin").GetPendingAsync(pageSize: 101)).StatusCode.Should().Be(400);
        (await Service(db, "TenantAdmin").GetPendingAsync(campusId: -1)).StatusCode.Should().Be(400);

        db.UserRoles.Remove(db.UserRoles.Single());
        await db.SaveChangesAsync();
        (await Service(db, "TenantAdmin").GetPendingAsync()).StatusCode.Should().Be(403);
        db.TenantMemberships.Single().Status = MembershipStatus.Suspended;
        await db.SaveChangesAsync();
        (await Service(db, "TenantAdmin").GetPendingAsync()).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Work_center_uses_canonical_owner_access_and_rejects_global_system_role()
    {
        await using var db = Database();
        SeedIdentity(db, "TenantAdmin");
        await db.SaveChangesAsync();
        var role = db.Roles.Single(x => x.Id == 2);
        role.IsActive = false;
        await db.SaveChangesAsync();
        (await Service(db, "TenantAdmin").GetPendingAsync()).StatusCode.Should().Be(403);

        var member = db.TenantMemberships.Single(x => x.UserId == 9);
        member.IsOwner = true;
        await db.SaveChangesAsync();
        (await Service(db, "TenantAdmin").GetPendingAsync()).StatusCode.Should().Be(200);

        member.IsOwner = false;
        db.Roles.Add(new ApplicationRole { Id = 3, TenantId = null,
            Name = "TenantAdmin", NormalizedName = "TENANTADMIN", IsSystemRole = true,
            IsActive = true });
        db.UserRoles.Add(new IdentityUserRole<long> { RoleId = 3, UserId = 9 });
        await db.SaveChangesAsync();
        (await Service(db, "TenantAdmin").GetPendingAsync()).StatusCode.Should().Be(403);
    }

    private static CentralWorkCenterService Service(EduOSDbContext db, string role,
        bool inventory = true, bool facilities = true, bool pilot = false, bool finance = false)
    {
        var modules = new Mock<ITenantModuleService>();
        modules.Setup(x => x.IsCurrentTenantModuleAvailableAsync("INVENTORY"))
            .ReturnsAsync(inventory);
        modules.Setup(x => x.IsCurrentTenantModuleAvailableAsync("FACILITIES"))
            .ReturnsAsync(facilities);
        modules.Setup(x => x.IsCurrentTenantModuleAvailableAsync("FINANCE"))
            .ReturnsAsync(finance);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["PilotRelease:Enabled"] = pilot.ToString() }).Build();
        return new CentralWorkCenterService(db, new User(role), modules.Object, config);
    }

    private static void SeedIdentity(EduOSDbContext db, string role)
    {
        db.Users.Add(new ApplicationUser { Id = 9, UserName = "reviewer" });
        db.TenantMemberships.Add(new TenantMembership { Id = 1, TenantId = 41,
            UserId = 9, Status = MembershipStatus.Active });
        db.Roles.Add(new ApplicationRole { Id = 2, TenantId = 41,
            Name = role, NormalizedName = role.ToUpperInvariant() });
        db.UserRoles.Add(new IdentityUserRole<long> { UserId = 9, RoleId = 2 });
        db.Campuses.Add(new Campus { Id = 10, TenantId = 41, Code = "MAIN", Name = "Main campus" });
        db.UserCampusAccesses.Add(new UserCampusAccess { Id = 1, TenantId = 41,
            UserId = 9, CampusId = 10, IsActive = true });
    }

    private static void SeedWork(EduOSDbContext db)
    {
        var at = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);
        db.ProcurementRequisitions.AddRange(
            new ProcurementRequisition { Id = 1, TenantId = 41, CampusId = 10,
                RequestNumber = "PR-1", Purpose = "For independent review", StateCode = "Submitted",
                CreatedBy = 8, SubmittedAt = at },
            new ProcurementRequisition { Id = 2, TenantId = 41, CampusId = 10,
                RequestNumber = "PR-2", Purpose = "Created by reviewer", StateCode = "Submitted",
                CreatedBy = 9, SubmittedAt = at });
        db.ProcurementPurchaseOrders.AddRange(
            new ProcurementPurchaseOrder { Id = 3, TenantId = 41, CampusId = 10,
                OrderNumber = "PO-3", StateCode = "Submitted", CreatedBy = 8,
                SubmittedAt = at.AddMinutes(1) },
            new ProcurementPurchaseOrder { Id = 4, TenantId = 41, CampusId = 10,
                OrderNumber = "PO-4", StateCode = "CancelRequested", CreatedBy = 8,
                SubmittedAt = at.AddMinutes(2) });
        db.FacilityVisitorPolicyChanges.AddRange(
            new FacilityVisitorPolicyChange { Id = 5, TenantId = 41, CampusId = 10,
                PolicyCode = "RetentionDays", ProposedValue = "365", RequestReason = "Adjust policy",
                RequestedByUserId = 8, CreatedAt = at.AddMinutes(3), StateCode = "Submitted" },
            new FacilityVisitorPolicyChange { Id = 6, TenantId = 41, CampusId = 10,
                PolicyCode = "LegalHold", ProposedValue = "true", RequestReason = "Own policy",
                RequestedByUserId = 9, StateCode = "Submitted" });
    }

    private static void SeedFinanceWork(EduOSDbContext db)
    {
        var at = new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);
        void Add(long id, JournalState state, string source, long? sourceId = null,
            long? reversalId = null, long? campusId = null)
            => db.Journals.Add(new Journal { Id = id, TenantId = 41,
                PublicId = Guid.NewGuid(), JournalNumber = "JV-" + id,
                JournalDate = new DateOnly(2026, 11, 5), AccountingPeriodId = 1,
                State = state, SourceType = source, SourceId = sourceId,
                ReversalOfJournalId = reversalId, CampusId = campusId,
                CreatedBy = 8, CreatedAt = at, UpdatedAt = at.AddMinutes((int)(id - 100)) });
        Add(101, JournalState.Submitted, "Manual");
        Add(102, JournalState.Submitted, "Manual"); // The reviewer is the maker.
        Add(103, JournalState.Submitted, "ManualReversal", 90, 90);
        Add(104, JournalState.Posted, "Manual");
        Add(105, JournalState.Submitted, "StudentInvoice", 77);
        Add(106, JournalState.Submitted, "Manual", campusId: 10);
    }

    private static Task RestoreSeedMakerAuditAsync(EduOSDbContext db)
    {
        // SaveChanges applies the HTTP actor on inserts. This synthetic test context has no
        // maker principal, so restore the historical creator snapshots after insertion.
        // Missing creator remains missing and must never enter a maker-checker inbox.
        foreach (var requisition in db.ProcurementRequisitions.Local.Where(x => x.Id is 1 or 2 or 7))
            requisition.CreatedBy = requisition.Id == 2 ? 9 : 8;
        foreach (var order in db.ProcurementPurchaseOrders.Local)
            order.CreatedBy = 8;
        return db.SaveChangesAsync();
    }

    private static EduOSDbContext Database(string? databaseName = null, bool scoped = true)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        if (scoped) accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase(databaseName ?? "work-center-" + Guid.NewGuid().ToString("N")).Options, accessor);
    }

    private sealed class User(string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 9;
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
