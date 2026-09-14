using EduOS.Core.Common;
using EduOS.Core.DTOs.Portals;
using EduOS.Core.Entities.Employees;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Portals;

public sealed class EmployeeSelfServiceService : IEmployeeSelfServiceService
{
    private readonly IGenericRepository<Employee> _employees;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<EmployeeSelfServiceService> _logger;

    public EmployeeSelfServiceService(IGenericRepository<Employee> employees, ICurrentUserService currentUser, ILogger<EmployeeSelfServiceService> logger)
    {
        _employees = employees;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<ApiResponse<EmployeePortalProfileDto>> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUsePortal()) return ApiResponse<EmployeePortalProfileDto>.ErrorResponse("Employee self-service is not available for this account.", 403);
        try
        {
            var profile = await _employees.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == _currentUser.TenantId && x.UserId == _currentUser.UserId && x.IsActive)
                .Select(x => new EmployeePortalProfileDto
                {
                    Reference = x.PublicId,
                    EmployeeCode = x.EmployeeCode,
                    FullName = x.FullName,
                    Phone = x.Phone,
                    Email = x.Email,
                    DesignationId = x.DesignationId,
                    DepartmentId = x.DepartmentId,
                    JoiningDate = x.JoiningDate,
                    Salary = x.Salary,
                    PhotoUrl = x.PhotoUrl,
                    Qualification = x.Qualification,
                    Experience = x.Experience,
                    IsTeacher = x.IsTeacher
                })
                .SingleOrDefaultAsync(cancellationToken);

            return profile == null
                ? ApiResponse<EmployeePortalProfileDto>.ErrorResponse("No active employee profile is linked to this account.", 404)
                : ApiResponse<EmployeePortalProfileDto>.SuccessResponse(profile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Employee self-service profile failed for user {UserId}", _currentUser.UserId);
            return ApiResponse<EmployeePortalProfileDto>.ErrorResponse("Employee profile could not be loaded.", 500);
        }
    }

    private bool CanUsePortal() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0 && (_currentUser.IsInRole("Teacher") || _currentUser.IsInRole("Staff"));
}
