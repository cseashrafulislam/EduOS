using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Finance;

public sealed partial class FinanceAdjustmentService
{
    /// <summary>
    /// Tenant-wide administrative fine register. Totals are not receivables or recognized income.
    /// Preserves historical enrollment/student/rule identities, including inactive records.
    /// </summary>
    public async Task<ApiResponse<StudentFineRegisterDto>> GetFineRegisterAsync(
        StudentFineRegisterQueryDto request, CancellationToken ct = default)
    {
        if (!Authorized) return Error<StudentFineRegisterDto>("Institution administrator permission required.", 403);
        if (request == null || request.CampusId is <= 0 ||
            request.Page is < 1 or > 10000 || request.PageSize is < 1 or > 100 ||
            request.From.HasValue && request.From.Value == default ||
            request.To.HasValue && request.To.Value == default ||
            request.From.HasValue && request.To.HasValue && request.From > request.To)
            return Error<StudentFineRegisterDto>("Invalid campus, date range or pagination.");

        var tenant = user.TenantId;
        var query = from fine in db.StudentFines.AsNoTracking()
            join enrollment in db.StudentEnrollments.AsNoTracking()
                on new { fine.TenantId, Id = fine.StudentEnrollmentId }
                equals new { enrollment.TenantId, enrollment.Id }
            join student in db.Students.AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.StudentId }
                equals new { student.TenantId, student.Id }
            join rule in db.FineRules.AsNoTracking()
                on new { fine.TenantId, Id = fine.FineRuleId }
                equals new { rule.TenantId, rule.Id }
            where fine.TenantId == tenant && !fine.IsDeleted
            select new { fine, enrollment, student, rule };
        if (request.CampusId.HasValue)
            query = query.Where(x => x.enrollment.CampusId == request.CampusId.Value);
        if (request.From.HasValue)
            query = query.Where(x => x.fine.FineDate >= request.From.Value);
        if (request.To.HasValue)
            query = query.Where(x => x.fine.FineDate <= request.To.Value);

        // The summary covers the selected campus/date period regardless of the status filter.
        var summary = await query.GroupBy(x => 1).Select(g => new {
            Recorded = g.Sum(x => x.fine.Amount),
            Waived = g.Sum(x => x.fine.IsWaived ? x.fine.Amount : 0m),
            Open = g.Sum(x => !x.fine.IsWaived ? x.fine.Amount : 0m)
        }).SingleOrDefaultAsync(ct);

        if (request.Waived.HasValue)
            query = query.Where(x => x.fine.IsWaived == request.Waived.Value);
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.fine.FineDate)
            .ThenByDescending(x => x.fine.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(x => new {
                x.fine.Id, x.enrollment.PublicId, x.student.StudentCode, x.student.FullName,
                x.enrollment.CampusId, RuleName = x.rule.Name, x.fine.FineDate,
                x.fine.Amount, x.fine.Reason, x.fine.IsWaived, x.fine.WaivedByUserId,
                x.fine.RowVersion
            }).ToListAsync(ct);
        return ApiResponse<StudentFineRegisterDto>.SuccessResponse(new StudentFineRegisterDto {
            Fines = new PagedResult<StudentFineRegisterRowDto> {
                Page = request.Page, PageSize = request.PageSize, TotalCount = count,
                Items = rows.Select(x => new StudentFineRegisterRowDto {
                    Id = x.Id, EnrollmentReference = x.PublicId, StudentCode = x.StudentCode,
                    StudentName = x.FullName, CampusId = x.CampusId, RuleName = x.RuleName,
                    FineDate = x.FineDate, Amount = x.Amount, Reason = x.Reason,
                    IsWaived = x.IsWaived, WaivedByUserId = x.WaivedByUserId,
                    RowVersion = Convert.ToBase64String(x.RowVersion)
                }).ToList()
            },
            RecordedTotal = summary?.Recorded ?? 0m,
            WaivedTotal = summary?.Waived ?? 0m,
            OutstandingAdministrativeTotal = summary?.Open ?? 0m
        }, "Administrative fine register only; amounts are not posted to invoices or the ledger.");
    }
}
