using EduOS.Core.Entities.SaaS;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Seed
{
    /// <summary>
    /// Seeds default subscription plans and features.
    /// Run once on first deployment.
    /// </summary>
    public static class SubscriptionSeeder
    {
        public static async Task SeedAsync(EduOSDbContext context)
        {
            await SeedFeaturesAsync(context);
            await SeedPlansAsync(context);
            await SeedPlanFeaturesAsync(context);
        }

        // ============================================================
        // FEATURES
        // ============================================================
        private static async Task SeedFeaturesAsync(EduOSDbContext context)
        {
            var features = new List<Feature>
            {
                // Academic
                new() { Name = "Student Management", NameBangla = "শিক্ষার্থী ব্যবস্থাপনা", Code = "STUDENT_MGMT", Category = "Academic", DisplayOrder = 1, IsActive = true, IsPubliclyVisible = true },
                new() { Name = "Class & Section", NameBangla = "শ্রেণি ও শাখা", Code = "CLASS_SECTION", Category = "Academic", DisplayOrder = 2, IsActive = true, IsPubliclyVisible = true },
                new() { Name = "Subject Management", NameBangla = "বিষয় ব্যবস্থাপনা", Code = "SUBJECT_MGMT", Category = "Academic", DisplayOrder = 3, IsActive = true, IsPubliclyVisible = true },
                new() { Name = "Class Routine", NameBangla = "ক্লাস রুটিন", Code = "CLASS_ROUTINE", Category = "Academic", DisplayOrder = 4, IsActive = true, IsPubliclyVisible = true },
                new() { Name = "Attendance", NameBangla = "উপস্থিতি", Code = "ATTENDANCE", Category = "Academic", DisplayOrder = 5, IsActive = true, IsPubliclyVisible = true },

                // Exam
                new() { Name = "Exam Management", NameBangla = "পরীক্ষা ব্যবস্থাপনা", Code = "EXAM_MGMT", Category = "Exam", DisplayOrder = 10, IsActive = true, IsPubliclyVisible = true },
                new() { Name = "Mark Entry", NameBangla = "নম্বর এন্ট্রি", Code = "MARK_ENTRY", Category = "Exam", DisplayOrder = 11, IsActive = true, IsPubliclyVisible = true },
                new() { Name = "Result & Report Card", NameBangla = "ফলাফল ও রিপোর্ট কার্ড", Code = "RESULT_REPORT", Category = "Exam", DisplayOrder = 12, IsActive = true, IsPubliclyVisible = true },
                new() { Name = "Online Exam", NameBangla = "অনলাইন পরীক্ষা", Code = "ONLINE_EXAM", Category = "Exam", DisplayOrder = 13, IsActive = true, IsPubliclyVisible = true },

                // Finance
                new() { Name = "Fee Collection", NameBangla = "ফি আদায়", Code = "FEE_COLLECTION", Category = "Finance", DisplayOrder = 20, IsActive = true, IsPubliclyVisible = true },
                new() { Name = "Invoice Generation", NameBangla = "ইনভয়েস তৈরি", Code = "INVOICE_GEN", Category = "Finance", DisplayOrder = 21, IsActive = true, IsPubliclyVisible = true },
                new() { Name = "Discount & Scholarship", NameBangla = "ছাড় ও বৃত্তি", Code = "DISCOUNT_SCHOLARSHIP", Category = "Finance", DisplayOrder = 22, IsActive = true, IsPubliclyVisible = true },
                new() { Name = "Accounting", NameBangla = "হিসাবরক্ষণ", Code = "ACCOUNTING", Category = "Finance", DisplayOrder = 23, IsActive = true, IsPubliclyVisible = true },

                // HR
                new() { Name = "Employee Management", NameBangla = "কর্মী ব্যবস্থাপনা", Code = "EMPLOYEE_MGMT", Category = "HR", DisplayOrder = 30, IsActive = true, IsPubliclyVisible = true },
                new() { Name = "Payroll", NameBangla = "বেতন ব্যবস্থাপনা", Code = "PAYROLL", Category = "HR", DisplayOrder = 31, IsActive = true, IsPubliclyVisible = true },
                new() { Name = "Leave Management", NameBangla = "ছুটি ব্যবস্থাপনা", Code = "LEAVE_MGMT", Category = "HR", DisplayOrder = 32, IsActive = true, IsPubliclyVisible = true },

                // Communication
                new() { Name = "SMS Notifications", NameBangla = "SMS বিজ্ঞপ্তি", Code = "SMS_NOTIFY", Category = "Communication", DisplayOrder = 40, IsActive = true, IsPubliclyVisible = true },
                new() { Name = "Email Notifications", NameBangla = "ইমেইল বিজ্ঞপ্তি", Code = "EMAIL_NOTIFY", Category = "Communication", DisplayOrder = 41, IsActive = true, IsPubliclyVisible = true },
                new() { Name = "Notice Board", NameBangla = "নোটিশ বোর্ড", Code = "NOTICE_BOARD", Category = "Communication", DisplayOrder = 42, IsActive = true, IsPubliclyVisible = true },
                new() { Name = "Parent Portal", NameBangla = "অভিভাবক পোর্টাল", Code = "PARENT_PORTAL", Category = "Communication", DisplayOrder = 43, IsActive = true, IsPubliclyVisible = true },

                // Operations
                new() { Name = "Library", NameBangla = "লাইব্রেরি", Code = "LIBRARY", Category = "Operations", DisplayOrder = 50, IsActive = true, IsPubliclyVisible = true },
                new() { Name = "Transport", NameBangla = "পরিবহন", Code = "TRANSPORT", Category = "Operations", DisplayOrder = 51, IsActive = true, IsPubliclyVisible = true },
                new() { Name = "Hostel", NameBangla = "হোস্টেল", Code = "HOSTEL", Category = "Operations", DisplayOrder = 52, IsActive = true, IsPubliclyVisible = true },
                new() { Name = "Inventory", NameBangla = "ইনভেন্টরি", Code = "INVENTORY", Category = "Operations", DisplayOrder = 53, IsActive = true, IsPubliclyVisible = true },

                // Advanced
                new() { Name = "Multi-Campus", NameBangla = "একাধিক ক্যাম্পাস", Code = "MULTI_CAMPUS", Category = "Advanced", DisplayOrder = 60, IsActive = true, IsPubliclyVisible = true },
                new() { Name = "Mobile App", NameBangla = "মোবাইল অ্যাপ", Code = "MOBILE_APP", Category = "Advanced", DisplayOrder = 61, IsActive = true, IsPubliclyVisible = true },
                new() { Name = "API Access", NameBangla = "API অ্যাক্সেস", Code = "API_ACCESS", Category = "Advanced", DisplayOrder = 62, IsActive = true, IsPubliclyVisible = true },
                new() { Name = "Custom Domain", NameBangla = "কাস্টম ডোমেইন", Code = "CUSTOM_DOMAIN", Category = "Advanced", DisplayOrder = 63, IsActive = true, IsPubliclyVisible = true },
                new() { Name = "Priority Support", NameBangla = "অগ্রাধিকার সহায়তা", Code = "PRIORITY_SUPPORT", Category = "Advanced", DisplayOrder = 64, IsActive = true, IsPubliclyVisible = true },
            };

            var existingFeatures = await context.Features
                .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase);
            foreach (var definition in features)
            {
                if (!existingFeatures.TryGetValue(definition.Code, out var existing))
                {
                    await context.Features.AddAsync(definition);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(existing.NameBangla))
                    existing.NameBangla = definition.NameBangla;
                if (string.IsNullOrWhiteSpace(existing.DescriptionBangla))
                    existing.DescriptionBangla = definition.DescriptionBangla;
            }
            await context.SaveChangesAsync();
        }

        // ============================================================
        // PLANS
        // ============================================================
        private static async Task SeedPlansAsync(EduOSDbContext context)
        {
            var plans = new List<SubscriptionPlan>
            {
                new()
                {
                    Name = "Free Trial",
                    NameBangla = "ফ্রি ট্রায়াল",
                    Code = "TRIAL",
                    Description = "14-day free trial with limited features. No credit card required.",
                    DescriptionBangla = "সীমিত ফিচারসহ ১৪ দিনের ফ্রি ট্রায়াল। কোনো কার্ড প্রয়োজন নেই।",
                    ShortDescription = "Try EduOS free for 14 days",
                    ShortDescriptionBangla = "EduOS ১৪ দিন বিনা মূল্যে ব্যবহার করুন",
                    DisplayOrder = 1,
                    IsRecommended = false,
                    IsFreeTrial = true,
                    TrialDays = 14,
                    MonthlyPrice = 0,
                    QuarterlyPrice = 0,
                    HalfYearlyPrice = 0,
                    YearlyPrice = 0,
                    SetupFee = 0,
                    Currency = "BDT",
                    MaxStudents = 50,
                    MaxTeachers = 10,
                    MaxCampuses = 1,
                    MaxAdminUsers = 2,
                    MaxStorageMb = 256,
                    MaxSmsPerMonth = 50,
                    MaxEmailsPerMonth = 100,
                    IsActive = true,
                    IsPubliclyVisible = true,
                },
                new()
                {
                    Name = "Basic",
                    NameBangla = "বেসিক",
                    Code = "BASIC",
                    Description = "Best for small schools and coaching centers. All essential features.",
                    DescriptionBangla = "ছোট স্কুল ও কোচিং সেন্টারের প্রয়োজনীয় সব ফিচার।",
                    ShortDescription = "Perfect for small schools",
                    ShortDescriptionBangla = "ছোট প্রতিষ্ঠানের জন্য উপযোগী",
                    DisplayOrder = 2,
                    IsRecommended = false,
                    IsFreeTrial = false,
                    MonthlyPrice = 1500,
                    QuarterlyPrice = 4000,   // ~10% off
                    HalfYearlyPrice = 7500,  // ~15% off
                    YearlyPrice = 14000,     // ~22% off
                    SetupFee = 0,
                    Currency = "BDT",
                    MaxStudents = 300,
                    MaxTeachers = 30,
                    MaxCampuses = 1,
                    MaxAdminUsers = 5,
                    MaxStorageMb = 2048,
                    MaxSmsPerMonth = 1000,
                    MaxEmailsPerMonth = 5000,
                    IsActive = true,
                    IsPubliclyVisible = true,
                },
                new()
                {
                    Name = "Pro",
                    NameBangla = "প্রো",
                    Code = "PRO",
                    Description = "For growing institutions. Advanced features + multi-campus support.",
                    DescriptionBangla = "বর্ধমান প্রতিষ্ঠানের জন্য উন্নত ফিচার ও একাধিক ক্যাম্পাস সুবিধা।",
                    ShortDescription = "Advanced features for medium institutions",
                    ShortDescriptionBangla = "মাঝারি প্রতিষ্ঠানের জন্য উন্নত ফিচার",
                    DisplayOrder = 3,
                    IsRecommended = true,
                    IsFreeTrial = false,
                    MonthlyPrice = 4000,
                    QuarterlyPrice = 11000,
                    HalfYearlyPrice = 21000,
                    YearlyPrice = 40000,
                    SetupFee = 0,
                    Currency = "BDT",
                    MaxStudents = 1500,
                    MaxTeachers = 100,
                    MaxCampuses = 3,
                    MaxAdminUsers = 15,
                    MaxStorageMb = 10240,
                    MaxSmsPerMonth = 5000,
                    MaxEmailsPerMonth = 20000,
                    IsActive = true,
                    IsPubliclyVisible = true,
                },
                new()
                {
                    Name = "Enterprise",
                    NameBangla = "এন্টারপ্রাইজ",
                    Code = "ENTERPRISE",
                    Description = "For large universities and multi-branch institutes. Unlimited everything.",
                    DescriptionBangla = "বড় বিশ্ববিদ্যালয় ও বহুশাখা প্রতিষ্ঠানের জন্য সর্বোচ্চ সক্ষমতা।",
                    ShortDescription = "Unlimited - for large institutions",
                    ShortDescriptionBangla = "বড় প্রতিষ্ঠানের জন্য সর্বোচ্চ সীমা",
                    DisplayOrder = 4,
                    IsRecommended = false,
                    IsFreeTrial = false,
                    MonthlyPrice = 10000,
                    QuarterlyPrice = 28000,
                    HalfYearlyPrice = 54000,
                    YearlyPrice = 100000,
                    SetupFee = 5000,
                    Currency = "BDT",
                    MaxStudents = 99999,
                    MaxTeachers = 9999,
                    MaxCampuses = 99,
                    MaxAdminUsers = 99,
                    MaxStorageMb = 102400,
                    MaxSmsPerMonth = 50000,
                    MaxEmailsPerMonth = 200000,
                    IsActive = true,
                    IsPubliclyVisible = true,
                },
            };

            var existingPlans = await context.SubscriptionPlans
                .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase);
            foreach (var definition in plans)
            {
                if (!existingPlans.TryGetValue(definition.Code, out var existing))
                {
                    await context.SubscriptionPlans.AddAsync(definition);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(existing.NameBangla))
                    existing.NameBangla = definition.NameBangla;
                if (string.IsNullOrWhiteSpace(existing.DescriptionBangla))
                    existing.DescriptionBangla = definition.DescriptionBangla;
                if (string.IsNullOrWhiteSpace(existing.ShortDescriptionBangla))
                    existing.ShortDescriptionBangla = definition.ShortDescriptionBangla;
            }
            await context.SaveChangesAsync();
        }

        // ============================================================
        // PLAN <-> FEATURES MAPPING
        // ============================================================
        private static async Task SeedPlanFeaturesAsync(EduOSDbContext context)
        {
            if (await context.PlanFeatures.AnyAsync()) return;

            var allFeatures = await context.Features.ToListAsync();
            var allPlans = await context.SubscriptionPlans.ToListAsync();

            var planFeatures = new List<PlanFeature>();

            // Trial: Basic features only
            var trialCodes = new[]
            {
                "STUDENT_MGMT", "CLASS_SECTION", "SUBJECT_MGMT", "ATTENDANCE",
                "EXAM_MGMT", "MARK_ENTRY", "FEE_COLLECTION", "INVOICE_GEN", "NOTICE_BOARD"
            };
            AddPlanFeatures(planFeatures, allPlans, allFeatures, "TRIAL", trialCodes);

            // Basic: Trial + a few more
            var basicCodes = trialCodes.Concat(new[]
            {
                "CLASS_ROUTINE", "RESULT_REPORT", "DISCOUNT_SCHOLARSHIP",
                "EMPLOYEE_MGMT", "LEAVE_MGMT", "SMS_NOTIFY", "EMAIL_NOTIFY", "PARENT_PORTAL"
            }).ToArray();
            AddPlanFeatures(planFeatures, allPlans, allFeatures, "BASIC", basicCodes);

            // Pro: Basic + advanced features
            var proCodes = basicCodes.Concat(new[]
            {
                "ONLINE_EXAM", "ACCOUNTING", "PAYROLL", "LIBRARY", "TRANSPORT",
                "HOSTEL", "INVENTORY", "MULTI_CAMPUS", "MOBILE_APP"
            }).ToArray();
            AddPlanFeatures(planFeatures, allPlans, allFeatures, "PRO", proCodes);

            // Enterprise: Everything
            var enterpriseCodes = allFeatures.Select(f => f.Code).ToArray();
            AddPlanFeatures(planFeatures, allPlans, allFeatures, "ENTERPRISE", enterpriseCodes);

            await context.PlanFeatures.AddRangeAsync(planFeatures);
            await context.SaveChangesAsync();
        }

        private static void AddPlanFeatures(
            List<PlanFeature> list,
            List<SubscriptionPlan> allPlans,
            List<Feature> allFeatures,
            string planCode,
            string[] featureCodes)
        {
            var plan = allPlans.FirstOrDefault(p => p.Code == planCode);
            if (plan == null) return;

            foreach (var code in featureCodes.Distinct())
            {
                var feature = allFeatures.FirstOrDefault(f => f.Code == code);
                if (feature == null) continue;

                list.Add(new PlanFeature
                {
                    SubscriptionPlanId = plan.Id,
                    FeatureId = feature.Id,
                    IsEnabled = true,
                });
            }
        }
    }
}
