using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EduOS.Service.Services.Finance;

public sealed partial class FinanceAdjustmentService
{
    private static TransportBillingPolicyDto TransportPolicyDto(long campusId, TransportBillingPolicy? policy) => new()
    {
        CampusId = campusId,
        CalculationMode = policy?.CalculationMode ?? TransportBillingCalculationMode.DailyProrated,
        AllowManualOverride = policy?.AllowManualOverride ?? true,
        RowVersion = policy == null ? null : Version(policy.RowVersion)
    };

    private static TransportClearanceDto Map(TransportFinanceClearance x, Guid sourceReference, Guid? destinationReference) => new()
    {
        Id = x.Id, SourceAssignmentReference = sourceReference, CampusId = x.CampusId,
        Operation = x.Operation, State = x.State, DestinationVehicleReference = destinationReference,
        EffectiveDate = x.EffectiveDate, CalculationMode = x.CalculationMode,
        MonthlyFareAtRequest = x.MonthlyFareAtRequest,
        SuggestedFareCredit = x.SuggestedFareCredit, ApprovedFareCredit = x.ApprovedFareCredit,
        OverrideReason = x.OverrideReason, RequestReason = x.RequestReason,
        RequestedByUserId = x.RequestedByUserId, NoAdjustmentRequired = x.NoAdjustmentRequired,
        SettlementJournalId = x.SettlementJournalId, ReconciliationNote = x.ReconciliationNote,
        ReviewedByUserId = x.ReviewedByUserId, SuccessorAssignmentId = x.SuccessorAssignmentId,
        RowVersion = Version(x.RowVersion)
    };

