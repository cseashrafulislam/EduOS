//using EduOS.Core.DTOs.Dashboard;
//using EduOS.Core.Entities.SaaS;
//using EduOS.Core.Helpers;
//using EduOS.Core.Interfaces;
//using EduOS.Core.Interfaces.SaaS;

//namespace EduOS.Service.Services.SaaS
//{
//    public class DashboardService : IDashboardService
//    {
//        private readonly IUnitOfWork _unitOfWork;

//        public DashboardService(
//            IUnitOfWork unitOfWork)
//        {
//            _unitOfWork = unitOfWork;
//        }

//        public async Task<DashboardVm?> GetDashboardAsync()
//        {
//            var tenantId = await UserContext.RequireTenantIdIntAsync();
//            if (tenantId == null) return null;

//            var tenant = await _unitOfWork.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId);
//            if (tenant == null) return null;

//            var onboarding = await _unitOfWork.OnboardingProgresses
//                .FirstOrDefaultAsync(x => x.TenantId == tenant.Id);

//            var subscription = await _unitOfWork.TenantSubscriptions
//                .FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.IsActive);

//            var planName = "N/A";
//            if (subscription != null)
//            {
//                var plan = await _unitOfWork.SubscriptionPlans
//                    .FirstOrDefaultAsync(x => x.Id == subscription.SubscriptionPlanId);

//                if (plan != null)
//                    planName = plan.Name;
//            }

//            var featureCount = await _unitOfWork.TenantFeatures.CountAsync(x => x.TenantId == tenant.Id && x.IsEnabled);

//            var vm = new DashboardVm
//            {
//                InstitutionName = tenant.Name,
//                InstitutionType = tenant.InstitutionType,
//                OwnerName = tenant.OwnerName,
//                PlanName = planName,
//                EmailVerified = tenant.IsEmailVerified,
//                SetupCompleted = tenant.IsSetupCompleted,
//                TrialEndDate = subscription?.EndDate,
//                ActiveFeatures = featureCount,

//                TotalStudents = 0,
//                TotalTeachers = 0,
//                TotalStaff = 0,
//                MonthlyCollection = 0,

//                CurrentStep = onboarding?.CurrentStep ?? 1,
//                OnboardingPercent = CalculateOnboardingPercent(onboarding)
//            };

//            return vm;
//        }

//        private int CalculateOnboardingPercent(OnboardingProgress? onboarding)
//        {
//            if (onboarding == null) return 0;

//            int total = 5;
//            int done = 0;

//            if (onboarding.AccountCreated) done++;
//            if (onboarding.EmailVerified) done++;
//            if (onboarding.InstitutionProfileCompleted) done++;
//            if (onboarding.CampusSetupCompleted) done++;
//            if (onboarding.AcademicSetupCompleted) done++;
//           // if (onboarding.AdminUserSetupCompleted) done++;
//           // if (onboarding.RolePermissionSetupCompleted) done++;

//            return (int)Math.Round((double)done / total * 100);
//        }
//    }
//}