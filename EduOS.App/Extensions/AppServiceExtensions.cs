using EduOS.App.Services;
using EduOS.Core.Common;

namespace EduOS.App.Extensions
{
    public static class AppServiceExtensions
    {
        /// <summary>
        /// Register App layer services (CurrentUserService, etc.)
        /// </summary>
        public static IServiceCollection AddAppServices(this IServiceCollection services)
        {
            services.AddHttpContextAccessor();
            services.AddScoped<ICurrentUserService, CurrentUserService>();

            return services;
        }
    }
}