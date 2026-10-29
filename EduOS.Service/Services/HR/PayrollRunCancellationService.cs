using System.Data;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Payroll;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.HR;

/// <summary>
/// Cancels an unposted run without erasing employee snapshots or approved accounting history.
/// Posted accruals require a separate compensating GL workflow and cannot be cancelled here.
/// </summary>
public sealed class PayrollRunCancellationService : IPayrollRunCancellationService
{
    private readonly EduOSDbContext db;
    private readonly ICurrentUserService user;
    private readonly TimeProvider clock;

    public PayrollRunCancellationService(EduOSDbContext db, ICurrentUserService user, TimeProvider clock)
    { this.db = db; this.user = user; this.clock = clock; }

    private static ApiResponse<CancelPayrollRunResultDto> Error(string message, int status = 400) =>
        ApiResponse<CancelPayrollRunResultDto>.ErrorResponse(message, status);

    private static bool Matches(byte[] actual, string supplied)
    {
        try { return actual.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
    }

    public async Task<ApiResponse<CancelPayrollRunResultDto>> CancelAsync(Guid reference,
        CancelPayrollRunRequestDto request, CancellationToken ct = default)
    {
        if (!user.IsAuthenticated || !user.IsTenantAdmin || user.TenantId <= 0 ||
            db.CurrentTenantId != user.TenantId)
            return Error("Tenant administrator permission required.", 403);
        var reason = request?.Reason?.Trim();
        if (reference == Guid.Empty || request == null || string.IsNullOrWhiteSpace(request.RowVersion) ||
            request.RowVersion.Length > 256 || string.IsNullOrWhiteSpace(reason) ||
            reason.Length is < 10 or > 1000)
            return Error("Payroll reference, current row version and a reason (10–1000 characters) are required.");

        var tenant = user.TenantId;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var run = await db.PayrollRuns.SingleOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == reference && !x.IsDeleted, ct);
            if (run == null) return Error("Payroll run not found.", 404);

            if (run.State == PayrollRunState.Cancelled)
            {
                // A repeat of the same one-way action is harmless and does not create audit duplicates.
                var prior = await db.AuditLogs.AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                    x.EntityName == "PayrollRun" && x.EntityId == run.Id &&
                    x.Action == "CancelUnpostedPayroll" && x.NewValue == reason, ct);
                if (!prior) return Error("Payroll was already cancelled for a different reason.", 409);
                return ApiResponse<CancelPayrollRunResultDto>.SuccessResponse(new CancelPayrollRunResultDto
                {
                    Reference = run.PublicId, RunNumber = run.RunNumber, State = run.State,
                    AlreadyCancelled = true, RowVersion = Convert.ToBase64String(run.RowVersion)
                }, "Existing cancellation returned.");
            }
            if (!Matches(run.RowVersion, request.RowVersion))
                return Error("Payroll changed; reload before cancelling.", 409);
            if (run.State is not (PayrollRunState.Draft or PayrollRunState.Calculated or PayrollRunState.Approved))
                return Error("Posted payroll is immutable; use an independently approved GL reversal workflow.", 409);

            // Check both directions: legacy journals may have SourceType/SourceId without a run.JournalId.
            if (run.JournalId.HasValue || await db.Journals.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.SourceType == "PayrollRun" && x.SourceId == run.Id, ct))
                return Error("A payroll GL journal exists; payroll cannot be administratively cancelled.", 409);
            // Soft-deleted legacy imports still count as financial history. Keep an explicit
            // tenant predicate whenever query filters are deliberately bypassed for this guard.
            var employeeIds = db.PayrollEmployees.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant &&
                x.PayrollRunId == run.Id).Select(x => x.Id);
            if (await db.PayrollPayments.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && employeeIds.Contains(x.PayrollEmployeeId), ct) ||
                await db.LoanAdvanceRecoveries.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.PayrollEmployeeId.HasValue &&
                    employeeIds.Contains(x.PayrollEmployeeId.Value), ct))
                return Error("This payroll has payment or loan recovery history and requires reconciliation.", 409);

            var oldState = run.State;
            var now = clock.GetUtcNow().UtcDateTime;
            run.State = PayrollRunState.Cancelled;
            run.UpdatedAt = now;
            run.UpdatedBy = user.UserId;
            db.AuditLogs.Add(new AuditLog
            {
                TenantId = tenant, UserId = user.UserId, Action = "CancelUnpostedPayroll",
                EntityName = "PayrollRun", EntityId = run.Id,
                OldValue = oldState.ToString(), NewValue = reason,
                IpAddress = user.IpAddress, OccurredAt = now
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<CancelPayrollRunResultDto>.SuccessResponse(new CancelPayrollRunResultDto
            {
                Reference = run.PublicId, RunNumber = run.RunNumber, State = run.State,
                RowVersion = Convert.ToBase64String(run.RowVersion)
            }, "Unposted payroll cancelled; employee snapshots retained for audit.");
        }
        catch (DbUpdateConcurrencyException) { return Error("Payroll changed concurrently; reload.", 409); }
        catch (DbUpdateException) { return Error("Payroll cancellation conflicts with another transaction.", 409); }
    }
}
