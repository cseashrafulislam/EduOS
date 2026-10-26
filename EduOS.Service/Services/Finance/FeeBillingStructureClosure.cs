using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EduOS.Service.Services.Finance;

public sealed partial class FeeBillingService
{
    // Closes future billing without changing any previously issued invoice, fee line or journal.
    // The existing invoice schema has no FeeStructureId; therefore a matching later invoice
    // conservatively blocks closure rather than guessing which structure produced it.
    public async Task<ApiResponse<FeeStructureDto>> CloseFeeStructureAsync(
        long id, CloseFeeStructureRequestDto request, CancellationToken ct = default)
    {
        if (!_user.IsAuthenticated || !_user.IsTenantAdmin || _user.TenantId <= 0)
            return Error<FeeStructureDto>("Tenant administrator access required.", 403);
        if (id <= 0 || request == null || request.EffectiveTo == default ||
            request.EffectiveTo.Day != DateTime.DaysInMonth(request.EffectiveTo.Year, request.EffectiveTo.Month))
            return Error<FeeStructureDto>("Select an inclusive calendar month-end date.", 400);
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var tenant = _user.TenantId;
                var row = await _structures.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == id && !x.IsDeleted, token);
                if (row == null) return Error<FeeStructureDto>("Fee structure not found.", 404);
                // A repeated successful request is a read-only replay; no audit or version change.
                if (row.EffectiveTo == request.EffectiveTo)
                    return ApiResponse<FeeStructureDto>.SuccessResponse(
                        await MapStructureAsync(row, token), "Structure already ends on this date.");
                if (!Match(row.RowVersion, request.RowVersion))
                    return Error<FeeStructureDto>("Fee structure changed; reload.", 409);
                var now = _clock.GetUtcNow().UtcDateTime;
                var thisMonthEnd = new DateOnly(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month));
                if (!row.IsActive || request.EffectiveTo < thisMonthEnd ||
                    request.EffectiveTo < row.EffectiveFrom ||
                    (row.EffectiveTo.HasValue && request.EffectiveTo > row.EffectiveTo.Value))
                    return Error<FeeStructureDto>(
                        "Only active structures may be shortened to a current or future month end.", 409);

                // The billing writer updates the same tenant rowversion. Concurrent invoice
                // generation and period closure therefore cannot both commit from a stale view.
                var tenantGate = await _tenants.GetQueryable().FirstOrDefaultAsync(x =>
                    x.Id == tenant && !x.IsDeleted, token);
                if (tenantGate == null) return Error<FeeStructureDto>("Institution not found.", 409);
                var campusId = row.CampusId;
                var yearId = row.AcademicYearId;
                var batchId = row.AcademicBatchId;
                var levelId = row.AcademicLevelId;
                var programId = row.AcademicProgramId;
                var cutoff = request.EffectiveTo;
                var laterInvoice = await (from invoice in _invoices.GetQueryable().AsNoTracking()
                    join enrollment in _enrollments.GetQueryable().AsNoTracking()
                        on new { invoice.TenantId, Id = invoice.StudentEnrollmentId }
                        equals new { enrollment.TenantId, enrollment.Id }
                    where invoice.TenantId == tenant && !invoice.IsDeleted &&
                        !enrollment.IsDeleted && invoice.State != InvoiceState.Cancelled &&
                        invoice.InvoiceDate > cutoff && enrollment.CampusId == campusId &&
                        enrollment.AcademicYearId == yearId &&
                        (!batchId.HasValue || enrollment.AcademicBatchId == batchId.Value) &&
                        (!levelId.HasValue || enrollment.AcademicLevelId == levelId.Value) &&
                        (!programId.HasValue || enrollment.AcademicProgramId == programId.Value)
                    select invoice.Id).AnyAsync(token);
                if (laterInvoice)
                    return Error<FeeStructureDto>(
                        "A matching enrollment has an issued invoice after this date. Reconcile billing before closing.", 409);
                row.EffectiveTo = cutoff;
                row.UpdatedAt = now;
                row.UpdatedBy = _user.UserId;
                _structures.Update(row);
                tenantGate.UpdatedAt = now;
                tenantGate.UpdatedBy = _user.UserId;
                _tenants.Update(tenantGate);
                await _uow.SaveChangesAsync(token);
                return ApiResponse<FeeStructureDto>.SuccessResponse(
                    await MapStructureAsync(row, token),
                    "Future fee billing closed at the selected month end; issued invoices are unchanged.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error<FeeStructureDto>("Fee structure or concurrent billing changed; reload.", 409);
        }
        catch (DbUpdateException)
        {
            return Error<FeeStructureDto>("Fee closure conflicts with a concurrent transaction.", 409);
        }
    }
}