    private static string TransportHash(RequestTransportClearanceDto x) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            x.SourceAssignmentReference, x.Operation, x.DestinationVehicleReference,
            x.EffectiveDate, x.SourceRowVersion, Reason = x.Reason.Trim(),
            x.ManualFareCredit, OverrideReason = x.OverrideReason?.Trim()
        }))));

    public async Task<ApiResponse<TransportBillingPolicyDto>> GetTransportBillingPolicyAsync(long campusId, CancellationToken ct = default)
    {
        if (!Authorized) return Error<TransportBillingPolicyDto>("Finance permission required.", 403);
        if (campusId <= 0 || !await db.Campuses.AnyAsync(x => x.TenantId == user.TenantId && x.Id == campusId && !x.IsDeleted, ct))
            return Error<TransportBillingPolicyDto>("Campus not found.", 404);
        var policy = await db.TransportBillingPolicies.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.CampusId == campusId && !x.IsDeleted, ct);
        return ApiResponse<TransportBillingPolicyDto>.SuccessResponse(TransportPolicyDto(campusId, policy));
    }

    public async Task<ApiResponse<TransportBillingPolicyDto>> SaveTransportBillingPolicyAsync(TransportBillingPolicyDto request, CancellationToken ct = default)
    {
        if (!Authorized) return Error<TransportBillingPolicyDto>("Finance permission required.", 403);
        if (request == null || request.CampusId <= 0 || !Enum.IsDefined(request.CalculationMode))
            return Error<TransportBillingPolicyDto>("Invalid policy.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            if (!await db.Campuses.AnyAsync(x => x.TenantId == user.TenantId && x.Id == request.CampusId && !x.IsDeleted && x.IsActive, ct))
                return Error<TransportBillingPolicyDto>("Active campus not found.", 404);
            var policy = await db.TransportBillingPolicies.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.CampusId == request.CampusId && !x.IsDeleted, ct);
            var previous = policy == null ? null : new { policy.CalculationMode, policy.AllowManualOverride };
            if (policy == null)
            {
                if (request.RowVersion != null) return Error<TransportBillingPolicyDto>("Policy changed. Reload.", 409);
                policy = new TransportBillingPolicy { TenantId = user.TenantId,
                    CampusId = request.CampusId, CreatedAt = clock.GetUtcNow().UtcDateTime, CreatedBy = user.UserId };
                db.TransportBillingPolicies.Add(policy);
            }
            else
            {
                if (!Matches(policy.RowVersion, request.RowVersion)) return Error<TransportBillingPolicyDto>("Policy changed. Reload.", 409);
                policy.UpdatedAt = clock.GetUtcNow().UtcDateTime; policy.UpdatedBy = user.UserId;
            }
            policy.CalculationMode = request.CalculationMode;
            policy.AllowManualOverride = request.AllowManualOverride;
            await db.SaveChangesAsync(ct);
            AuditReservationFinance("TransportBillingPolicyChanged", nameof(TransportBillingPolicy), policy.Id,
                previous, new { policy.CampusId, policy.CalculationMode, policy.AllowManualOverride });
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return ApiResponse<TransportBillingPolicyDto>.SuccessResponse(TransportPolicyDto(request.CampusId, policy));
        }
        catch (DbUpdateConcurrencyException) { return Error<TransportBillingPolicyDto>("Policy changed. Reload.", 409); }
        catch (DbUpdateException) { return Error<TransportBillingPolicyDto>("Conflicting policy.", 409); }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (TransportClearanceSqlConflict(ex))
        { return Error<TransportBillingPolicyDto>("Policy conflicts with another update. Reload.", 409); }
        catch (InvalidOperationException ex) when (TransportClearanceSqlConflict(ex))
        { return Error<TransportBillingPolicyDto>("Policy conflicts with another update. Reload.", 409); }
    }

    private async Task<TransportClearanceDto> TransportMapAsync(TransportFinanceClearance x, CancellationToken ct)
    {
        var source = await db.StudentTransports.IgnoreQueryFilters().AsNoTracking().Where(a => a.TenantId == user.TenantId && a.Id == x.SourceAssignmentId)
            .Select(a => a.PublicId).FirstAsync(ct);
        var target = x.DestinationVehicleId.HasValue
            ? await db.Vehicles.IgnoreQueryFilters().AsNoTracking().Where(v => v.TenantId == user.TenantId && v.Id == x.DestinationVehicleId.Value)
                .Select(v => (Guid?)v.PublicId).FirstOrDefaultAsync(ct) : null;
        return Map(x, source, target);
    }

    public async Task<ApiResponse<TransportClearanceDto>> RequestTransportClearanceAsync(RequestTransportClearanceDto request, CancellationToken ct = default)
    {
        if (!Authorized) return Error<TransportClearanceDto>("Finance permission required.", 403);
        if (request == null || request.ClientRequestId == Guid.Empty || request.SourceAssignmentReference == Guid.Empty ||
            request.EffectiveDate == default || !Enum.IsDefined(request.Operation) ||
            !MatchesVersionFormat(request.SourceRowVersion) || string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Trim().Length is < 5 or > 500 ||
            request.Operation == TransportClearanceOperation.VehicleTransfer &&
                (request.DestinationVehicleReference == null || request.DestinationVehicleReference == Guid.Empty) ||
            request.Operation == TransportClearanceOperation.Closure && request.DestinationVehicleReference.HasValue ||
            request.ManualFareCredit.HasValue && !Money(request.ManualFareCredit.Value))
            return Error<TransportClearanceDto>("Invalid transport clearance request.");
        var fingerprint = TransportHash(request);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var existing = await db.TransportFinanceClearances.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.ClientRequestId == request.ClientRequestId, ct);
            if (existing != null)
                return existing.RequestHash == fingerprint
                    ? ApiResponse<TransportClearanceDto>.SuccessResponse(await TransportMapAsync(existing, ct), "Existing request returned.")
                    : Error<TransportClearanceDto>("Request key used with different values.", 409);
            var source = await db.StudentTransports.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.PublicId == request.SourceAssignmentReference && !x.IsDeleted, ct);
            if (source == null) return Error<TransportClearanceDto>("Assignment not found.", 404);
            if (source.State != TransportAssignmentState.Active || !Matches(source.RowVersion, request.SourceRowVersion) ||
                !Money(source.MonthlyFare)) return Error<TransportClearanceDto>("Assignment version, state or fare invalid.", 409);
            var campus = await db.StudentEnrollments.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
                x.Id == source.StudentEnrollmentId && x.StudentId == source.StudentId &&
                x.IsCurrent && x.State == EnrollmentState.Active && !x.IsDeleted)
                .Select(x => (long?)x.CampusId).FirstOrDefaultAsync(ct);
            if (!campus.HasValue || !await db.Campuses.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.Id == campus && x.IsActive && !x.IsDeleted, ct) ||
                !await db.Routes.AsNoTracking().AnyAsync(x =>
                    x.TenantId == user.TenantId && x.Id == source.RouteId && !x.IsDeleted &&
                    (!x.CampusId.HasValue || x.CampusId == campus), ct) ||
                !await db.Vehicles.AsNoTracking().AnyAsync(x =>
                    x.TenantId == user.TenantId && x.Id == source.VehicleId && !x.IsDeleted &&
                    (!x.CampusId.HasValue || x.CampusId == campus), ct))
                return Error<TransportClearanceDto>("Assignment campus/route/vehicle ownership invalid.", 409);
            var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            if (request.Operation == TransportClearanceOperation.VehicleTransfer &&
                (source.StartDate > today || request.EffectiveDate <= source.StartDate ||
                 request.EffectiveDate < today || source.EndDate < request.EffectiveDate) ||
                request.Operation == TransportClearanceOperation.Closure &&
                (source.StartDate > today || request.EffectiveDate < source.StartDate ||
                 request.EffectiveDate > today || source.EndDate < request.EffectiveDate))
                return Error<TransportClearanceDto>("Transport effective date is outside eligible service period.", 409);
            long? targetId = null;
            string? targetVersion = null;
            if (request.DestinationVehicleReference.HasValue)
            {
                var target = await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.PublicId == request.DestinationVehicleReference && !x.IsDeleted &&
                    x.IsActive && x.Capacity > 0 && (!x.CampusId.HasValue || x.CampusId == campus), ct);
                if (target == null || target.Id == source.VehicleId)
                    return Error<TransportClearanceDto>("Destination vehicle unavailable.", 409);
                var end = source.EndDate;
                if (!await db.RouteVehicleAssignments.AsNoTracking().AnyAsync(x =>
                    x.TenantId == user.TenantId && !x.IsDeleted && x.IsCurrent &&
                    x.RouteId == source.RouteId && x.VehicleId == target.Id &&
                    x.EffectiveFrom <= request.EffectiveDate &&
                    (!x.EffectiveTo.HasValue || (end.HasValue && x.EffectiveTo >= end)), ct))
                    return Error<TransportClearanceDto>("Destination has no valid route/vehicle pairing.", 409);
                targetId = target.Id; targetVersion = Version(target.RowVersion);
            }
            var policy = await db.TransportBillingPolicies.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.CampusId == campus && !x.IsDeleted, ct);
            var mode = policy?.CalculationMode ?? TransportBillingCalculationMode.DailyProrated;
            var suggested = request.Operation == TransportClearanceOperation.Closure &&
                mode == TransportBillingCalculationMode.DailyProrated
                ? HostelBillingCalculator.ProrateUnusedMonth(source.MonthlyFare, request.EffectiveDate, source.EndDate) : 0m;
            var manual = request.ManualFareCredit.HasValue;
            if (mode == TransportBillingCalculationMode.Manual && !manual ||
                manual && policy?.AllowManualOverride == false && mode != TransportBillingCalculationMode.Manual)
                return Error<TransportClearanceDto>("Manual amount required or campus overrides disabled.", 409);
            var amount = request.ManualFareCredit ?? suggested;
            if (!Money(amount) || request.Operation == TransportClearanceOperation.VehicleTransfer && amount != 0m ||
                manual && (string.IsNullOrWhiteSpace(request.OverrideReason) || request.OverrideReason.Trim().Length is < 20 or > 1000))
                return Error<TransportClearanceDto>("Invalid fare credit or missing manual override reason.");
            var exposure = await new ReservationFinancePolicy(db).GetExposureHashAsync(user.TenantId,
                source.StudentId, source.StudentEnrollmentId, ct);
            if (exposure == null) return Error<TransportClearanceDto>("Finance evidence too large/unavailable.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            var row = new TransportFinanceClearance
            {
                TenantId = user.TenantId, ClientRequestId = request.ClientRequestId, RequestHash = fingerprint,
                SourceAssignmentId = source.Id, CampusId = campus.Value, StudentId = source.StudentId,
                StudentEnrollmentId = source.StudentEnrollmentId, Operation = request.Operation,
                DestinationVehicleId = targetId, EffectiveDate = request.EffectiveDate,
                SourceVersion = request.SourceRowVersion, DestinationVersion = targetVersion,
                FinanceSnapshotHash = exposure, RequestReason = request.Reason.Trim(),
                RequestedByUserId = user.UserId, CalculationMode = mode,
                MonthlyFareAtRequest = source.MonthlyFare, SuggestedFareCredit = suggested,
                ApprovedFareCredit = amount, OverrideReason = manual ? request.OverrideReason?.Trim() : null,
                CreatedAt = now, CreatedBy = user.UserId
            };
            db.TransportFinanceClearances.Add(row); await db.SaveChangesAsync(ct);
            AuditReservationFinance("TransportClearanceRequested", nameof(TransportFinanceClearance), row.Id, null,
                new { row.SourceAssignmentId, row.DestinationVehicleId, row.Operation, row.EffectiveDate,
                    row.SuggestedFareCredit, row.ApprovedFareCredit, row.OverrideReason });
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return ApiResponse<TransportClearanceDto>.SuccessResponse(
                Map(row, source.PublicId, request.DestinationVehicleReference), "Transport clearance requested; no GL posting.");
        }
        catch (DbUpdateConcurrencyException) { return Error<TransportClearanceDto>("Assignment changed.", 409); }
        catch (DbUpdateException) { return Error<TransportClearanceDto>("Conflicting or duplicate clearance.", 409); }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (TransportClearanceSqlConflict(ex))
        { return Error<TransportClearanceDto>("Finance request conflicts with another update. Reload.", 409); }
        catch (InvalidOperationException ex) when (TransportClearanceSqlConflict(ex))
        { return Error<TransportClearanceDto>("Finance request conflicts with another update. Reload.", 409); }
    }

    public async Task<ApiResponse<TransportClearanceDto>> ReviewTransportClearanceAsync(long id, ReviewTransportClearanceDto request, CancellationToken ct = default)
    {
        if (!Authorized) return Error<TransportClearanceDto>("Finance permission required.", 403);
        if (id <= 0 || request == null || string.IsNullOrWhiteSpace(request.ReconciliationNote) ||
            request.ReconciliationNote.Trim().Length is < 20 or > 1000)
            return Error<TransportClearanceDto>("Reconciliation evidence (20-1000 characters) required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var row = await db.TransportFinanceClearances.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == id && !x.IsDeleted, ct);
            if (row == null) return Error<TransportClearanceDto>("Clearance not found.", 404);
            if (row.State != TransportClearanceState.Pending || !Matches(row.RowVersion, request.RowVersion))
                return Error<TransportClearanceDto>("Clearance changed. Reload.", 409);
            if (row.RequestedByUserId == user.UserId)
                return Error<TransportClearanceDto>("Requester cannot approve their own clearance.", 403);
            var before = new { row.State, row.ApprovedFareCredit, row.OverrideReason, row.NoAdjustmentRequired, row.SettlementJournalId };
            if (request.Approve)
            {
                var source = await db.StudentTransports.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.Id == row.SourceAssignmentId && !x.IsDeleted, ct);
                if (source == null || source.State != TransportAssignmentState.Active ||
                    !Matches(source.RowVersion, row.SourceVersion) || source.MonthlyFare != row.MonthlyFareAtRequest)
                    return Error<TransportClearanceDto>("Assignment changed after request.", 409);
                if (row.DestinationVehicleId.HasValue)
                {
                    var target = await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(x =>
                        x.TenantId == user.TenantId && x.Id == row.DestinationVehicleId && !x.IsDeleted, ct);
                    if (target == null || !Matches(target.RowVersion, row.DestinationVersion))
                        return Error<TransportClearanceDto>("Destination vehicle changed after request.", 409);
                }
                var exposure = await new ReservationFinancePolicy(db).GetExposureHashAsync(user.TenantId,
                    row.StudentId, row.StudentEnrollmentId, ct);
                if (exposure == null || exposure != row.FinanceSnapshotHash)
                    return Error<TransportClearanceDto>("Finance exposure changed; request a new clearance.", 409);
                var manual = request.ManualFareCredit.HasValue;
                if (manual && (string.IsNullOrWhiteSpace(request.OverrideReason) ||
                    request.OverrideReason.Trim().Length is < 20 or > 1000))
                    return Error<TransportClearanceDto>("Manual adjustment requires an audit reason.");
                if (manual && row.CalculationMode != TransportBillingCalculationMode.Manual)
                {
                    var policy = await db.TransportBillingPolicies.AsNoTracking().FirstOrDefaultAsync(x =>
                        x.TenantId == user.TenantId && x.CampusId == row.CampusId && !x.IsDeleted, ct);
                    if (policy?.AllowManualOverride == false)
                        return Error<TransportClearanceDto>("Campus policy disallows manual override.", 409);
                }
                var amount = request.ManualFareCredit ?? row.ApprovedFareCredit;
                if (!Money(amount) || row.Operation == TransportClearanceOperation.VehicleTransfer && amount != 0m)
                    return Error<TransportClearanceDto>("Invalid approved fare adjustment.");
                if (request.NoAdjustmentRequired)
                {
                    if (amount != 0 || request.SettlementJournalId.HasValue)
                        return Error<TransportClearanceDto>("Nonzero adjustments require a posted journal; zero settlement has no journal.");
                }
                else
                {
                    if (amount <= 0 || request.SettlementJournalId is null or <= 0)
                        return Error<TransportClearanceDto>("Provide posted journal or attest no adjustment.");
                    var journal = await db.Journals.AsNoTracking().FirstOrDefaultAsync(x =>
                        x.TenantId == user.TenantId && x.Id == request.SettlementJournalId && !x.IsDeleted &&
                        x.SourceType == "Manual" && x.State == JournalState.Posted && x.CurrencyCode == "BDT" &&
                        x.ReferenceNumber == $"TRANSPORT-CLEARANCE-{id}" && x.CampusId == row.CampusId &&
                        x.ApprovedByUserId.HasValue && x.ApprovedByUserId != x.CreatedBy, ct);
                    if (journal == null || !await new ReservationFinancePolicy(db).HasValidPostedSettlementJournalAsync(
                        user.TenantId, journal.Id, ct))
                        return Error<TransportClearanceDto>("Posted and independently checked journal required.", 409);
                    var lines = await db.JournalLines.AsNoTracking().Where(x =>
                        x.TenantId == user.TenantId && x.JournalId == journal.Id && !x.IsDeleted).ToListAsync(ct);
                    if (lines.Sum(x => x.DebitAmount) != amount ||
                        !lines.Any(x => x.StudentId == row.StudentId) ||
                        lines.Any(x => x.StudentId.HasValue && x.StudentId != row.StudentId))
                        return Error<TransportClearanceDto>("Journal amount or student does not match.", 409);
                }
                row.ApprovedFareCredit = amount;
                if (manual) row.OverrideReason = request.OverrideReason?.Trim();
                row.NoAdjustmentRequired = request.NoAdjustmentRequired;
                row.SettlementJournalId = request.SettlementJournalId;
                row.State = TransportClearanceState.Approved;
            }
            else row.State = TransportClearanceState.Rejected;
            row.ReconciliationNote = request.ReconciliationNote.Trim();
            row.ReviewedByUserId = user.UserId;
            row.ReviewedAtUtc = clock.GetUtcNow().UtcDateTime;
            row.UpdatedAt = row.ReviewedAtUtc; row.UpdatedBy = user.UserId;
            AuditReservationFinance("TransportClearanceReviewed", nameof(TransportFinanceClearance), row.Id, before,
                new { row.State, row.ApprovedFareCredit, row.OverrideReason, row.NoAdjustmentRequired,
                    row.SettlementJournalId, row.ReconciliationNote, row.ReviewedByUserId });
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return ApiResponse<TransportClearanceDto>.SuccessResponse(await TransportMapAsync(row, ct),
                request.Approve ? "Finance clearance approved. No payout performed." : "Finance clearance rejected.");
        }
        catch (DbUpdateConcurrencyException) { return Error<TransportClearanceDto>("Clearance changed.", 409); }
        catch (DbUpdateException) { return Error<TransportClearanceDto>("Conflicting clearance or journal.", 409); }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (TransportClearanceSqlConflict(ex))
        { return Error<TransportClearanceDto>("Finance review conflicts with another update. Reload.", 409); }
        catch (InvalidOperationException ex) when (TransportClearanceSqlConflict(ex))
        { return Error<TransportClearanceDto>("Finance review conflicts with another update. Reload.", 409); }
    }

    private static bool TransportClearanceSqlConflict(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
            if (current is Microsoft.Data.SqlClient.SqlException { Number: 1205 or 1222 or 3960 or 2601 or 2627 })
                return true;
        return false;
    }

    public async Task<ApiResponse<PagedResult<TransportClearanceDto>>> GetTransportClearancesAsync(long? campusId, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Authorized) return Error<PagedResult<TransportClearanceDto>>("Finance permission required.", 403);
        if (campusId is <= 0 || page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue) return Error<PagedResult<TransportClearanceDto>>("Invalid filter.");
        var query = db.TransportFinanceClearances.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && !x.IsDeleted && (!campusId.HasValue || x.CampusId == campusId.Value));
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var sourceIds = rows.Select(x => x.SourceAssignmentId).Distinct().ToArray();
        var vehicleIds = rows.Where(x => x.DestinationVehicleId.HasValue).Select(x => x.DestinationVehicleId!.Value).Distinct().ToArray();
        var sources = await db.StudentTransports.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == user.TenantId && sourceIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.PublicId, ct);
        var targets = await db.Vehicles.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == user.TenantId && vehicleIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.PublicId, ct);
        return ApiResponse<PagedResult<TransportClearanceDto>>.SuccessResponse(new PagedResult<TransportClearanceDto>
        {
            Page = page, PageSize = pageSize, TotalCount = count,
            Items = rows.Select(x => Map(x, sources[x.SourceAssignmentId],
                x.DestinationVehicleId.HasValue && targets.TryGetValue(x.DestinationVehicleId.Value, out var target)
                    ? target : null)).ToList()
        });
    }
}
