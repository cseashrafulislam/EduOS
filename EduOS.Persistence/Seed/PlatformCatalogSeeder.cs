using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Seed;

public static class PlatformCatalogSeeder
{
    private static readonly HashSet<string> RequiredModuleCodes =
        new(StringComparer.OrdinalIgnoreCase) { "CORE_ADMIN", "STUDENT", "ACADEMIC" };

    public static async Task SeedAsync(EduOSDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        await SeedInstitutionTypesAsync(context);
        await SeedModulesAsync(context);
        await SeedPresetModulesAsync(context);
        await SeedModuleFeaturesAsync(context);
    }

    private static async Task SeedInstitutionTypesAsync(EduOSDbContext context)
    {
        var definitions = new[]
        {
            Institution("PRE_PRIMARY", "Pre-primary School", "প্রাক-প্রাথমিক বিদ্যালয়", AcademicCycleType.Annual, 10),
            Institution("PRIMARY_SCHOOL", "Primary School", "প্রাথমিক বিদ্যালয়", AcademicCycleType.Annual, 20),
            Institution("SECONDARY_SCHOOL", "Secondary School", "মাধ্যমিক বিদ্যালয়", AcademicCycleType.Annual, 30),
            Institution("SCHOOL_COLLEGE", "School and College", "স্কুল ও কলেজ", AcademicCycleType.Annual, 40),
            Institution("COLLEGE", "College", "কলেজ", AcademicCycleType.Annual, 50),
            Institution("UNIVERSITY", "University", "বিশ্ববিদ্যালয়", AcademicCycleType.Semester, 60),
            Institution("MADRASA", "Madrasa", "মাদ্রাসা", AcademicCycleType.Annual, 70),
            Institution("POLYTECHNIC", "Polytechnic Institute", "পলিটেকনিক ইনস্টিটিউট", AcademicCycleType.Semester, 80),
            Institution("COACHING_CENTER", "Coaching Center", "কোচিং সেন্টার", AcademicCycleType.BatchBased, 90),
            Institution("TRAINING_INSTITUTE", "Training Institute", "প্রশিক্ষণ প্রতিষ্ঠান", AcademicCycleType.BatchBased, 100),
            Institution("PRIVATE_TUTOR", "Private Tutor", "প্রাইভেট টিউটর", AcademicCycleType.BatchBased, 110),
            Institution("LMS_PROVIDER", "Online Learning Provider", "অনলাইন শিক্ষা প্রদানকারী", AcademicCycleType.Modular, 120),
            Institution("HYBRID_INSTITUTE", "Hybrid Education Institute", "হাইব্রিড শিক্ষা প্রতিষ্ঠান", AcademicCycleType.Modular, 130)
        };

        var existing = await context.InstitutionTypeDefinitions.ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase);
        foreach (var row in definitions)
            if (!existing.ContainsKey(row.Code)) await context.InstitutionTypeDefinitions.AddAsync(row);
        await context.SaveChangesAsync();
    }

    private static async Task SeedModulesAsync(EduOSDbContext context)
    {
        var definitions = new[]
        {
            Module("CORE_ADMIN","Administration",10,true),
            Module("ADMISSION","Admission",20),
            Module("STUDENT","Student Management",30,true),
            Module("ACADEMIC","Academic Management",40,true),
            Module("ATTENDANCE","Attendance",50),
            Module("EXAM","Assessment & Results",60),
            Module("FINANCE","Finance & Fees",70),
            Module("HR","Human Resources",80),
            Module("PAYROLL","Payroll",90),
            Module("LMS","Learning Management",100),
            Module("LIBRARY","Library",110),
            Module("TRANSPORT","Transport",120),
            Module("HOSTEL","Hostel",130),
            Module("INVENTORY","Inventory & Assets",140),
            Module("COMMUNICATION","Communication",150),
            Module("DOCUMENTS","Documents & Certificates",160),
            Module("REPORTING","Reporting & Analytics",170),
            Module("API_ACCESS","API & Integrations",180),
            Module("AI_INSIGHTS","AI Insights",190),
            Module("MULTI_CAMPUS","Multi-campus",200)
        };

        var existing = await context.ProductModules.ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase);
        foreach (var row in definitions)
            if (!existing.ContainsKey(row.Code)) await context.ProductModules.AddAsync(row);
        await context.SaveChangesAsync();
    }

    private static async Task SeedPresetModulesAsync(EduOSDbContext context)
    {
        var institutionTypes = await context.InstitutionTypeDefinitions.AsNoTracking()
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var modules = await context.ProductModules.AsNoTracking()
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var existing = (await context.InstitutionTypeModules.AsNoTracking()
            .Select(x => new { x.InstitutionTypeDefinitionId, x.ProductModuleId }).ToListAsync())
            .Select(x => $"{x.InstitutionTypeDefinitionId}:{x.ProductModuleId}")
            .ToHashSet(StringComparer.Ordinal);

        var common = new[] { "CORE_ADMIN","STUDENT","ACADEMIC","COMMUNICATION","DOCUMENTS","REPORTING" };
        var school = common.Concat(new[] { "ADMISSION","ATTENDANCE","EXAM","FINANCE","HR","PAYROLL","LMS","LIBRARY","TRANSPORT" });
        var advanced = school.Concat(new[] { "HOSTEL","INVENTORY","API_ACCESS","AI_INSIGHTS","MULTI_CAMPUS" });
        var presets = new Dictionary<string,IEnumerable<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["PRE_PRIMARY"] = school.Except(new[] { "LMS" }),
            ["PRIMARY_SCHOOL"] = school,
            ["SECONDARY_SCHOOL"] = school.Concat(new[] { "INVENTORY" }),
            ["SCHOOL_COLLEGE"] = advanced,
            ["COLLEGE"] = advanced,
            ["UNIVERSITY"] = advanced,
            ["MADRASA"] = school.Concat(new[] { "HOSTEL","INVENTORY" }),
            ["POLYTECHNIC"] = advanced,
            ["COACHING_CENTER"] = common.Concat(new[] { "ADMISSION","ATTENDANCE","EXAM","FINANCE","HR","PAYROLL","LMS","AI_INSIGHTS" }),
            ["TRAINING_INSTITUTE"] = common.Concat(new[] { "ADMISSION","ATTENDANCE","EXAM","FINANCE","HR","PAYROLL","LMS","API_ACCESS","AI_INSIGHTS" }),
            ["PRIVATE_TUTOR"] = common.Concat(new[] { "ATTENDANCE","EXAM","FINANCE","LMS" }),
            ["LMS_PROVIDER"] = common.Concat(new[] { "ADMISSION","EXAM","FINANCE","LMS","API_ACCESS","AI_INSIGHTS" }),
            ["HYBRID_INSTITUTE"] = advanced.Except(new[] { "HOSTEL" })
        };

        var additions = new List<InstitutionTypeModule>();
        foreach (var preset in presets)
        {
            if (!institutionTypes.TryGetValue(preset.Key, out var institutionType)) continue;
            foreach (var code in preset.Value.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!modules.TryGetValue(code, out var module)) continue;
                var key = $"{institutionType.Id}:{module.Id}";
                if (!existing.Add(key)) continue;
                additions.Add(new InstitutionTypeModule
                {
                    InstitutionTypeDefinitionId = institutionType.Id,
                    ProductModuleId = module.Id,
                    IsRequired = RequiredModuleCodes.Contains(code),
                    IsDefaultEnabled = true
                });
            }
        }

        if (additions.Count == 0) return;
        await context.InstitutionTypeModules.AddRangeAsync(additions);
        await context.SaveChangesAsync();
    }

    private static async Task SeedModuleFeaturesAsync(EduOSDbContext context)
    {
        var modules = await context.ProductModules.AsNoTracking().ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var features = await context.Features.AsNoTracking().ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var existing = (await context.ProductModuleFeatures.AsNoTracking()
            .Select(x => new { x.ProductModuleId, x.FeatureId }).ToListAsync())
            .Select(x => $"{x.ProductModuleId}:{x.FeatureId}")
            .ToHashSet(StringComparer.Ordinal);

        var map = new Dictionary<string,string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["ADMISSION"] = ["STUDENT_MGMT"],
            ["STUDENT"] = ["STUDENT_MGMT"],
            ["ACADEMIC"] = ["CLASS_SECTION","SUBJECT_MGMT","CLASS_ROUTINE"],
            ["ATTENDANCE"] = ["ATTENDANCE"],
            ["EXAM"] = ["EXAM_MGMT","MARK_ENTRY","RESULT_REPORT","ONLINE_EXAM"],
            ["FINANCE"] = ["FEE_COLLECTION","INVOICE_GEN","DISCOUNT_SCHOLARSHIP","ACCOUNTING"],
            ["HR"] = ["EMPLOYEE_MGMT","LEAVE_MGMT"],
            ["PAYROLL"] = ["PAYROLL"],
            ["LMS"] = ["ONLINE_EXAM"],
            ["LIBRARY"] = ["LIBRARY"],
            ["TRANSPORT"] = ["TRANSPORT"],
            ["HOSTEL"] = ["HOSTEL"],
            ["INVENTORY"] = ["INVENTORY"],
            ["COMMUNICATION"] = ["SMS_NOTIFY","EMAIL_NOTIFY","NOTICE_BOARD","PARENT_PORTAL"],
            ["DOCUMENTS"] = ["RESULT_REPORT"],
            ["REPORTING"] = ["RESULT_REPORT","ACCOUNTING"],
            ["API_ACCESS"] = ["API_ACCESS"],
            ["MULTI_CAMPUS"] = ["MULTI_CAMPUS"]
        };

        var additions = new List<ProductModuleFeature>();
        foreach (var pair in map)
        {
            if (!modules.TryGetValue(pair.Key, out var module)) continue;
            foreach (var code in pair.Value)
            {
                if (!features.TryGetValue(code, out var feature)) continue;
                var key = $"{module.Id}:{feature.Id}";
                if (!existing.Add(key)) continue;
                additions.Add(new ProductModuleFeature { ProductModuleId = module.Id, FeatureId = feature.Id });
            }
        }
        if (additions.Count == 0) return;
        await context.ProductModuleFeatures.AddRangeAsync(additions);
        await context.SaveChangesAsync();
    }

    private static InstitutionTypeDefinition Institution(
        string code, string name, string nameBangla, AcademicCycleType cycle, int order) =>
        new()
        {
            Code = code,
            Name = name,
            NameBangla = nameBangla,
            Description = $"EduOS preset for {name}.",
            DefaultAcademicCycle = cycle,
            IsActive = true,
            DisplayOrder = order
        };

    private static ProductModule Module(string code, string name, int order, bool isCore = false) =>
        new()
        {
            Code = code,
            Name = name,
            Description = $"Configure and operate {name}.",
            IsCore = isCore,
            IsActive = true,
            DisplayOrder = order
        };
}
