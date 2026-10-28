using EduOS.Core.Common;
using EduOS.Core.DTOs.Payroll;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.HR;

/// <summary>Tenant-authorized, historical payroll snapshot report; no current salary-master joins.</summary>
public sealed class PayrollSalaryStatementService : IPayrollSalaryStatementService
{
    private readonly EduOSDbContext _db;
    private readonly ICurrentUserService _user;
    public PayrollSalaryStatementService(EduOSDbContext db, ICurrentUserService user)
    { _db = db; _user = user; }

    public async Task<ApiResponse<PagedResult<PayrollSalaryStatementRowDto>>> GetAsync(
        int? year, int? month, long? campusId, int page, int pageSize, CancellationToken ct = default)
    {
        if (!_user.IsAuthenticated || !_user.IsTenantAdmin || _user.TenantId <= 0 ||
            _db.CurrentTenantId != _user.TenantId)
            return ApiResponse<PagedResult<PayrollSalaryStatementRowDto>>
                .ErrorResponse("Tenant administrator payroll-report permission required.", 403);
        if (year is < 2000 or > 2200 || month is < 1 or > 12 || campusId is <= 0 ||
            page < 1 || pageSize is < 1 or > 1000 ||
            (pageSize > 50 && (!year.HasValue || !month.HasValue)) ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return ApiResponse<PagedResult<PayrollSalaryStatementRowDto>>
                .ErrorResponse("Invalid report period or pagination.");
        var tenant = _user.TenantId;
        var query = from person in _db.PayrollEmployees.AsNoTracking()
                    join run in _db.PayrollRuns.AsNoTracking()
                        on new { person.TenantId, Id = person.PayrollRunId }
                        equals new { run.TenantId, run.Id }
                    where person.TenantId == tenant && !person.IsDeleted && !run.IsDeleted &&
                          run.State != PayrollRunState.Cancelled &&
                          run.State != PayrollRunState.Draft
                    select new { person, run };
        if (year.HasValue) query = query.Where(x => x.run.Year == year.Value);
        if (month.HasValue) query = query.Where(x => x.run.Month == month.Value);
        if (campusId.HasValue) query = query.Where(x => x.run.CampusId == campusId.Value);
        var count = await query.CountAsync(ct);
        // CSV remains bounded and explicit. Large exports require a separate authorized batch workflow.
        if (pageSize > 50 && count > 1000)
            return ApiResponse<PagedResult<PayrollSalaryStatementRowDto>>
                .ErrorResponse("More than 1,000 salary rows. Narrow the report before exporting.", 409);
        var rows = await query.OrderByDescending(x => x.run.PeriodEnd)
            .ThenBy(x => x.person.EmployeeCodeSnapshot).ThenBy(x => x.person.Id)
            .Skip((int)((long)(page - 1) * pageSize)).Take(pageSize)
            .Select(x => new PayrollSalaryStatementRowDto
            {
                RunReference = x.run.PublicId, RunNumber = x.run.RunNumber,
                PeriodStart = x.run.PeriodStart, PeriodEnd = x.run.PeriodEnd,
                State = x.run.State, CampusId = x.run.CampusId,
                EmployeeCode = x.person.EmployeeCodeSnapshot ?? string.Empty,
                EmployeeName = x.person.EmployeeNameSnapshot ?? string.Empty,
                GrossAmount = x.person.GrossAmount, DeductionAmount = x.person.DeductionAmount,
                NetAmount = x.person.NetAmount
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<PayrollSalaryStatementRowDto>>.SuccessResponse(
            new PagedResult<PayrollSalaryStatementRowDto>
            { Items = rows, Page = page, PageSize = pageSize, TotalCount = count });
    }
}
