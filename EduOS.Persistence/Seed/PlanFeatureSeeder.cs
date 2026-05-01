//using EduOS.Core.Entities.SaaS;
//using EduOS.Persistence.Context;
//using Microsoft.EntityFrameworkCore;

//namespace EduOS.Persistence.Seed
//{
//    public static class PlanFeatureSeeder
//    {
//        public static async Task SeedAsync(EduOSDbContext context)
//        {
//            if (await context.PlanFeatures.AnyAsync())
//                return;

//            var starterPlan = await context.SubscriptionPlans
//                .FirstOrDefaultAsync(x => x.Code == "STARTER_TRIAL");

//            if (starterPlan == null)
//                return;

//            var features = await context.Features.ToListAsync();

//            if (!features.Any())
//                return;

//            var planFeatures = features.Select(f => new PlanFeature
//            {
//                SubscriptionPlanId = (int)starterPlan.Id,
//                FeatureId = (int)f.Id,
//                IsEnabled = true
//            }).ToList();

//            await context.PlanFeatures.AddRangeAsync(planFeatures);
//            await context.SaveChangesAsync();
//        }
//    }
//}