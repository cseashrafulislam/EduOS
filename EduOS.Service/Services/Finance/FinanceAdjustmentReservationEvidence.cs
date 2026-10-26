using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Finance;

public sealed partial class FinanceAdjustmentService
{
    /// <summary>
    /// Bounded, read-only source evidence for issues #45/#47/#48/#51. The existing
    /// preflight validates tenant, enrollment, campus and resource linkage first.
    /// No reservation-to-invoice attribution, approval or posting is inferred.
    /// </summary>
    public async Task<ApiResponse<ReservationFinanceEvidenceDto>> GetReservationFinanceEvidenceAsync(
        ReservationFinanceEvidenceQueryDto request, CancellationToken ct = default)
    {
        if (!Authorized)
            return Error<ReservationFinanceEvidenceDto>("Institution administrator permission required.", 403);
        if (request == null || request.ReservationId <= 0 ||
            string.IsNullOrWhiteSpace(request.Kind) || string.IsNullOrWhiteSpace(request.Source) ||
            request.Page is < 1 or > 10000 || request.PageSize is < 1 or > 100)
            return Error<ReservationFinanceEvidenceDto>("Invalid reservation, evidence source or pagination.");

        var source = request.Source.Trim().ToLowerInvariant();
        if (source is not ("invoice" or "payment"))
            return Error<ReservationFinanceEvidenceDto>("Evidence source must be invoice or payment.");

        var preflight = await GetReservationFinancePreflightAsync(request.Kind, request.ReservationId, ct);
        if (!preflight.Success || preflight.Data == null)
            return Error<ReservationFinanceEvidenceDto>(preflight.Message, preflight.StatusCode);

        var scope = preflight.Data;
        var tenant = user.TenantId;
        var offset = (request.Page - 1) * request.PageSize;
        int total;
        List<ReservationFinanceEvidenceRowDto> rows;

        if (source == "invoice")
        {
            var query = db.StudentInvoices.AsNoTracking().Where(x =>
                x.TenantId == tenant && !x.IsDeleted &&
                x.StudentEnrollmentId == scope.StudentEnrollmentId);
            total = await query.CountAsync(ct);
            var page = await query.OrderByDescending(x => x.InvoiceDate).ThenByDescending(x => x.Id)
                .Skip(offset).Take(request.PageSize)
                .Select(x => new
                {
                    x.Id, x.InvoiceNumber, x.InvoiceDate, x.State, x.TotalAmount,
                    x.PaidAmount, x.DueAmount, x.JournalId
                }).ToListAsync(ct);
            rows = page.Select(x => new ReservationFinanceEvidenceRowDto
            {
                Id = x.Id, Reference = x.InvoiceNumber, ActivityDate = x.InvoiceDate,
                State = x.State.ToString(), Amount = x.TotalAmount,
                PaidAmount = x.PaidAmount, DueAmount = x.DueAmount, JournalId = x.JournalId,
                HasPostedActivity = x.JournalId.HasValue || x.PaidAmount > 0m ||
                    x.State is InvoiceState.PartiallyPaid or InvoiceState.Paid or InvoiceState.Refunded,
                IsUnpostedIssuedInvoice = x.State == InvoiceState.Issued &&
                    !x.JournalId.HasValue && x.PaidAmount == 0m
            }).ToList();
        }
        else
        {
            var query = db.StudentPayments.AsNoTracking().Where(x =>
                x.TenantId == tenant && !x.IsDeleted && x.StudentId == scope.StudentId);
            total = await query.CountAsync(ct);
            var page = await query.OrderByDescending(x => x.PaymentDate).ThenByDescending(x => x.Id)
                .Skip(offset).Take(request.PageSize)
                .Select(x => new
                {
                    x.Id, x.ReceiptNumber, x.PaymentDate, x.State, x.Amount, x.JournalId
                }).ToListAsync(ct);
            rows = page.Select(x => new ReservationFinanceEvidenceRowDto
            {
                Id = x.Id, Reference = x.ReceiptNumber, ActivityDate = x.PaymentDate,
                State = x.State.ToString(), Amount = x.Amount, JournalId = x.JournalId,
                HasPostedActivity = x.JournalId.HasValue ||
                    x.State is PaymentState.Successful or PaymentState.Refunded
            }).ToList();
        }

        return ApiResponse<ReservationFinanceEvidenceDto>.SuccessResponse(new ReservationFinanceEvidenceDto
        {
            Kind = scope.Kind, ReservationId = scope.ReservationId,
            CampusId = scope.CampusId, StudentId = scope.StudentId,
            StudentEnrollmentId = scope.StudentEnrollmentId, Source = source,
            Evidence = new PagedResult<ReservationFinanceEvidenceRowDto>
            {
                Items = rows, TotalCount = total, Page = request.Page, PageSize = request.PageSize
            }
        }, "Read-only institutional Finance evidence; no reservation adjustment approved.");
    }
}
