using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EduOS.Service.Services.Finance;

public sealed partial class FinanceAdjustmentService
{
    /// <summary>
    /// Stops an approved discount from being used by subsequent invoice generation.
    /// Keeps the original approval, dates and issued financial documents intact.
    /// TenantAdmin is the only role authorized to manage assignments across campuses.
    /// </summary>
    public async Task<ApiResponse<StudentDiscountDto>> DeactivateStudentDiscountAsync(
        long discountId, DeactivateStudentDiscountRequestDto request, CancellationToken ct = default)
    {
        if (!Authorized) return Error<StudentDiscountDto>("Institution administrator permission required.", 403);
        if (discountId <= 0 || request == null || string.IsNullOrWhiteSpace(request.RowVersion) ||
            request.RowVersion.Length > 256)
            return Error<StudentDiscountDto>("A valid discount assignment and row version are required.");
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var row = await Assignments.FirstOrDefaultAsync(x => x.Id == discountId, token);
                if (row == null) return Error<StudentDiscountDto>("Discount assignment not found.", 404);
                var enrollment = await Enrollments.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == row.StudentEnrollmentId, token);
                if (enrollment == null)
                    return Error<StudentDiscountDto>("The assignment's enrollment is unavailable.", 409);
                var ruleName = await Discounts.AsNoTracking()
                    .Where(x => x.Id == row.DiscountRuleId)
                    .Select(x => x.Name).FirstOrDefaultAsync(token) ?? "[unavailable rule]";
                // A replay after a committed deactivation is a read-only success.
                if (!row.IsActive)
                    return ApiResponse<StudentDiscountDto>.SuccessResponse(
                        Map(row, enrollment.PublicId, ruleName), "Discount already inactive.");
                if (!Matches(row.RowVersion, request.RowVersion))
                    return Error<StudentDiscountDto>("Discount assignment changed; reload before deactivating.", 409);
                row.IsActive = false;
                row.UpdatedAt = clock.GetUtcNow().UtcDateTime;
                row.UpdatedBy = user.UserId;
                await db.SaveChangesAsync(token);
                return ApiResponse<StudentDiscountDto>.SuccessResponse(
                    Map(row, enrollment.PublicId, ruleName),
                    "Discount deactivated for future invoices. Issued invoices remain unchanged.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error<StudentDiscountDto>("Discount assignment changed; reload before deactivating.", 409);
        }
        catch (DbUpdateException)
        {
            return Error<StudentDiscountDto>("Conflicting discount deactivation; reload the register.", 409);
        }
    }
}
