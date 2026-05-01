//using EduOS.Core.Entities.SaaS;
//using EduOS.Persistence.Context;
//using Microsoft.EntityFrameworkCore;

//namespace EduOS.Persistence.Seed
//{
//    public static class SubscriptionPlanSeeder
//    {
//        public static async Task SeedAsync(EduOSDbContext context)
//        {
//            if (await context.SubscriptionPlans.AnyAsync())
//                return;

//            context.SubscriptionPlans.AddRange(
//                new SubscriptionPlan
//                {
//                    Name = "Starter",
//                    Code = "STARTER",
//                    BillingType = "Fixed",
//                    FixedAmount = 1500,
//                    PerActiveStudentAmount = 0,
//                    IsTrialAvailable = true,
//                    TrialDays = 7,
//                    IsActive = true
//                },
//                new SubscriptionPlan
//                {
//                    Name = "Pro",
//                    Code = "PRO",
//                    BillingType = "PerStudent",
//                    FixedAmount = 0,
//                    PerActiveStudentAmount = 10,
//                    IsTrialAvailable = true,
//                    TrialDays = 7,
//                    IsActive = true
//                },
//                new SubscriptionPlan
//                {
//                    Name = "Enterprise",
//                    Code = "ENTERPRISE",
//                    BillingType = "Hybrid",
//                    FixedAmount = 3000,
//                    PerActiveStudentAmount = 5,
//                    IsTrialAvailable = false,
//                    TrialDays = 0,
//                    IsActive = true
//                }
//            );

//            await context.SaveChangesAsync();
//        }
//    }
//}