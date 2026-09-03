using EduOS.BackgroundJobs.Jobs;
using EduOS.App.Authorization;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Core.Interfaces.Jobs;
using EduOS.Core.Settings;
using EduOS.Service.Helpers;
using EduOS.Service.Helpers.Payment;
using EduOS.Service.Helpers.Storage;
using EduOS.Service.Mappings;
using EduOS.Service.Services.Auth;
using EduOS.Service.Services.SaaS;
using EduOS.Service.Services.Students;
using EduOS.Service.Services.Tenants;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authorization;

namespace EduOS.App.Extensions
{
    /// <summary>
    /// Registers all application services (Service layer).
    /// </summary>
    public static class ApplicationServiceExtensions
    {
        public static IServiceCollection AddApplicationServices(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // ==================== Settings (bind from appsettings.json) ====================
            services.Configure<AamarPaySettings>(configuration.GetSection(AamarPaySettings.SectionName));
            services.Configure<ManualPaymentSettings>(configuration.GetSection(ManualPaymentSettings.SectionName));
            services.Configure<FileUploadSettings>(configuration.GetSection(FileUploadSettings.SectionName));
            services.Configure<FileStorageSettings>(configuration.GetSection("FileStorage"));
            services.Configure<TenantPortalSettings>(configuration.GetSection(TenantPortalSettings.SectionName));
            services.Configure<LearnerIdentitySettings>(configuration.GetSection(LearnerIdentitySettings.SectionName));
            services.Configure<MfaSettings>(configuration.GetSection(MfaSettings.SectionName));

            // ==================== AutoMapper ====================
            services.AddAutoMapper(_ => { }, typeof(MappingProfile).Assembly);

            // ==================== Core Helpers ====================
            services.AddScoped<ICurrentUserService, CurrentUserService>();
            services.AddScoped<IFileUploadService, FileUploadService>();
            services.AddScoped<ILearnerIdentifierProtector, LearnerIdentifierProtector>();
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IMfaChallengeService, MfaChallengeService>();

            // ==================== HTTP Clients ====================
            services.AddHttpClient<IAamarPayClient, AamarPayClient>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            });

            // ==================== Subscription / Payment Services (Phase B) ====================
            services.AddScoped<ISubscriptionPlanService, SubscriptionPlanService>();
            services.AddScoped<ISubscriptionService, SubscriptionService>();
            services.AddScoped<ISubscriptionInvoiceService, SubscriptionInvoiceService>();
            services.AddScoped<ISubscriptionPaymentService, SubscriptionPaymentService>();
            services.AddScoped<IPlatformCatalogService, PlatformCatalogService>();
            services.AddScoped<ITenantModuleService, TenantModuleService>();
            services.AddSingleton<IAuthorizationPolicyProvider, ModuleAuthorizationPolicyProvider>();
            services.AddScoped<IAuthorizationHandler, ModuleAccessHandler>();

            // ==================== Tenant Management Services (Phase C) ====================
            services.AddScoped<ITenantProfileService, TenantProfileService>();
            services.AddScoped<ITenantSettingService, TenantSettingService>();
            services.AddScoped<IOnboardingService, OnboardingService>();

            // ==================== TODO: Add other services as you build them ====================
             services.AddScoped<IInstitutionOnboardingService, InstitutionOnboardingService>();
             services.AddScoped<IDashboardService, DashboardService>();
             services.AddScoped<IEmailService, EmailService>();
             services.AddScoped<IEmailJob, EmailJob>();
             services.AddScoped<ILearnerIdentityService, LearnerIdentityService>();


            return services;
        }
    }
}
