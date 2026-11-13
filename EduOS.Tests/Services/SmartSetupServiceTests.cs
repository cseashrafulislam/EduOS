using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Tenants;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class SmartSetupServiceTests
{
    // AsyncLocal-backed HttpContextAccessor is not retained when an awaited
    // fixture factory returns. Keep each test DbContext request explicit.
    private sealed class IsolatedHttpContextAccessor(HttpContext context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    private sealed class User(long tenant, bool admin = true) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 51;
        public long TenantId => tenant;
        public string? FullName => "Setup Administrator";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => admin;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : ["Teacher"];
        public bool IsInRole(string role) => Roles.Contains(role);
        public string? IpAddress => null;
        public string? UserAgent => null;
    }

    private sealed class Modules(params string[] enabled) : ITenantModuleService
    {
        private readonly HashSet<string> _enabled = new(enabled, StringComparer.OrdinalIgnoreCase);
        public Task<ApiResponse<List<TenantModuleDto>>> GetCurrentTenantModulesAsync() =>
            Task.FromResult(ApiResponse<List<TenantModuleDto>>.SuccessResponse(new()));
        public Task<ApiResponse<TenantModuleDto>> UpdateCurrentTenantModuleAsync(
            string moduleCode, UpdateTenantModuleRequestDto request) =>
            Task.FromResult(ApiResponse<TenantModuleDto>.ErrorResponse("Test-only."));
        public Task<ApiResponse<bool>> ValidateCurrentTenantSelectionAsync() =>
            Task.FromResult(ApiResponse<bool>.SuccessResponse(true));
        public Task<bool> IsCurrentTenantModuleAvailableAsync(string moduleCode) =>
            Task.FromResult(_enabled.Contains(moduleCode));
        public Task<ApiResponse<bool>> ApplyInstitutionPresetAsync(long tenantId,
            long institutionTypeDefinitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResponse<bool>.ErrorResponse("Test-only."));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required EduOSDbContext Db { get; init; }
        public required DefaultHttpContext Http { get; init; }
        public required SmartSetupService Service { get; init; }
        public required long Tenant { get; init; }
        public required long InstitutionType { get; init; }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private static async Task<Fixture> Open(string typeCode = "SCHOOL",
        bool admin = true, params string[] modules)
    {
        var http = new DefaultHttpContext();
        var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("smart-setup-" + Guid.NewGuid().ToString("N")).Options,
            new IsolatedHttpContextAccessor(http));
        var category = new InstitutionTypeDefinition
        {
            Code = typeCode, Name = typeCode, IsActive = true
        };
        db.InstitutionTypeDefinitions.Add(category);
        await db.SaveChangesAsync();
        var tenant = new Tenant
        {
            InstitutionTypeDefinitionId = category.Id, Name = "Smart Setup School",
            Code = "SMART-" + Guid.NewGuid().ToString("N")[..8],
            Email = "school@example.test", CountryCode = "BD",
            State = EduOS.Core.Enums.Domain.TenantState.Active
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        http.Items["TenantId"] = tenant.Id;
        var currentUser = new User(tenant.Id, admin);
        var service = new SmartSetupService(db, new Modules(modules), db, currentUser,
            NullLogger<SmartSetupService>.Instance);
        return new Fixture
        {
            Db = db, Http = http, Service = service,
            Tenant = tenant.Id, InstitutionType = category.Id
        };
    }

    private static ApplySmartSetupRequestDto Request(Fixture f, params string[] modules) => new()
    {
        ClientRequestId = Guid.NewGuid(), InstitutionTypeId = f.InstitutionType,
        TemplateVersion = "2026.11.4", Confirmed = true, Modules = modules.ToList()
    };

    [Fact]
    public async Task Role_presets_create_only_scoped_definitions_and_are_idempotent()
    {
        await using var f = await Open(modules: ["CORE_ADMIN"]);
        var preview = await f.Service.PreviewAsync();
        Assert.True(preview.Success, preview.Message);
        var roles = preview.Data!.Modules.Single(x => x.Code == "ROLES");
        Assert.True(roles.IsAvailable);
        Assert.True(roles.SupportsSafeApply);
        Assert.Equal(12, roles.Entries.Count);
        Assert.Contains(roles.Entries, x => x.Name == "Teacher" && x.Status == "Missing");
        Assert.DoesNotContain(roles.Entries, x => x.Name is "TenantAdmin" or "SuperAdmin" or "Admin");
        var request = Request(f, "ROLES");
        request.Entries = [new() { Module = "ROLES", Kind = "Role", Code = "TEACHER" }];
        var applied = await f.Service.ApplyAsync(request);
        Assert.True(applied.Success, applied.Message);
        Assert.Equal(1, applied.Data!.CreatedCount);
        var created = await f.Db.Roles.SingleAsync(x => x.TenantId == f.Tenant);
        Assert.Equal("Teacher", created.Name);
        Assert.False(created.IsSystemRole);
        Assert.Empty(await f.Db.UserRoles.ToListAsync());
        Assert.Empty(await f.Db.RolePermissions.ToListAsync());
        Assert.True((await f.Service.ApplyAsync(request)).Success);
        Assert.Single(await f.Db.Roles.Where(x => x.TenantId == f.Tenant).ToListAsync());
        Assert.Equal("Existing", (await f.Service.PreviewAsync()).Data!.Modules
            .Single(x => x.Code == "ROLES").Entries.Single(x => x.Name == "Teacher").Status);
    }

    [Fact]
    public async Task Role_setup_blocks_privileged_and_unknown_rows_without_partial_writes()
    {
        await using var f = await Open(modules: ["CORE_ADMIN"]);
        var request = Request(f, "ROLES");
        request.Entries = [new() { Module = "ROLES", Kind = "Role", Code = "TENANTADMIN" }];
        Assert.Equal(409, (await f.Service.ApplyAsync(request)).StatusCode);
        request.Entries = [new() { Module = "ROLES", Kind = "Role", Code = "SUPERADMIN" }];
        Assert.Equal(409, (await f.Service.ApplyAsync(request)).StatusCode);
        request.Entries = [new() { Module = "ROLES", Kind = "Role", Code = "TEACHER" },
            new() { Module = "ROLES", Kind = "Role", Code = "INVENTED" }];
        Assert.Equal(409, (await f.Service.ApplyAsync(request)).StatusCode);
        Assert.Empty(await f.Db.Roles.ToListAsync());
    }

    [Fact]
    public async Task Role_setup_is_denied_when_core_admin_is_disabled_or_institution_is_changed()
    {
        await using var f = await Open(modules: ["ACADEMIC"]);
        Assert.False((await f.Service.PreviewAsync()).Data!.Modules.Single(x => x.Code == "ROLES").IsAvailable);
        Assert.Equal(403, (await f.Service.ApplyAsync(Request(f, "ROLES"))).StatusCode);
        Assert.Empty(await f.Db.Roles.ToListAsync());
        f.Http.Items["TenantId"] = f.Tenant + 1;
        Assert.Equal(403, (await f.Service.PreviewAsync()).StatusCode);
    }

    [Fact]
    public async Task All_reviewed_master_defaults_are_additive_idempotent_and_audited()
    {
        await using var f = await Open(modules: ["ACADEMIC", "HR", "FINANCE", "INVENTORY"]);
        var preview = await f.Service.PreviewAsync();
        Assert.True(preview.Success, preview.Message);
        Assert.Equal("SCHOOL", preview.Data!.InstitutionTypeCode);
        Assert.Contains(preview.Data.Modules.Single(x => x.Code == "ACADEMIC").Entries,
            x => x.Code == "BANGLA" && x.Status == "Missing");
        var request = Request(f, "ACADEMIC", "HR", "FEES", "INVENTORY");
        var first = await f.Service.ApplyAsync(request);
        Assert.True(first.Success, first.Message);
        var expected = preview.Data.Modules.Where(x => request.Modules.Contains(x.Code))
            .Sum(x => x.Entries.Count(e => e.Status == "Missing"));
        Assert.Equal(expected, first.Data!.CreatedCount);
        Assert.Equal(2, await f.Db.Mediums.CountAsync(x => x.TenantId == f.Tenant));
        Assert.Equal(12, await f.Db.OrganizationUnits.CountAsync(x => x.TenantId == f.Tenant));
        Assert.Equal(12, await f.Db.Designations.CountAsync(x => x.TenantId == f.Tenant));
        Assert.Equal(12, await f.Db.FeeHeads.CountAsync(x => x.TenantId == f.Tenant));
        Assert.Equal(12, await f.Db.AssetCategories.CountAsync(x => x.TenantId == f.Tenant));
        Assert.Equal(EduOS.Core.Enums.Domain.FeeFrequencyType.PerTerm,
            (await f.Db.FeeHeads.SingleAsync(x => x.Code == "EXAMINATION")).DefaultFrequency);
        Assert.Equal(EduOS.Core.Enums.Domain.FeeFrequencyType.OneTime,
            (await f.Db.FeeHeads.SingleAsync(x => x.Code == "ADMISSION")).DefaultFrequency);
        Assert.Equal(0, await f.Db.FeeStructures.CountAsync());
        Assert.Equal(0, await f.Db.StudentEnrollments.CountAsync());
        var replay = await f.Service.ApplyAsync(request);
        Assert.True(replay.Success, replay.Message);
        Assert.Equal(expected, replay.Data!.CreatedCount);
        Assert.Equal(0, replay.Data.ExistingCount);
        Assert.Equal(1, await f.Db.AuditLogs.CountAsync(x => x.Action == "SmartSetup.Apply" && x.TenantId == f.Tenant));
    }

    [Fact]
    public async Task One_confirmed_apply_can_create_all_seven_enabled_safe_reference_groups()
    {
        await using var f = await Open(modules:
            ["ACADEMIC", "HR", "FINANCE", "INVENTORY", "LIBRARY", "COMMUNICATION", "DOCUMENTS"]);
        var preview = await f.Service.PreviewAsync();
        Assert.True(preview.Success, preview.Message);
        var request = Request(f, "ACADEMIC", "HR", "FEES", "INVENTORY",
            "LIBRARY", "COMMUNICATION", "DOCUMENTS");
        request.Entries = preview.Data!.Modules
            .Where(m => request.Modules.Contains(m.Code))
            .SelectMany(m => m.Entries.Where(e => e.Status == "Missing")
                .Select(e => new SmartSetupSelectedEntryDto
                {
                    Module = m.Code, Kind = e.Kind, Code = e.Code
                })).ToList();
        Assert.True(request.Entries.Count > 86 && request.Entries.Count <= 200);
        var saved = await f.Service.ApplyAsync(request);
        Assert.True(saved.Success, saved.Message);
        Assert.Equal(request.Entries.Count, saved.Data!.CreatedCount);
        Assert.Equal(0, await f.Db.FeeStructures.CountAsync());
        Assert.Equal(0, await f.Db.StudentEnrollments.CountAsync());
        Assert.Equal(0, await f.Db.HostelRooms.CountAsync());
        var replay = await f.Service.ApplyAsync(request);
        Assert.True(replay.Success);
        Assert.Equal(request.Entries.Count, replay.Data!.CreatedCount);
        Assert.Single(await f.Db.AuditLogs.Where(x => x.Action == "SmartSetup.Apply").ToListAsync());
    }


    [Fact]
    public async Task Bangladesh_grading_subject_account_and_payroll_templates_are_canonical_and_retry_safe()
    {
        await using var f = await Open(modules: ["ACADEMIC", "EXAM", "FINANCE", "HR"]);
        var preview = await f.Service.PreviewAsync();
        Assert.True(preview.Success, preview.Message);
        var requested = new[] { "ACADEMIC", "ASSESSMENT", "FINANCE", "PAYROLL" };
        var selected = preview.Data!.Modules.Where(x => requested.Contains(x.Code))
            .SelectMany(x => x.Entries.Where(e => e.Status == "Missing").Select(e =>
                new SmartSetupSelectedEntryDto { Module = x.Code, Kind = e.Kind, Code = e.Code }))
            .ToList();
        Assert.Contains(selected, x => x.Kind == "GradeScheme" && x.Code == "BD-GPA-5");
        Assert.Contains(selected, x => x.Kind == "GradeScheme" && x.Code == "BD-CGPA-4");
        Assert.Contains(selected, x => x.Kind == "Subject" && x.Code == "ICT");
        Assert.Contains(selected, x => x.Kind == "Account" && x.Code == "1000");
        Assert.Contains(selected, x => x.Kind == "SalaryComponent" && x.Code == "BASIC");
        Assert.InRange(selected.Count, 50, 200);
        var request = Request(f, requested);
        request.Entries = selected;
        var saved = await f.Service.ApplyAsync(request);
        Assert.True(saved.Success, saved.Message);
        Assert.Equal(selected.Count, saved.Data!.CreatedCount);
        Assert.Equal(2, await f.Db.GradeSchemes.CountAsync());
        Assert.Equal(17, await f.Db.GradeRules.CountAsync());
        var five = await f.Db.GradeSchemes.SingleAsync(x => x.Code == "BD-GPA-5");
        var four = await f.Db.GradeSchemes.SingleAsync(x => x.Code == "BD-CGPA-4");
        Assert.False(five.IsDefault);
        Assert.False(four.IsDefault);
        Assert.False(five.UseCreditWeightedGpa);
        Assert.True(four.UseCreditWeightedGpa);
        Assert.Equal(5m, await f.Db.GradeRules.Where(x => x.GradeSchemeId == five.Id && x.MinMarks == 80m)
            .Select(x => x.GradePoint).SingleAsync());
        Assert.Equal(3.75m, await f.Db.GradeRules.Where(x => x.GradeSchemeId == four.Id && x.MinMarks == 75m)
            .Select(x => x.GradePoint).SingleAsync());
        Assert.True(await f.Db.Subjects.AnyAsync(x => x.Code == "ICT" && x.DefaultCreditHours == 0m));
        Assert.Empty(await f.Db.CurriculumSubjects.ToListAsync());
        Assert.Equal(15, await f.Db.Accounts.CountAsync());
        var assetGroup = await f.Db.Accounts.SingleAsync(x => x.Code == "1000");
        var assetPosting = await f.Db.Accounts.SingleAsync(x => x.Code == "1100");
        Assert.True(assetGroup.IsGroup);
        Assert.False(assetGroup.AllowsPosting);
        Assert.Equal(assetGroup.Id, assetPosting.ParentAccountId);
        Assert.False(assetPosting.IsCashInHand);
        Assert.Empty(await f.Db.Journals.ToListAsync());
        Assert.Equal(10, await f.Db.SalaryComponents.CountAsync());
        Assert.Equal(SalaryComponentType.Earning,
            (await f.Db.SalaryComponents.SingleAsync(x => x.Code == "BASIC")).Type);
        Assert.Equal(SalaryComponentType.Deduction,
            (await f.Db.SalaryComponents.SingleAsync(x => x.Code == "LOAN-RECOVERY")).Type);
        Assert.Empty(await f.Db.SalaryStructures.ToListAsync());
        var replay = await f.Service.ApplyAsync(request);
        Assert.True(replay.Success, replay.Message);
        Assert.Equal(saved.Data.CreatedCount, replay.Data!.CreatedCount);
        var retry = Request(f, requested);
        retry.Entries = selected;
        var existing = await f.Service.ApplyAsync(retry);
        Assert.True(existing.Success, existing.Message);
        Assert.Equal(0, existing.Data!.CreatedCount);
        Assert.Equal(selected.Count, existing.Data.ExistingCount);
        Assert.Equal(2, await f.Db.GradeSchemes.CountAsync());
        Assert.Equal(17, await f.Db.GradeRules.CountAsync());
        Assert.Equal(15, await f.Db.Accounts.CountAsync());
        Assert.Equal(10, await f.Db.SalaryComponents.CountAsync());
    }


    [Fact]
    public async Task Bangladesh_specific_ledger_and_education_presets_are_not_assumed_for_foreign_tenants()
    {
        await using var f = await Open(modules: ["ACADEMIC", "EXAM", "FINANCE", "HR"]);
        var tenant = await f.Db.Tenants.SingleAsync();
        tenant.CountryCode = "US";
        await f.Db.SaveChangesAsync();
        var preview = await f.Service.PreviewAsync();
        Assert.True(preview.Success, preview.Message);
        Assert.DoesNotContain(preview.Data!.Modules.SelectMany(x => x.Entries),
            x => x.Kind is "GradeScheme" or "Subject" or "Account" or "SalaryComponent");
        Assert.Contains(preview.Data.Modules.Single(x => x.Code == "ACADEMIC").Entries,
            x => x.Kind == "Medium" && x.Code == "ENGLISH");
    }

    [Fact]
    public async Task Existing_custom_grades_and_mismatched_accounts_are_not_overwritten()
    {
        await using var f = await Open(modules: ["EXAM", "FINANCE"]);
        f.Db.GradeSchemes.Add(new GradeScheme
        {
            TenantId = f.Tenant, Code = "BD-GPA-5", Name = "Bangladesh Board GPA 5.00",
            IsActive = true, CreatedAt = DateTime.UtcNow
        });
        f.Db.Accounts.Add(new Account
        {
            TenantId = f.Tenant, Code = "1000", Name = "Assets", IsGroup = false,
            AccountType = AccountType.Asset, AllowsPosting = true, IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await f.Db.SaveChangesAsync();
        var preview = await f.Service.PreviewAsync();
        Assert.Equal("Conflict", preview.Data!.Modules.Single(x => x.Code == "ASSESSMENT")
            .Entries.Single(x => x.Code == "BD-GPA-5").Status);
        Assert.Equal("Conflict", preview.Data.Modules.Single(x => x.Code == "FINANCE")
            .Entries.Single(x => x.Code == "1000").Status);
        var selected = Request(f, "ASSESSMENT", "FINANCE");
        selected.Entries =
        [
            new() { Module = "ASSESSMENT", Kind = "GradeScheme", Code = "BD-GPA-5" },
            new() { Module = "FINANCE", Kind = "Account", Code = "1000" }
        ];
        var response = await f.Service.ApplyAsync(selected);
        Assert.Equal(409, response.StatusCode);
        Assert.Single(await f.Db.GradeSchemes.ToListAsync());
        Assert.Empty(await f.Db.GradeRules.ToListAsync());
        Assert.Single(await f.Db.Accounts.ToListAsync());
        Assert.Empty(await f.Db.AuditLogs.Where(x => x.Action == "SmartSetup.Apply").ToListAsync());
    }

    [Fact]
    public async Task Conflicting_existing_fee_head_never_creates_second_master_or_overwrites_it()
    {
        await using var f = await Open(modules: ["FINANCE"]);
        f.Db.FeeHeads.Add(new FeeHead
        {
            TenantId = f.Tenant, Code = "TUITION", Name = "Legacy Tuition", IsActive = true
        });
        await f.Db.SaveChangesAsync();
        var result = await f.Service.ApplyAsync(Request(f, "FEES"));
        Assert.Equal(409, result.StatusCode);
        Assert.Single(await f.Db.FeeHeads.ToListAsync());
        Assert.Equal("Legacy Tuition", (await f.Db.FeeHeads.SingleAsync()).Name);
    }

    [Fact]
    public async Task Disabled_module_and_changed_tenant_are_rejected_without_side_effects()
    {
        await using var f = await Open(modules: ["ACADEMIC"]);
        var denied = await f.Service.ApplyAsync(Request(f, "INVENTORY"));
        Assert.Equal(403, denied.StatusCode);
        f.Http.Items["TenantId"] = 999L;
        Assert.Equal(403, (await f.Service.PreviewAsync()).StatusCode);
        Assert.Equal(403, (await f.Service.ApplyAsync(Request(f, "ACADEMIC"))).StatusCode);
        Assert.Empty(await f.Db.Mediums.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Non_admin_unconfirmed_and_unreviewed_modules_cannot_apply()
    {
        await using var f = await Open(admin: false, modules: ["ACADEMIC"]);
        Assert.Equal(403, (await f.Service.PreviewAsync()).StatusCode);
        Assert.Equal(403, (await f.Service.ApplyAsync(Request(f, "ACADEMIC"))).StatusCode);
        await using var admin = await Open(modules: ["ACADEMIC"]);
        var wrong = Request(admin, "STUDENT");
        Assert.Equal(400, (await admin.Service.ApplyAsync(wrong)).StatusCode);
        var notConfirmed = Request(admin, "ACADEMIC");
        notConfirmed.Confirmed = false;
        Assert.Equal(400, (await admin.Service.ApplyAsync(notConfirmed)).StatusCode);
        Assert.Empty(await admin.Db.Mediums.ToListAsync());
    }

    [Fact]
    public async Task Library_notice_and_document_reference_records_only_create_when_selected()
    {
        await using var f = await Open(modules: ["LIBRARY", "COMMUNICATION", "DOCUMENTS"]);
        var created = await f.Service.ApplyAsync(Request(f, "LIBRARY", "COMMUNICATION", "DOCUMENTS"));
        Assert.True(created.Success, created.Message);
        Assert.Equal(36, created.Data!.CreatedCount);
        Assert.Equal(12, await f.Db.BookCategories.CountAsync());
        Assert.Equal(12, await f.Db.NoticeCategories.CountAsync());
        Assert.Equal(12, await f.Db.DocumentTypeDefinitions.CountAsync());
        Assert.Empty(await f.Db.Notices.ToListAsync());
        Assert.Empty(await f.Db.Documents.ToListAsync());
        var second = await f.Service.ApplyAsync(Request(f, "LIBRARY", "COMMUNICATION", "DOCUMENTS"));
        Assert.Equal(0, second.Data!.CreatedCount);
    }

    [Fact]
    public async Task Reused_request_reference_with_different_scope_is_rejected_without_new_inserts()
    {
        await using var f = await Open(modules: ["ACADEMIC", "HR"]);
        var first = Request(f, "ACADEMIC");
        Assert.True((await f.Service.ApplyAsync(first)).Success);
        first.Modules = ["HR"];
        var denied = await f.Service.ApplyAsync(first);
        Assert.Equal(409, denied.StatusCode);
        Assert.Empty(await f.Db.Designations.ToListAsync());
        Assert.Equal(1, await f.Db.AuditLogs.CountAsync(x => x.Action == "SmartSetup.Apply"));
    }

    [Fact]
    public async Task Disabled_and_conflicting_sections_are_separate_from_supported_safe_apply()
    {
        await using var f = await Open(modules: ["FINANCE", "EXAM", "ACADEMIC"]);
        var preview = await f.Service.PreviewAsync();
        Assert.True(preview.Success, preview.Message);
        Assert.True(preview.Data!.Modules.Single(x => x.Code == "FEES").IsAvailable);
        Assert.True(preview.Data.Modules.Single(x => x.Code == "ASSESSMENT").IsAvailable);
        Assert.False(preview.Data.Modules.Single(x => x.Code == "ASSESSMENT").SupportsSafeApply);
    }

    [Fact]
    public async Task Readiness_checks_actual_tenant_prerequisites_without_inserting_data()
    {
        await using var f = await Open(modules: ["ACADEMIC", "FINANCE", "EXAM", "PAYROLL", "HOSTEL"]);
        var checker = new SmartSetupReadinessService(f.Db,
            new Modules("ACADEMIC", "FINANCE", "EXAM", "PAYROLL", "HOSTEL"),
            new User(f.Tenant), new ConfigurationBuilder().Build(),
            NullLogger<SmartSetupReadinessService>.Instance);
        var all = await checker.GetAsync(null);
        Assert.True(all.Success, all.Message);
        Assert.True(all.Data!.Single(x => x.ModuleCode == "FEES").IsAvailable);
        Assert.True(all.Data.Single(x => x.ModuleCode == "ASSESSMENT").IsAvailable);
        Assert.False(all.Data.Single(x => x.ModuleCode == "ACADEMIC").PrerequisitesConfigured);
        Assert.Contains(all.Data.Single(x => x.ModuleCode == "ACADEMIC").Requirements,
            x => x.Code == "ACADEMIC_YEAR" && !x.Configured);
        Assert.Contains(all.Data.Single(x => x.ModuleCode == "HOSTEL").Requirements,
            x => x.Code == "HOSTEL_BED" && !x.Configured);
        Assert.Empty(await f.Db.AcademicYears.ToListAsync());
        Assert.Equal(400, (await checker.GetAsync("made-up-module")).StatusCode);
        f.Http.Items["TenantId"] = 999L;
        Assert.Equal(403, (await checker.GetAsync("ACADEMIC")).StatusCode);
    }

    [Fact]
    public async Task Pilot_excluded_modules_do_not_expose_or_apply_reference_setup()
    {
        await using var f = await Open(modules: ["ACADEMIC", "HOSTEL", "LIBRARY"]);
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["PilotRelease:Enabled"] = "true" }).Build();
        var checker = new SmartSetupReadinessService(f.Db, new Modules("ACADEMIC", "HOSTEL", "LIBRARY"),
            new User(f.Tenant), config, NullLogger<SmartSetupReadinessService>.Instance);
        var hostel = await checker.GetAsync("HOSTEL");
        Assert.True(hostel.Success, hostel.Message);
        Assert.False(hostel.Data!.Single().IsAvailable);
        var presenter = new SmartSetupService(f.Db, new Modules("ACADEMIC", "HOSTEL", "LIBRARY"),
            f.Db, new User(f.Tenant), NullLogger<SmartSetupService>.Instance, config);
        var preview = await presenter.PreviewAsync();
        Assert.False(preview.Data!.Modules.Single(x => x.Code == "LIBRARY").IsAvailable);
        Assert.Equal(403, (await presenter.ApplyAsync(Request(f, "LIBRARY"))).StatusCode);
    }

    [Fact]
    public async Task Deleted_master_codes_cannot_be_reintroduced_silently()
    {
        await using var f = await Open(modules: ["ACADEMIC"]);
        f.Db.Mediums.Add(new EduOS.Core.Entities.Academic.Medium
        {
            TenantId = f.Tenant, Code = "ENGLISH", Name = "English", IsDeleted = true
        });
        await f.Db.SaveChangesAsync();
        var result = await f.Service.ApplyAsync(Request(f, "ACADEMIC"));
        Assert.Equal(409, result.StatusCode);
        Assert.Empty(await f.Db.Mediums.ToListAsync());
    }

    [Fact]
    public async Task Institution_specific_catalog_does_not_assume_bangla_medium_for_university()
    {
        await using var f = await Open(typeCode: "UNIVERSITY", modules: ["ACADEMIC", "HR"]);
        var preview = await f.Service.PreviewAsync();
        Assert.DoesNotContain(preview.Data!.Modules.Single(x => x.Code == "ACADEMIC").Entries,
            x => x.Code == "BANGLA");
        Assert.Contains(preview.Data.Modules.Single(x => x.Code == "HR").Entries,
            x => x.Code == "LECTURER");
    }
    [Fact]
    public async Task Row_selection_saves_only_checked_masters_and_preserves_other_missing_rows()
    {
        await using var f = await Open(modules: ["ACADEMIC", "HR"]);
        var request = Request(f, "ACADEMIC", "HR");
        request.Entries =
        [
            new() { Module = "ACADEMIC", Kind = "Medium", Code = "BANGLA" },
            new() { Module = "HR", Kind = "Designation", Code = "OFFICE-STAFF" }
        ];
        var result = await f.Service.ApplyAsync(request);
        Assert.True(result.Success, result.Message);
        Assert.Equal(2, result.Data!.CreatedCount);
        Assert.Single(await f.Db.Mediums.ToListAsync());
        Assert.Equal("BANGLA", (await f.Db.Mediums.SingleAsync()).Code);
        Assert.Single(await f.Db.Designations.ToListAsync());
        Assert.Empty(await f.Db.OrganizationUnits.ToListAsync());
        var preview = await f.Service.PreviewAsync();
        Assert.Contains(preview.Data!.Modules.Single(x => x.Code == "ACADEMIC").Entries,
            x => x.Code == "ENGLISH" && x.Status == "Missing");
        var replay = await f.Service.ApplyAsync(request);
        Assert.True(replay.Success);
        Assert.Equal(1, await f.Db.AuditLogs.CountAsync(x => x.Action == "SmartSetup.Apply"));
    }

    [Fact]
    public async Task Row_selection_rejects_unknown_duplicate_and_mismatched_keys_without_writes()
    {
        await using var f = await Open(modules: ["ACADEMIC", "HR"]);
        var request = Request(f, "ACADEMIC");
        request.Entries = [new() { Module = "ACADEMIC", Kind = "Medium", Code = "INVENTED" }];
        Assert.Equal(409, (await f.Service.ApplyAsync(request)).StatusCode);
        request.Entries = [new() { Module = "HR", Kind = "Designation", Code = "OFFICE-STAFF" }];
        Assert.Equal(400, (await f.Service.ApplyAsync(request)).StatusCode);
        request.Entries =
        [
            new() { Module = "ACADEMIC", Kind = "Medium", Code = "ENGLISH" },
            new() { Module = "ACADEMIC", Kind = "Medium", Code = "ENGLISH" }
        ];
        Assert.Equal(400, (await f.Service.ApplyAsync(request)).StatusCode);
        Assert.Empty(await f.Db.Mediums.ToListAsync());
        Assert.Empty(await f.Db.Designations.ToListAsync());
    }

    [Fact]
    public async Task Row_selection_rechecks_conflicts_without_rejecting_unselected_legacy_conflicts()
    {
        await using var f = await Open(modules: ["ACADEMIC"]);
        f.Db.Mediums.Add(new EduOS.Core.Entities.Academic.Medium
        {
            TenantId = f.Tenant, Code = "BANGLA", Name = "Legacy Bangla", IsActive = true
        });
        await f.Db.SaveChangesAsync();
        var request = Request(f, "ACADEMIC");
        request.Entries = [new() { Module = "ACADEMIC", Kind = "Medium", Code = "ENGLISH" }];
        Assert.True((await f.Service.ApplyAsync(request)).Success);
        Assert.Equal(2, await f.Db.Mediums.CountAsync());
        Assert.Equal("Legacy Bangla", (await f.Db.Mediums.SingleAsync(x => x.Code == "BANGLA")).Name);
    }

    [Fact]
    public async Task Row_scoped_receipt_cannot_replay_different_checked_entries()
    {
        await using var f = await Open(modules: ["ACADEMIC"]);
        var request = Request(f, "ACADEMIC");
        request.Entries = [new() { Module = "ACADEMIC", Kind = "Medium", Code = "ENGLISH" }];
        Assert.True((await f.Service.ApplyAsync(request)).Success);
        request.Entries = [new() { Module = "ACADEMIC", Kind = "Medium", Code = "BANGLA" }];
        Assert.Equal(409, (await f.Service.ApplyAsync(request)).StatusCode);
        Assert.Single(await f.Db.Mediums.ToListAsync());
    }

}
