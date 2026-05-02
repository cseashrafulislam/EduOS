using Hangfire.Dashboard;

namespace EduOS.App.Filters
{
    /// <summary>
    /// Restricts /hangfire dashboard access to SuperAdmin users only.
    /// </summary>
    public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context)
        {
            var httpContext = context.GetHttpContext();

            // Must be authenticated
            if (httpContext.User?.Identity?.IsAuthenticated != true)
                return false;

            // Must be SuperAdmin
            return httpContext.User.IsInRole("SuperAdmin");
        }
    }
}
