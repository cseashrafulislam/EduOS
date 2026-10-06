using EduOS.Core.Entities.SaaS;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Seed;

public static class SubscriptionSeeder
{
    private sealed record FeatureSeed(string Code, string Name, string Description);
    private sealed record PlanSeed(
        string Code, string Name, decimal MonthlyPrice, decimal YearlyPrice, int TrialDays,
        int MaxStudents, int MaxEmployees, int MaxUsers, int MaxCampuses, int MaxStorageMb, bool IsPublic = true);

    public static async Task SeedAsync(EduOSDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        await SeedFeaturesAsync(context);
        await SeedPlansAsync(context);
        await SeedPlanFeaturesAsync(context);
    }

    private static async Task SeedFeaturesAsync(EduOSDbContext context)
    {
        var definitions = new[]
        {
            new FeatureSeed("STUDENT_MGMT", "Student Management", "Student profiles, guardians and lifecycle."),
            new FeatureSeed("CLASS_SECTION", "Academic Structure", "Programs, levels, tracks and batches."),
            new FeatureSeed("SUBJECT_MGMT", "Subject Management", "Curriculum, subjects and offerings."),
            new FeatureSeed("CLASS_ROUTINE", "Routine & Lesson Planning", "Routine, substitution and lesson plans."),
            new FeatureSeed("ATTENDANCE", "Attendance", "Student and employee attendance."),
            new FeatureSeed("EXAM_MGMT", "Assessment Management", "Assessments, schedules and publication."),
            new FeatureSeed("MARK_ENTRY", "Mark Entry", "Secure assessment mark entry."),
            new FeatureSeed("RESULT_REPORT", "Results & Transcripts", "Results, transcripts and certificates."),
            new FeatureSeed("ONLINE_EXAM", "Online Assessment", "LMS quizzes and online assessments."),
            new FeatureSeed("FEE_COLLECTION", "Fee Collection", "Student payments and allocations."),
            new FeatureSeed("INVOICE_GEN", "Invoice Generation", "Fee invoicing and receivables."),
            new FeatureSeed("DISCOUNT_SCHOLARSHIP", "Discount & Scholarship", "Discount and scholarship rules."),
            new FeatureSeed("ACCOUNTING", "Accounting", "Double-entry accounting and journals."),
            new FeatureSeed("EMPLOYEE_MGMT", "Employee Management", "Employee profile and assignments."),
            new FeatureSeed("PAYROLL", "Payroll", "Payroll, loans and salary disbursement."),
            new FeatureSeed("LEAVE_MGMT", "Leave Management", "Leave entitlement and approval."),
            new FeatureSeed("SMS_NOTIFY", "SMS Notifications", "SMS communication."),
            new FeatureSeed("EMAIL_NOTIFY", "Email Notifications", "Email communication."),
            new FeatureSeed("NOTICE_BOARD", "Notice Board", "Institution notices."),
            new FeatureSeed("PARENT_PORTAL", "Guardian Portal", "Guardian self-service access."),
            new FeatureSeed("LIBRARY", "Library", "Library circulation."),
            new FeatureSeed("TRANSPORT", "Transport", "Routes, vehicles and assignments."),
            new FeatureSeed("HOSTEL", "Hostel", "Hostel room and bed allocation."),
            new FeatureSeed("INVENTORY", "Inventory", "Inventory and asset operations."),
            new FeatureSeed("MULTI_CAMPUS", "Multi-campus", "Multiple campus operations."),
            new FeatureSeed("MOBILE_APP", "Mobile App", "Mobile application access."),
            new FeatureSeed("API_ACCESS", "API Access", "API and integration access."),
            new FeatureSeed("CUSTOM_DOMAIN", "Custom Domain", "Custom tenant domain."),
            new FeatureSeed("PRIORITY_SUPPORT", "Priority Support", "Priority support entitlement.")
        };

        var existing = await context.Features.ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            if (existing.TryGetValue(definition.Code, out var row))
            {
                if (string.IsNullOrWhiteSpace(row.Name)) row.Name = definition.Name;
                if (string.IsNullOrWhiteSpace(row.Description)) row.Description = definition.Description;
                continue;
            }

            await context.Features.AddAsync(new Feature
            {
                Code = definition.Code,
                Name = definition.Name,
                Description = definition.Description,
                IsActive = true
            });
        }
        await context.SaveChangesAsync();
    }

    private static async Task SeedPlansAsync(EduOSDbContext context)
    {
        var definitions = new[]
        {
            new PlanSeed("TRIAL", "Free Trial", 0m, 0m, 14, 50, 10, 3, 1, 256),
            new PlanSeed("BASIC", "Basic", 1500m, 14000m, 0, 300, 40, 10, 1, 2048),
            new PlanSeed("PRO", "Pro", 4000m, 40000m, 0, 1500, 150, 30, 3, 10240),
            new PlanSeed("ENTERPRISE", "Enterprise", 10000m, 100000m, 0, 10000, 1000, 200, 20, 102400)
        };

        var existing = await context.SubscriptionPlans.ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            if (existing.ContainsKey(definition.Code)) continue;
            await context.SubscriptionPlans.AddAsync(new SubscriptionPlan
            {
                Code = definition.Code,
                Name = definition.Name,
                MonthlyPrice = definition.MonthlyPrice,
                YearlyPrice = definition.YearlyPrice,
                CurrencyCode = "BDT",
                TrialDays = definition.TrialDays,
                MaxStudents = definition.MaxStudents,
                MaxEmployees = definition.MaxEmployees,
                MaxUsers = definition.MaxUsers,
                MaxCampuses = definition.MaxCampuses,
                MaxStorageMb = definition.MaxStorageMb,
                IsPublic = definition.IsPublic,
                IsActive = true
            });
        }
        await context.SaveChangesAsync();
    }

    private static async Task SeedPlanFeaturesAsync(EduOSDbContext context)
    {
        var plans = await context.SubscriptionPlans.AsNoTracking().ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var features = await context.Features.AsNoTracking().ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var existing = (await context.PlanFeatures.AsNoTracking()
            .Select(x => new { x.SubscriptionPlanId, x.FeatureId }).ToListAsync())
            .Select(x => $"{x.SubscriptionPlanId}:{x.FeatureId}")
            .ToHashSet(StringComparer.Ordinal);

        var trial = new[]
        {
            "STUDENT_MGMT","CLASS_SECTION","SUBJECT_MGMT","ATTENDANCE","EXAM_MGMT",
            "MARK_ENTRY","FEE_COLLECTION","INVOICE_GEN","NOTICE_BOARD"
        };
        var basic = trial.Concat(new[]
        {
            "CLASS_ROUTINE","RESULT_REPORT","DISCOUNT_SCHOLARSHIP","EMPLOYEE_MGMT",
            "LEAVE_MGMT","SMS_NOTIFY","EMAIL_NOTIFY","PARENT_PORTAL"
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var pro = basic.Concat(new[]
        {
            "ONLINE_EXAM","ACCOUNTING","PAYROLL","LIBRARY","TRANSPORT","HOSTEL",
            "INVENTORY","MULTI_CAMPUS","MOBILE_APP"
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var map = new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["TRIAL"] = trial,
            ["BASIC"] = basic,
            ["PRO"] = pro,
            ["ENTERPRISE"] = features.Keys
        };

        var additions = new List<PlanFeature>();
        foreach (var pair in map)
        {
            if (!plans.TryGetValue(pair.Key, out var plan)) continue;
            foreach (var code in pair.Value.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!features.TryGetValue(code, out var feature)) continue;
                var key = $"{plan.Id}:{feature.Id}";
                if (!existing.Add(key)) continue;
                additions.Add(new PlanFeature
                {
                    SubscriptionPlanId = plan.Id,
                    FeatureId = feature.Id,
                    IsEnabled = true
                });
            }
        }

        if (additions.Count == 0) return;
        await context.PlanFeatures.AddRangeAsync(additions);
        await context.SaveChangesAsync();
    }
}
