using System.Text.Json;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Academics;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Seed;

/// <summary>
/// Seeds the platform-owned institution presets and top-level module catalog.
/// Existing records are never overwritten, so platform administrators retain control.
/// </summary>
public static class PlatformCatalogSeeder
{
    private static readonly HashSet<string> RequiredModuleCodes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CORE_ADMIN", "STUDENT", "ACADEMIC"
        };

    public static async Task SeedAsync(EduOSDbContext context)
    {
        await SeedInstitutionTypesAsync(context);
        await SeedModulesAsync(context);
        await SeedPresetModulesAsync(context);
        await BackfillTenantInstitutionTypesAsync(context);
    }

    private static async Task SeedInstitutionTypesAsync(EduOSDbContext context)
    {
        var existingCodeList = await context.InstitutionTypeDefinitions
            .Select(x => x.Code)
            .ToListAsync();
        var existingCodes = existingCodeList.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var institutionTypes = new[]
        {
            Institution("PRE_PRIMARY", "Pre-primary School", "প্রাক-প্রাথমিক বিদ্যালয়", AcademicCycleType.Annual, 10, "class", "Class", "learner", "Student"),
            Institution("PRIMARY_SCHOOL", "Primary School", "প্রাথমিক বিদ্যালয়", AcademicCycleType.Annual, 20, "class", "Class", "learner", "Student"),
            Institution("SECONDARY_SCHOOL", "Secondary School", "মাধ্যমিক বিদ্যালয়", AcademicCycleType.Annual, 30, "class", "Class", "learner", "Student"),
            Institution("SCHOOL_COLLEGE", "School and College", "স্কুল ও কলেজ", AcademicCycleType.Annual, 40, "class", "Class", "learner", "Student"),
            Institution("COLLEGE", "College", "কলেজ", AcademicCycleType.Annual, 50, "class", "Class", "learner", "Student"),
            Institution("UNIVERSITY", "University", "বিশ্ববিদ্যালয়", AcademicCycleType.Semester, 60, "program", "Program", "learner", "Student"),
            Institution("MADRASA", "Madrasa", "মাদ্রাসা", AcademicCycleType.Annual, 70, "class", "Jamaat", "learner", "Student"),
            Institution("POLYTECHNIC", "Polytechnic Institute", "পলিটেকনিক ইনস্টিটিউট", AcademicCycleType.Semester, 80, "program", "Technology", "learner", "Student"),
            Institution("COACHING_CENTER", "Coaching Center", "কোচিং সেন্টার", AcademicCycleType.BatchBased, 90, "class", "Batch", "learner", "Student"),
            Institution("TRAINING_INSTITUTE", "Training Institute", "প্রশিক্ষণ প্রতিষ্ঠান", AcademicCycleType.BatchBased, 100, "program", "Course", "learner", "Trainee"),
            Institution("PRIVATE_TUTOR", "Private Tutor", "প্রাইভেট টিউটর", AcademicCycleType.BatchBased, 110, "class", "Batch", "learner", "Student"),
            Institution("LMS_PROVIDER", "Online Learning Provider", "অনলাইন শিক্ষা প্রদানকারী", AcademicCycleType.Modular, 120, "program", "Course", "learner", "Learner", "Online"),
            Institution("HYBRID_INSTITUTE", "Hybrid Education Institute", "হাইব্রিড শিক্ষা প্রতিষ্ঠান", AcademicCycleType.Modular, 130, "program", "Program", "learner", "Learner", "Hybrid")
        };

        var missing = institutionTypes.Where(x => !existingCodes.Contains(x.Code)).ToList();
        if (missing.Count == 0) return;

        await context.InstitutionTypeDefinitions.AddRangeAsync(missing);
        await context.SaveChangesAsync();
    }

    private static async Task SeedModulesAsync(EduOSDbContext context)
    {
        var existingCodeList = await context.ProductModules
            .Select(x => x.Code)
            .ToListAsync();
        var existingCodes = existingCodeList.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var modules = new[]
        {
            Module("CORE_ADMIN", "Administration", "প্রশাসন", "Core", "/admin", 10, true),
            Module("ADMISSION", "Admission", "ভর্তি", "Academic", "/admissions", 20),
            Module("STUDENT", "Student Management", "শিক্ষার্থী ব্যবস্থাপনা", "Academic", "/students", 30, true),
            Module("ACADEMIC", "Academic Management", "একাডেমিক ব্যবস্থাপনা", "Academic", "/academics", 40, true),
            Module("ATTENDANCE", "Attendance", "উপস্থিতি", "Academic", "/attendance", 50),
            Module("EXAM", "Exam and Assessment", "পরীক্ষা ও মূল্যায়ন", "Academic", "/exams", 60),
            Module("FINANCE", "Finance and Fees", "অর্থ ও ফি", "Operations", "/finance", 70),
            Module("HR", "Human Resources", "মানবসম্পদ", "Operations", "/hr", 80),
            Module("PAYROLL", "Payroll", "বেতন ব্যবস্থাপনা", "Operations", "/payroll", 90),
            Module("LMS", "Learning Management", "লার্নিং ম্যানেজমেন্ট", "Learning", "/lms", 100),
            Module("LIBRARY", "Library", "লাইব্রেরি", "Operations", "/library", 110),
            Module("TRANSPORT", "Transport", "পরিবহন", "Operations", "/transport", 120),
            Module("HOSTEL", "Hostel", "হোস্টেল", "Operations", "/hostel", 130),
            Module("INVENTORY", "Inventory", "ইনভেন্টরি", "Operations", "/inventory", 140),
            Module("COMMUNICATION", "Communication", "যোগাযোগ", "Engagement", "/communication", 150),
            Module("DOCUMENTS", "Documents and Certificates", "নথি ও সনদ", "Platform", "/documents", 160),
            Module("REPORTING", "Reporting and Analytics", "রিপোর্ট ও বিশ্লেষণ", "Platform", "/reports", 170),
            Module("API_ACCESS", "API and Integrations", "এপিআই ও ইন্টিগ্রেশন", "Platform", "/integrations", 180),
            Module("AI_INSIGHTS", "AI Insights", "এআই ইনসাইট", "Platform", "/insights", 190),
            Module("MULTI_CAMPUS", "Multi-campus", "মাল্টি-ক্যাম্পাস", "Platform", "/campuses", 200)
        };

        var missing = modules.Where(x => !existingCodes.Contains(x.Code)).ToList();
        if (missing.Count == 0) return;

        await context.ProductModules.AddRangeAsync(missing);
        await context.SaveChangesAsync();
    }

    private static async Task SeedPresetModulesAsync(EduOSDbContext context)
    {
        var institutionTypeList = await context.InstitutionTypeDefinitions.ToListAsync();
        var institutionTypes = institutionTypeList
            .ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var moduleList = await context.ProductModules.ToListAsync();
        var modules = moduleList.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var existingPairs = await context.InstitutionTypeModules
            .Select(x => new { x.InstitutionTypeDefinitionId, x.ProductModuleId })
            .ToListAsync();
        var existingKeys = existingPairs
            .Select(x => $"{x.InstitutionTypeDefinitionId}:{x.ProductModuleId}")
            .ToHashSet(StringComparer.Ordinal);

        var common = new[]
        {
            "CORE_ADMIN", "STUDENT", "ACADEMIC", "COMMUNICATION", "DOCUMENTS", "REPORTING"
        };
        var school = common.Concat(new[]
        {
            "ADMISSION", "ATTENDANCE", "EXAM", "FINANCE", "HR", "PAYROLL", "LMS", "LIBRARY", "TRANSPORT"
        });
        var advanced = school.Concat(new[]
        {
            "HOSTEL", "INVENTORY", "API_ACCESS", "AI_INSIGHTS", "MULTI_CAMPUS"
        });

        var presets = new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["PRE_PRIMARY"] = school.Except(new[] { "LMS" }),
            ["PRIMARY_SCHOOL"] = school,
            ["SECONDARY_SCHOOL"] = school.Concat(new[] { "INVENTORY" }),
            ["SCHOOL_COLLEGE"] = advanced,
            ["COLLEGE"] = advanced,
            ["UNIVERSITY"] = advanced,
            ["MADRASA"] = school.Concat(new[] { "HOSTEL", "INVENTORY" }),
            ["POLYTECHNIC"] = advanced,
            ["COACHING_CENTER"] = common.Concat(new[] { "ADMISSION", "ATTENDANCE", "EXAM", "FINANCE", "HR", "PAYROLL", "LMS", "AI_INSIGHTS" }),
            ["TRAINING_INSTITUTE"] = common.Concat(new[] { "ADMISSION", "ATTENDANCE", "EXAM", "FINANCE", "HR", "PAYROLL", "LMS", "API_ACCESS", "AI_INSIGHTS" }),
            ["PRIVATE_TUTOR"] = common.Concat(new[] { "ATTENDANCE", "EXAM", "FINANCE", "LMS" }),
            ["LMS_PROVIDER"] = common.Concat(new[] { "ADMISSION", "EXAM", "FINANCE", "LMS", "API_ACCESS", "AI_INSIGHTS" }),
            ["HYBRID_INSTITUTE"] = advanced.Except(new[] { "HOSTEL" })
        };

        var mappings = new List<InstitutionTypeModule>();
        foreach (var preset in presets)
        {
            if (!institutionTypes.TryGetValue(preset.Key, out var institutionType)) continue;

            var displayOrder = 0;
            foreach (var moduleCode in preset.Value.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!modules.TryGetValue(moduleCode, out var module)) continue;
                displayOrder += 10;

                var key = $"{institutionType.Id}:{module.Id}";
                if (existingKeys.Contains(key)) continue;

                mappings.Add(new InstitutionTypeModule
                {
                    InstitutionTypeDefinitionId = institutionType.Id,
                    ProductModuleId = module.Id,
                    IsRequired = RequiredModuleCodes.Contains(moduleCode),
                    IsEnabledByDefault = true,
                    DisplayOrder = displayOrder,
                    CreatedAt = DateTime.UtcNow
                });
                existingKeys.Add(key);
            }
        }

        if (mappings.Count == 0) return;
        await context.InstitutionTypeModules.AddRangeAsync(mappings);
        await context.SaveChangesAsync();
    }

    private static async Task BackfillTenantInstitutionTypesAsync(EduOSDbContext context)
    {
        var presetRows = await context.InstitutionTypeDefinitions
            .Select(x => new { x.Code, x.Id })
            .ToListAsync();
        var presets = presetRows.ToDictionary(
            x => x.Code,
            x => x.Id,
            StringComparer.OrdinalIgnoreCase);
        var tenants = await context.Tenants
            .Where(x => x.InstitutionTypeDefinitionId == null && x.InstitutionType != null)
            .ToListAsync();

        var changed = false;
        foreach (var tenant in tenants)
        {
            var code = tenant.InstitutionType!.Trim().ToUpperInvariant();
            if (!presets.TryGetValue(code, out var presetId)) continue;

            tenant.InstitutionType = code;
            tenant.InstitutionTypeDefinitionId = presetId;
            tenant.UpdatedAt = DateTime.UtcNow;
            changed = true;
        }

        if (changed) await context.SaveChangesAsync();
    }

    private static InstitutionTypeDefinition Institution(
        string code,
        string name,
        string nameBangla,
        AcademicCycleType cycle,
        int order,
        string structureKey,
        string structureLabel,
        string learnerKey,
        string learnerLabel,
        string deliveryMode = "OnCampus")
    {
        return new InstitutionTypeDefinition
        {
            Code = code,
            Name = name,
            NameBangla = nameBangla,
            Description = $"EduOS preset for {name.ToLowerInvariant()} operations.",
            AcademicCycleType = cycle,
            TerminologyJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                [structureKey] = structureLabel,
                [learnerKey] = learnerLabel,
                ["educator"] = "Teacher"
            }),
            DefaultSettingsJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["country"] = "Bangladesh",
                ["currency"] = "BDT",
                ["timeZone"] = "Asia/Dhaka",
                ["deliveryMode"] = deliveryMode
            }),
            DisplayOrder = order,
            IsActive = true,
            IsPubliclyVisible = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static ProductModule Module(
        string code,
        string name,
        string nameBangla,
        string category,
        string routePrefix,
        int order,
        bool isCore = false)
    {
        return new ProductModule
        {
            Code = code,
            Name = name,
            NameBangla = nameBangla,
            Category = category,
            Description = $"Configure and operate {name.ToLowerInvariant()} from one workspace.",
            IconName = code.ToLowerInvariant().Replace('_', '-'),
            RoutePrefix = routePrefix,
            DisplayOrder = order,
            IsCore = isCore,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }
}
