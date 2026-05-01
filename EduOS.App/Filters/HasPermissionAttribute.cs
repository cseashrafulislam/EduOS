using EduOS.Core.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EduOS.App.Filters
{
    /// <summary>
    /// Custom authorization attribute to check user permissions.
    /// Usage: [HasPermission(PermissionConstants.Student.View)]
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
    public class HasPermissionAttribute : Attribute, IAsyncAuthorizationFilter
    {
        private readonly string[] _permissions;
        private readonly bool _requireAll;

        /// <param name="permission">Single permission required</param>
        public HasPermissionAttribute(string permission)
        {
            _permissions = new[] { permission };
            _requireAll = false;
        }

        /// <param name="requireAll">If true, user must have ALL permissions. If false, ANY permission is enough.</param>
        /// <param name="permissions">Multiple permissions</param>
        public HasPermissionAttribute(bool requireAll, params string[] permissions)
        {
            _permissions = permissions;
            _requireAll = requireAll;
        }

        public Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var currentUser = context.HttpContext.RequestServices
                .GetService(typeof(ICurrentUserService)) as ICurrentUserService;

            if (currentUser == null || !currentUser.IsAuthenticated)
            {
                context.Result = new UnauthorizedObjectResult(new
                {
                    success = false,
                    message = "Authentication required",
                    statusCode = 401
                });
                return Task.CompletedTask;
            }

            // SuperAdmin bypass
            if (currentUser.IsSuperAdmin)
                return Task.CompletedTask;

            bool hasAccess = _requireAll
                ? currentUser.HasAllPermissions(_permissions)
                : currentUser.HasAnyPermission(_permissions);

            if (!hasAccess)
            {
                context.Result = new ObjectResult(new
                {
                    success = false,
                    message = "You don't have permission to perform this action",
                    requiredPermissions = _permissions,
                    statusCode = 403
                })
                {
                    StatusCode = 403
                };
            }

            return Task.CompletedTask;
        }
    }
}