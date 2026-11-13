using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Tenants;

/// <summary>
/// On-demand, read-only prerequisites across canonical module tables. Does not create
/// data or claim operational approval. Every query is tenant scoped and projected to EXISTS.
/// </summary>
public sealed class SmartSetupReadinessService(
    EduOSDbContext db, ITenantModuleService moduleService, ICurrentUserService current,
    IConfiguration configuration, ILogger<SmartSetupReadinessService> logger)
{
    private static readonly HashSet<string> PilotExcluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "LMS", "LIBRARY", "HOSTEL", "TRANSPORT", "INVENTORY",
        "FACILITIES", "ALUMNI", "RESEARCH", "ACCREDITATION"
    };

    public async Task<ApiResponse<List<SmartSetupReadinessDto>>> GetAsync(
        string? sectionCode, CancellationToken ct = default)
    {
        if (!current.IsAuthenticated || !current.IsTenantAdmin || current.TenantId <= 0 ||
            current.TenantId != db.CurrentTenantId)
            return ApiResponse<List<SmartSetupReadinessDto>>.ErrorResponse("Tenant administrator permission required.", 403);
        var sections = string.IsNullOrWhiteSpace(sectionCode)
            ? SmartSetupCatalog.Sections
            : SmartSetupCatalog.Sections.Where(x =>
                string.Equals(x.Code, sectionCode.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (sections.Length == 0)
            return ApiResponse<List<SmartSetupReadinessDto>>.ErrorResponse("Unknown setup module.", 400);
        try
        {
            var tenant = current.TenantId;
            var institution = await db.Tenants.AsNoTracking()
                .Where(x => x.Id == tenant && !x.IsDeleted)
                .Select(x => new { x.InstitutionTypeDefinitionId })
                .SingleOrDefaultAsync(ct);
            if (institution == null)
                return ApiResponse<List<SmartSetupReadinessDto>>.ErrorResponse("Institution not found.", 404);
            var result = new List<SmartSetupReadinessDto>(sections.Length);
            foreach (var section in sections)
            {
                var accessCode = SmartSetupCatalog.AccessModule(section.Code);
                var available = !configuration.GetValue<bool>("PilotRelease:Enabled") ||
                    !PilotExcluded.Contains(accessCode);
                available = available && await moduleService.IsCurrentTenantModuleAvailableAsync(accessCode);
                var row = new SmartSetupReadinessDto
                {
                    ModuleCode = section.Code, ModuleName = section.Name,
                    ModuleNameBangla = section.Bangla, NextStepUrl = section.Url,
                    IsAvailable = available
                };
                if (institution.InstitutionTypeDefinitionId == null)
                    Add(row, "INSTITUTION_TYPE", "Choose institution type", "প্রতিষ্ঠানের ধরন নির্বাচন",
                        "/InstitutionManagement/Profile", false);
                if (available)
                    await LoadRequirementsAsync(row, tenant, ct);
                row.HasChecklist = row.Requirements.Count > 0;
                row.PrerequisitesConfigured = available && row.Requirements.All(x => x.Configured);
                result.Add(row);
            }
            return ApiResponse<List<SmartSetupReadinessDto>>.SuccessResponse(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Smart setup readiness lookup failed for tenant {TenantId}", current.TenantId);
            return ApiResponse<List<SmartSetupReadinessDto>>.ErrorResponse("Setup readiness could not be loaded.", 500);
        }
    }

    private static void Add(SmartSetupReadinessDto dto, string code,
        string name, string bangla, string url, bool configured)
    {
        dto.Requirements.Add(new SmartSetupRequirementDto
        {
            Code = code, Label = name, LabelBangla = bangla,
            ActionUrl = url, Configured = configured
        });
    }

    private async Task LoadRequirementsAsync(SmartSetupReadinessDto row, long tenant, CancellationToken ct)
    {
        switch (row.ModuleCode)
        {
            case "ACADEMIC":
                Add(row, "CAMPUS", "Campus", "ক্যাম্পাস", "/InstitutionManagement/Campuses",
                    await db.Campuses.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.IsActive, ct));
                Add(row, "ACADEMIC_YEAR", "Academic year and dates", "শিক্ষাবর্ষ ও তারিখ",
                    "/AcademicManagement/Years",
                    await db.AcademicYears.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.IsActive, ct));
                Add(row, "ACADEMIC_PROGRAM", "Programme / grade framework", "প্রোগ্রাম / শ্রেণির কাঠামো",
                    "/AcademicManagement/Classes",
                    await db.AcademicPrograms.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.IsActive, ct));
                Add(row, "ACADEMIC_LEVEL", "Classes / levels", "শ্রেণি / লেভেল",
                    "/AcademicManagement/Classes",
                    await db.AcademicLevels.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.IsActive, ct));
                Add(row, "CURRICULUM", "Reviewed curriculum", "যাচাইকৃত পাঠ্যক্রম",
                    "/AcademicManagement/Curricula",
                    await db.AcademicCurriculums.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.IsActive, ct));
                Add(row, "SECTION", "Academic Section", "একাডেমিক শাখা",
                    "/AcademicManagement/Sections",
                    await db.AcademicSections.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.IsActive, ct));
                break;
            case "STUDENT":
                Add(row, "STUDENT_SECTION", "Academic section for enrollment", "ভর্তির জন্য শ্রেণি / শাখা",
                    "/AcademicManagement/Sections",
                    await db.AcademicSections.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.IsActive, ct));
                Add(row, "STUDENT_CURRICULUM", "Enrollment curriculum", "ভর্তির পাঠ্যক্রম",
                    "/AcademicManagement/Curricula",
                    await db.AcademicCurriculums.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.IsActive, ct));
                break;
            case "ADMISSION":
                Add(row, "ADMISSION_FORM", "Published intake form", "প্রকাশিত ভর্তি ফর্ম",
                    "/Admissions/Forms",
                    await db.AdmissionIntakeForms.AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                        x.State == AdmissionFormState.Published, ct));
                break;
            case "ATTENDANCE":
                Add(row, "ATTENDANCE_SECTION", "Active academic section", "সক্রিয় শাখা",
                    "/AcademicManagement/Sections",
                    await db.AcademicSections.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.IsActive, ct));
                break;
            case "ASSESSMENT":
                Add(row, "GRADE_SCHEME", "Grade scheme", "গ্রেড কাঠামো",
                    "/AssessmentAdministration/GradeSchemes",
                    await db.GradeSchemes.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.IsActive, ct));
                Add(row, "GRADE_RULES", "Approved grade boundaries", "গ্রেড সীমা / নম্বর",
                    "/AssessmentAdministration/GradeSchemes",
                    await db.GradeRules.AsNoTracking().AnyAsync(x => x.TenantId == tenant, ct));
                Add(row, "SUBJECT_OFFERING", "Subject offerings", "বিষয় বরাদ্দ",
                    "/AcademicManagement/Offerings",
                    await db.SubjectOfferings.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.IsActive, ct));
                break;
            case "FEES":
                Add(row, "FEE_HEADS", "Fee heads (no amounts)", "ফি-এর ধরন (টাকার পরিমাণ ছাড়া)",
                    "/FeeOperations/Index",
                    await db.FeeHeads.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.IsActive, ct));
                Add(row, "FEE_STRUCTURE", "Confirmed fee structure and amounts", "নিশ্চিত ফি কাঠামো ও পরিমাণ",
                    "/FeeOperations/Index",
                    await db.FeeStructures.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.IsActive, ct));
                break;
            case "FINANCE":
                Add(row, "CHART_OF_ACCOUNTS", "Chart of accounts", "হিসাবের তালিকা",
                    "/AccountingSetup/Index",
                    await db.Accounts.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.IsActive, ct));
                Add(row, "ACCOUNTING_PERIOD", "Open accounting period", "খোলা হিসাবকাল",
                    "/AccountingSetup/Index",
                    await db.AccountingPeriods.AsNoTracking().AnyAsync(x => x.TenantId == tenant && !x.IsClosed, ct));
                break;
            case "HR":
                Add(row, "HR_DEPARTMENT", "Organization units", "বিভাগ",
                    "/HrSetup/Index",
                    await db.OrganizationUnits.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.IsActive, ct));
                Add(row, "HR_DESIGNATION", "Designations", "পদবি",
                    "/HrSetup/Index",
                    await db.Designations.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.IsActive, ct));
                break;
            case "PAYROLL":
                Add(row, "SALARY_COMPONENT", "Salary component types", "বেতনের উপাদান",
                    "/PayrollSalaryComponents/Index",
                    await db.SalaryComponents.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.IsActive, ct));
                Add(row, "SALARY_STRUCTURE", "Reviewed employee salary structures", "অনুমোদিত বেতন কাঠামো",
                    "/PayrollSalaryStructures/Index",
                    await db.SalaryStructures.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.IsCurrent, ct));
                break;
            case "INVENTORY":
                Add(row, "INVENTORY_LOCATION", "Stock locations", "স্টক লোকেশন",
                    "/InventoryCatalog/Index",
                    await db.InventoryLocations.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.IsActive, ct));
                Add(row, "ASSET_CATEGORY", "Asset categories", "সম্পদের ধরন",
                    "/InventoryCatalog/Index",
                    await db.AssetCategories.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.IsActive, ct));
                break;
            case "LIBRARY":
                Add(row, "BOOK_CATEGORIES", "Book categories", "বইয়ের ধরন",
                    "/Library/Index",
                    await db.BookCategories.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.IsActive, ct));
                Add(row, "LIBRARY_BRANCH", "Campus library branch", "ক্যাম্পাস লাইব্রেরি",
                    "/LibraryBranches/Index",
                    await db.LibraryBranches.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.IsActive, ct));
                break;
            case "HOSTEL":
                Add(row, "HOSTEL_BUILDING", "Hostel / residence", "হোস্টেল / আবাসন",
                    "/HostelOperations/Setup",
                    await db.Hostels.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.IsActive, ct));
                Add(row, "HOSTEL_ROOM", "Rooms and capacity", "কক্ষ ও ধারণক্ষমতা",
                    "/HostelOperations/Setup",
                    await db.HostelRooms.AsNoTracking().AnyAsync(x => x.TenantId == tenant, ct));
                Add(row, "HOSTEL_BED", "Bed inventory", "বেডের তালিকা",
                    "/HostelOperations/Setup",
                    await db.HostelBeds.AsNoTracking().AnyAsync(x => x.TenantId == tenant, ct));
                break;
            case "TRANSPORT":
                Add(row, "ROUTE", "Transport routes", "পরিবহন রুট",
                    "/TransportOperations/Setup",
                    await db.Routes.AsNoTracking().AnyAsync(x => x.TenantId == tenant, ct));
                Add(row, "ROUTE_STOP", "Route stops", "রুটের স্টপেজ",
                    "/TransportOperations/Setup",
                    await db.RouteStops.AsNoTracking().AnyAsync(x => x.TenantId == tenant, ct));
                Add(row, "VEHICLE", "Vehicle registry", "যানবাহনের তালিকা",
                    "/TransportOperations/Setup",
                    await db.Vehicles.AsNoTracking().AnyAsync(x => x.TenantId == tenant, ct));
                break;
            case "LMS":
                Add(row, "LMS_COURSE", "Course catalog (real content)", "কোর্স তালিকা (প্রকৃত কনটেন্ট)",
                    "/Learning/Index",
                    await db.Courses.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.IsActive, ct));
                break;
            case "COMMUNICATION":
                Add(row, "NOTICE_CATEGORY", "Notice categories", "নোটিশের ধরন",
                    "/CommunicationAdministration/Index",
                    await db.NoticeCategories.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.IsActive, ct));
                break;
            case "DOCUMENTS":
                Add(row, "DOCUMENT_TYPES", "Document type catalog", "নথির ধরন",
                    "/DocumentAdministration/Index",
                    await db.DocumentTypeDefinitions.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.IsActive, ct));
                break;
            case "FACILITIES":
                Add(row, "FACILITY_SPACE", "Verified facility spaces", "যাচাইকৃত স্থান / কক্ষ",
                    "/FacilitySpaces/Index",
                    await db.FacilitySpaces.AsNoTracking().AnyAsync(x => x.TenantId == tenant, ct));
                break;
            // Reports aggregate authoritative existing data. No artificial master record.
            case "REPORTS":
                break;
        }
    }
}
