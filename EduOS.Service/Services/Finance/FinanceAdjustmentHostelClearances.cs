using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EduOS.Service.Services.Finance;

public sealed partial class FinanceAdjustmentService
{
    private static HostelClearanceDto Map(HostelFinanceClearance x) => new()
    {
        Id = x.Id, SourceAllocationId = x.SourceAllocationId, CampusId = x.CampusId,
        Operation = x.Operation, State = x.State, DestinationBedId = x.DestinationBedId,
        EffectiveDate = x.EffectiveDate, RequestReason = x.RequestReason,
        RequestedByUserId = x.RequestedByUserId, CalculationMode = x.CalculationMode,
        MonthlyRentAtRequest = x.MonthlyRentAtRequest, DepositAtRequest = x.DepositAtRequest,
        SuggestedRentCredit = x.SuggestedRentCredit, SuggestedDepositRefund = x.SuggestedDepositRefund,
        ApprovedRentCredit = x.ApprovedRentCredit, ApprovedDepositRefund = x.ApprovedDepositRefund,
        SuggestedNetAdjustment = x.SuggestedNetAdjustment, ApprovedNetAdjustment = x.ApprovedNetAdjustment,
        OverrideReason = x.OverrideReason, NoAdjustmentRequired = x.NoAdjustmentRequired,
        SettlementJournalId = x.SettlementJournalId, ReconciliationNote = x.ReconciliationNote,
        ReviewedByUserId = x.ReviewedByUserId, SuccessorAllocationId = x.SuccessorAllocationId,
        RowVersion = Version(x.RowVersion)
    };

    private static string Fingerprint(RequestHostelClearanceDto x) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            x.SourceAllocationId, x.Operation, x.DestinationBedId, x.EffectiveDate,
            x.SourceRowVersion, x.DestinationBedRowVersion,
            Reason = x.Reason.Trim(), x.ManualRentCredit, x.ManualDepositRefund, x.ManualNetAdjustment,
            OverrideReason = x.OverrideReason?.Trim()
        }))));

    private static bool Money(decimal value) => value >= 0m && value <= 999999999999.99m && Scale(value, 2);

    private void AuditReservationFinance(string action, string entity, long id, object? before, object? after)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = user.TenantId, UserId = user.UserId,
            Action = action, EntityName = entity, EntityId = id,
            OldValue = before == null ? null : JsonSerializer.Serialize(before),
            NewValue = after == null ? null : JsonSerializer.Serialize(after),
            OccurredAt = now, CreatedAt = now, CreatedBy = user.UserId
        });
    }

    private static HostelBillingPolicyDto PolicyDto(long campusId, HostelBillingPolicy? x) => new()
    {
        CampusId = campusId, CalculationMode = x?.CalculationMode ?? HostelBillingCalculationMode.DailyProrated,
        AllowManualOverride = x?.AllowManualOverride ?? true,
        RowVersion = x == null ? null : Version(x.RowVersion)
    };

    public async Task<ApiResponse<HostelBillingPolicyDto>> GetHostelBillingPolicyAsync(long campusId, CancellationToken ct = default)
    {
        if (!Authorized) return Error<HostelBillingPolicyDto>("Finance permission required.", 403);
        if (campusId <= 0 || !await db.Campuses.AnyAsync(x => x.TenantId == user.TenantId && x.Id == campusId && !x.IsDeleted, ct))
            return Error<HostelBillingPolicyDto>("Campus not found.", 404);
        var policy = await db.HostelBillingPolicies.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.CampusId == campusId && !x.IsDeleted, ct);
        return ApiResponse<HostelBillingPolicyDto>.SuccessResponse(PolicyDto(campusId, policy));
    }

    public async Task<ApiResponse<HostelBillingPolicyDto>> SaveHostelBillingPolicyAsync(HostelBillingPolicyDto request, CancellationToken ct = default)
    {
        if (!Authorized) return Error<HostelBillingPolicyDto>("Finance permission required.", 403);
        if (request == null || request.CampusId <= 0 || !Enum.IsDefined(request.CalculationMode))
            return Error<HostelBillingPolicyDto>("Invalid campus or billing mode.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            if (!await db.Campuses.AnyAsync(x => x.TenantId == user.TenantId && x.Id == request.CampusId && !x.IsDeleted && x.IsActive, ct))
                return Error<HostelBillingPolicyDto>("Active campus not found.", 404);
            var policy = await db.HostelBillingPolicies.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.CampusId == request.CampusId && !x.IsDeleted, ct);
            var previous = policy == null ? null : new { policy.CalculationMode, policy.AllowManualOverride };
            if (policy == null)
            {
                if (request.RowVersion != null) return Error<HostelBillingPolicyDto>("Policy changed. Reload.", 409);
                policy = new HostelBillingPolicy { TenantId = user.TenantId, CampusId = request.CampusId,
                    CreatedBy = user.UserId, CreatedAt = clock.GetUtcNow().UtcDateTime };
                db.HostelBillingPolicies.Add(policy);
            }
            else
            {
                if (!Matches(policy.RowVersion, request.RowVersion)) return Error<HostelBillingPolicyDto>("Policy changed. Reload.", 409);
                policy.UpdatedBy = user.UserId; policy.UpdatedAt = clock.GetUtcNow().UtcDateTime;
            }
            policy.CalculationMode = request.CalculationMode;
            policy.AllowManualOverride = request.AllowManualOverride;
            await db.SaveChangesAsync(ct);
            AuditReservationFinance("HostelBillingPolicyChanged", nameof(HostelBillingPolicy), policy.Id,
                previous, new { policy.CampusId, policy.CalculationMode, policy.AllowManualOverride });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<HostelBillingPolicyDto>.SuccessResponse(PolicyDto(request.CampusId, policy));
        }
        catch (DbUpdateConcurrencyException) { return Error<HostelBillingPolicyDto>("Policy changed. Reload.", 409); }
        catch (DbUpdateException) { return Error<HostelBillingPolicyDto>("Conflicting policy. Reload.", 409); }
    }

    public async Task<ApiResponse<HostelClearanceDto>> RequestHostelClearanceAsync(RequestHostelClearanceDto request, CancellationToken ct = default)
    {
        if (!Authorized) return Error<HostelClearanceDto>("Finance permission required.", 403);
        if (request == null || request.ClientRequestId == Guid.Empty || request.SourceAllocationId <= 0 ||
            request.EffectiveDate == default || !Enum.IsDefined(request.Operation) ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 5 or > 500 ||
            !MatchesVersionFormat(request.SourceRowVersion) ||
            (request.Operation == HostelClearanceOperation.Transfer && (request.DestinationBedId is null or <= 0 ||
                !MatchesVersionFormat(request.DestinationBedRowVersion))) ||
            (request.Operation != HostelClearanceOperation.Transfer &&
                (request.DestinationBedId.HasValue || request.DestinationBedRowVersion != null)) ||
            (request.ManualRentCredit.HasValue && !Money(request.ManualRentCredit.Value)) ||
            (request.ManualDepositRefund.HasValue && !Money(request.ManualDepositRefund.Value)) ||
            (request.Operation == HostelClearanceOperation.UpcomingStartReschedule &&
                (request.ManualRentCredit.HasValue || request.ManualDepositRefund.HasValue)) ||
            (request.Operation != HostelClearanceOperation.UpcomingStartReschedule && request.ManualNetAdjustment.HasValue))
            return Error<HostelClearanceDto>("Invalid clearance request, versions or monetary amounts.");
        var hash = Fingerprint(request);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var existing = await db.HostelFinanceClearances.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.ClientRequestId == request.ClientRequestId, ct);
            if (existing != null)
                return existing.RequestHash == hash
                    ? ApiResponse<HostelClearanceDto>.SuccessResponse(Map(existing), "Existing request returned.")
                    : Error<HostelClearanceDto>("Request key used with different values.", 409);
            var source = await db.StudentHostelAllocations.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == request.SourceAllocationId && !x.IsDeleted, ct);
            if (source == null) return Error<HostelClearanceDto>("Stay not found.", 404);
            if (!Matches(source.RowVersion, request.SourceRowVersion) || source.State != HostelAllocationState.Active)
                return Error<HostelClearanceDto>("Stay has changed. Reload.", 409);
            var campusId = await (from enrollment in db.StudentEnrollments.AsNoTracking()
                join student in db.Students.AsNoTracking() on new { enrollment.TenantId, Id = enrollment.StudentId } equals new { student.TenantId, Id = student.Id }
                join bed in db.HostelBeds.AsNoTracking() on new { source.TenantId, Id = source.HostelBedId } equals new { bed.TenantId, Id = bed.Id }
                join room in db.HostelRooms.AsNoTracking() on new { bed.TenantId, Id = bed.HostelRoomId } equals new { room.TenantId, Id = room.Id }
                join hostel in db.Hostels.AsNoTracking() on new { room.TenantId, Id = room.HostelId } equals new { hostel.TenantId, Id = hostel.Id }
                where enrollment.TenantId == user.TenantId && enrollment.Id == source.StudentEnrollmentId &&
                    enrollment.StudentId == source.StudentId && enrollment.IsCurrent && enrollment.State == EnrollmentState.Active &&
                    student.Id == source.StudentId && !enrollment.IsDeleted && !student.IsDeleted &&
                    !bed.IsDeleted && !room.IsDeleted && !hostel.IsDeleted && hostel.CampusId == enrollment.CampusId
                select (long?)enrollment.CampusId).FirstOrDefaultAsync(ct);
            if (!campusId.HasValue) return Error<HostelClearanceDto>("Stay/enrollment campus mismatch.", 409);
            var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            if (request.Operation == HostelClearanceOperation.Transfer &&
                (source.StartDate >= today || request.EffectiveDate != today || source.EndDate < today) ||
                request.Operation == HostelClearanceOperation.Checkout &&
                (request.EffectiveDate < source.StartDate || request.EffectiveDate > today || source.StartDate > today) ||
                request.Operation == HostelClearanceOperation.UpcomingStartReschedule &&
                (source.StartDate <= today || request.EffectiveDate <= today ||
                    request.EffectiveDate == source.StartDate ||
                    request.EffectiveDate > today.AddYears(2) ||
                    source.EndDate.HasValue && request.EffectiveDate > source.EndDate.Value ||
                    source.EndDate.HasValue && source.EndDate.Value.DayNumber - request.EffectiveDate.DayNumber > 89))
                return Error<HostelClearanceDto>("Effective date is not eligible for this operation.", 409);
            if (request.Operation == HostelClearanceOperation.Transfer)
            {
                var target = await (from bed in db.HostelBeds.AsNoTracking()
                    join room in db.HostelRooms.AsNoTracking() on new { bed.TenantId, Id = bed.HostelRoomId } equals new { room.TenantId, Id = room.Id }
                    join hostel in db.Hostels.AsNoTracking() on new { room.TenantId, Id = room.HostelId } equals new { hostel.TenantId, Id = hostel.Id }
                    where bed.TenantId == user.TenantId && bed.Id == request.DestinationBedId &&
                        !bed.IsDeleted && bed.IsActive && !room.IsDeleted && room.IsActive && !hostel.IsDeleted && hostel.IsActive &&
                        room.Capacity > 0 && hostel.CampusId == campusId
                    select new { bed.RowVersion, bed.Id }).FirstOrDefaultAsync(ct);
                if (target == null || target.Id == source.HostelBedId || !Matches(target.RowVersion, request.DestinationBedRowVersion))
                    return Error<HostelClearanceDto>("Destination bed changed or is outside campus.", 409);
            }
            var policy = await db.HostelBillingPolicies.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.CampusId == campusId && !x.IsDeleted, ct);
            var mode = policy?.CalculationMode ?? HostelBillingCalculationMode.DailyProrated;
            var periodAmendment = request.Operation == HostelClearanceOperation.UpcomingStartReschedule;
            var periodQuote = periodAmendment ? ReservationTermQuoteCalculator.Calculate(
                source.StartDate, source.EndDate, request.EffectiveDate, source.EndDate,
                source.MonthlyRent, (int)mode) : null;
            ReservationQuoteSelection.Result? selected = null;
            if (periodAmendment)
            {
                try
                {
                    selected = ReservationQuoteSelection.Select(periodQuote!.NetAdjustment,
                        mode == HostelBillingCalculationMode.Manual, policy?.AllowManualOverride ?? true,
                        request.ManualNetAdjustment, request.OverrideReason);
                }
                catch (ArgumentException ex) { return Error<HostelClearanceDto>(ex.Message); }
                catch (InvalidOperationException ex) { return Error<HostelClearanceDto>(ex.Message, 409); }
                if (!selected.SelectedAmount.HasValue)
                    return Error<HostelClearanceDto>("Open-ended or manual reservations need an explicit Finance amount and reason.", 409);
            }
            var suggestion = periodAmendment ? (RentCredit: 0m, DepositRefund: 0m) :
                HostelBillingCalculator.Suggest(request.Operation, mode, source.MonthlyRent,
                    source.SecurityDeposit, request.EffectiveDate, source.EndDate);
            var manual = request.ManualRentCredit.HasValue || request.ManualDepositRefund.HasValue;
            if (!periodAmendment && (mode == HostelBillingCalculationMode.Manual && !manual ||
                manual && policy?.AllowManualOverride == false && mode != HostelBillingCalculationMode.Manual))
                return Error<HostelClearanceDto>("Manual amounts are required or manual overrides are disabled.", 409);
            var rent = request.ManualRentCredit ?? suggestion.RentCredit;
            var refund = request.ManualDepositRefund ?? suggestion.DepositRefund;
            if (!Money(rent) || !Money(refund) || refund > source.SecurityDeposit ||
                request.Operation == HostelClearanceOperation.Transfer && (rent != 0 || refund != 0) ||
                !periodAmendment && manual && (string.IsNullOrWhiteSpace(request.OverrideReason) ||
                    request.OverrideReason.Trim().Length is < 20 or > 1000))
                return Error<HostelClearanceDto>("Invalid override: provide 20-1000 character reason; cannot refund more than deposit or credit a transfer.");
            var exposure = await new ReservationFinancePolicy(db).GetExposureHashAsync(user.TenantId,
                source.StudentId, source.StudentEnrollmentId, ct);
            if (exposure == null) return Error<HostelClearanceDto>("Finance evidence is too large or unavailable for safe approval.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            var row = new HostelFinanceClearance
            {
                TenantId = user.TenantId, ClientRequestId = request.ClientRequestId, RequestHash = hash,
                SourceAllocationId = source.Id, CampusId = campusId.Value,
                StudentId = source.StudentId, StudentEnrollmentId = source.StudentEnrollmentId,
                Operation = request.Operation, DestinationBedId = request.DestinationBedId,
                EffectiveDate = request.EffectiveDate, SourceVersion = request.SourceRowVersion,
                DestinationVersion = request.DestinationBedRowVersion, FinanceSnapshotHash = exposure,
                RequestReason = request.Reason!.Trim(), RequestedByUserId = user.UserId,
                CalculationMode = mode, MonthlyRentAtRequest = source.MonthlyRent,
                DepositAtRequest = source.SecurityDeposit,
                SuggestedRentCredit = suggestion.RentCredit, SuggestedDepositRefund = suggestion.DepositRefund,
                ApprovedRentCredit = rent, ApprovedDepositRefund = refund,
                SuggestedNetAdjustment = periodQuote?.NetAdjustment ?? 0m,
                ApprovedNetAdjustment = selected?.SelectedAmount ?? 0m,
                OverrideReason = periodAmendment ? selected?.IsManual == true ? request.OverrideReason?.Trim() : null :
                    manual ? request.OverrideReason?.Trim() : null,
                CreatedBy = user.UserId, CreatedAt = now
            };
            db.HostelFinanceClearances.Add(row);
            await db.SaveChangesAsync(ct);
            AuditReservationFinance("HostelClearanceRequested", nameof(HostelFinanceClearance), row.Id, null,
                new { row.SourceAllocationId, row.Operation, row.DestinationBedId, row.EffectiveDate,
                    row.CalculationMode, row.SuggestedRentCredit, row.SuggestedDepositRefund,
                    row.ApprovedRentCredit, row.ApprovedDepositRefund, row.SuggestedNetAdjustment,
                    row.ApprovedNetAdjustment, row.OverrideReason });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<HostelClearanceDto>.SuccessResponse(Map(row), "Finance clearance requested. No GL posting has occurred.");
        }
        catch (DbUpdateConcurrencyException) { return Error<HostelClearanceDto>("Stay or clearance changed.", 409); }
        catch (DbUpdateException) { return Error<HostelClearanceDto>("Duplicate or conflicting clearance.", 409); }
    }

    private static bool MatchesVersionFormat(string? version)
    {
        try { return !string.IsNullOrWhiteSpace(version) && Convert.FromBase64String(version).Length > 0; }
        catch (FormatException) { return false; }
    }

    public async Task<ApiResponse<HostelClearanceDto>> ReviewHostelClearanceAsync(long id, ReviewHostelClearanceDto request, CancellationToken ct = default)
    {
        if (!Authorized) return Error<HostelClearanceDto>("Finance permission required.", 403);
        if (id <= 0 || request == null || string.IsNullOrWhiteSpace(request.ReconciliationNote) ||
            request.ReconciliationNote.Trim().Length is < 20 or > 1000)
            return Error<HostelClearanceDto>("Reconciliation note (20-1000 characters) is required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var row = await db.HostelFinanceClearances.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == id && !x.IsDeleted, ct);
            if (row == null) return Error<HostelClearanceDto>("Clearance not found.", 404);
            if (row.State != HostelClearanceState.Pending || !Matches(row.RowVersion, request.RowVersion))
                return Error<HostelClearanceDto>("Clearance changed. Reload.", 409);
            if (row.RequestedByUserId == user.UserId)
                return Error<HostelClearanceDto>("Requester cannot approve their own clearance.", 403);
            var previous = new { row.State, row.ApprovedRentCredit, row.ApprovedDepositRefund,
                row.ApprovedNetAdjustment, row.OverrideReason, row.NoAdjustmentRequired, row.SettlementJournalId };
            if (request.Approve)
            {
                var source = await db.StudentHostelAllocations.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.Id == row.SourceAllocationId && !x.IsDeleted, ct);
                if (source == null || source.State != HostelAllocationState.Active || !Matches(source.RowVersion, row.SourceVersion) ||
                    source.MonthlyRent != row.MonthlyRentAtRequest || source.SecurityDeposit != row.DepositAtRequest)
                    return Error<HostelClearanceDto>("Stay terms changed since request.", 409);
                if (row.DestinationBedId.HasValue)
                {
                    var bed = await db.HostelBeds.AsNoTracking().FirstOrDefaultAsync(x =>
                        x.TenantId == user.TenantId && x.Id == row.DestinationBedId && !x.IsDeleted, ct);
                    if (bed == null || !Matches(bed.RowVersion, row.DestinationVersion))
                        return Error<HostelClearanceDto>("Destination changed since request.", 409);
                }
                var exposure = await new ReservationFinancePolicy(db).GetExposureHashAsync(user.TenantId,
                    row.StudentId, row.StudentEnrollmentId, ct);
                if (exposure == null || exposure != row.FinanceSnapshotHash)
                    return Error<HostelClearanceDto>("Finance exposure changed. Create a new clearance.", 409);
                if (row.Operation == HostelClearanceOperation.UpcomingStartReschedule)
                {
                    if (request.ManualRentCredit.HasValue || request.ManualDepositRefund.HasValue ||
                        source.StartDate <= DateOnly.FromDateTime(clock.GetLocalNow().DateTime) ||
                        row.EffectiveDate <= DateOnly.FromDateTime(clock.GetLocalNow().DateTime) ||
                        source.StartDate == row.EffectiveDate ||
                        source.EndDate.HasValue && row.EffectiveDate > source.EndDate.Value)
                        return Error<HostelClearanceDto>("Upcoming period or adjustment parameters are no longer eligible.", 409);
                    decimal net = row.ApprovedNetAdjustment;
                    if (request.ManualNetAdjustment.HasValue)
                    {
                        var policy = await db.HostelBillingPolicies.AsNoTracking().FirstOrDefaultAsync(x =>
                            x.TenantId == user.TenantId && x.CampusId == row.CampusId && !x.IsDeleted, ct);
                        try
                        {
                            var selection = ReservationQuoteSelection.Select(row.SuggestedNetAdjustment,
                                row.CalculationMode == HostelBillingCalculationMode.Manual,
                                policy?.AllowManualOverride ?? true,
                                request.ManualNetAdjustment, request.OverrideReason);
                            net = selection.SelectedAmount!.Value;
                        }
                        catch (ArgumentException ex) { return Error<HostelClearanceDto>(ex.Message); }
                        catch (InvalidOperationException ex) { return Error<HostelClearanceDto>(ex.Message, 409); }
                        row.OverrideReason = request.OverrideReason?.Trim();
                    }
                    else if (row.OverrideReason != null && row.CalculationMode != HostelBillingCalculationMode.Manual &&
                        await db.HostelBillingPolicies.AsNoTracking().AnyAsync(x =>
                            x.TenantId == user.TenantId && x.CampusId == row.CampusId && !x.IsDeleted &&
                            !x.AllowManualOverride, ct))
                        return Error<HostelClearanceDto>("Campus manual override policy changed. Request new approval.", 409);
                    if (!ReservationPeriodSettlement.ValidAmount(net))
                        return Error<HostelClearanceDto>("Invalid signed period adjustment.");
                    if (request.AutoPostInvoice)
                    {
                        if (net <= 0m || request.NoAdjustmentRequired || request.SettlementJournalId.HasValue ||
                            source.FeeHeadId.HasValue && request.AutoFeeHeadId.HasValue &&
                            source.FeeHeadId.Value != request.AutoFeeHeadId.Value)
                            return Error<HostelClearanceDto>("Automatic invoicing requires a positive amount, no existing journal and matching hostel fee head.", 409);
                        var (posting, error) = await ReservationAutomaticInvoicePosting.PrepareAsync(
                            db, user.TenantId, user.UserId, row.RequestedByUserId,
                            "HOSTEL", id, row.CampusId,
                            row.StudentEnrollmentId, row.StudentId, net,
                            source.FeeHeadId ?? request.AutoFeeHeadId, request.AccountingPeriodId,
                            DateOnly.FromDateTime(clock.GetLocalNow().DateTime), clock.GetUtcNow().UtcDateTime, ct);
                        if (posting == null)
                            return Error<HostelClearanceDto>(error ?? "Automatic invoice posting failed.", 409);
                        row.NoAdjustmentRequired = false;
                        row.SettlementJournalId = posting.JournalId;
                        // The new invoice is part of the approved exposure; capture that
                        // snapshot so Operations does not reject our own atomic posting.
                        row.FinanceSnapshotHash = await new ReservationFinancePolicy(db)
                            .GetExposureHashAsync(user.TenantId, row.StudentId, row.StudentEnrollmentId, ct)
                            ?? string.Empty;
                        if (row.FinanceSnapshotHash.Length != 64)
                            return Error<HostelClearanceDto>("Finance exposure is too large to approve safely.", 409);
                    }
                    else if (request.NoAdjustmentRequired)
                    {
                        if (net != 0m || request.SettlementJournalId.HasValue)
                            return Error<HostelClearanceDto>("A nonzero period change requires a reconciled journal.", 409);
                    }
                    else if (!await ReservationPeriodSettlement.HasPostedMatchingJournalAsync(db,
                        user.TenantId, row.CampusId, row.StudentId, net,
                        request.SettlementJournalId, $"HOSTEL-CLEARANCE-{id}", ct) ||
                        await db.TransportFinanceClearances.AsNoTracking().AnyAsync(x =>
                            x.TenantId == user.TenantId && x.SettlementJournalId == request.SettlementJournalId, ct))
                        return Error<HostelClearanceDto>("Finance journal must be posted, single-use, independent and match the signed student adjustment.", 409);
                    row.ApprovedNetAdjustment = net;
                    if (!request.AutoPostInvoice)
                    {
                        row.NoAdjustmentRequired = request.NoAdjustmentRequired;
                        row.SettlementJournalId = request.SettlementJournalId;
                    }
                }
                else
                {
                if (request.ManualNetAdjustment.HasValue || request.AutoPostInvoice)
                    return Error<HostelClearanceDto>("Signed period amount is only valid for upcoming reschedules.");
                var manual = request.ManualRentCredit.HasValue || request.ManualDepositRefund.HasValue;
                if (manual && (string.IsNullOrWhiteSpace(request.OverrideReason) ||
                    request.OverrideReason.Trim().Length is < 20 or > 1000))
                    return Error<HostelClearanceDto>("Finance override requires a reason of 20-1000 characters.");
                if (manual && row.CalculationMode != HostelBillingCalculationMode.Manual)
                {
                    var policy = await db.HostelBillingPolicies.AsNoTracking().FirstOrDefaultAsync(x =>
                        x.TenantId == user.TenantId && x.CampusId == row.CampusId && !x.IsDeleted, ct);
                    if (policy?.AllowManualOverride == false)
                        return Error<HostelClearanceDto>("Campus policy currently disallows manual override.", 409);
                }
                var rent = request.ManualRentCredit ?? row.ApprovedRentCredit;
                var refund = request.ManualDepositRefund ?? row.ApprovedDepositRefund;
                if (!Money(rent) || !Money(refund) || refund > row.DepositAtRequest ||
                    row.Operation == HostelClearanceOperation.Transfer && (rent > 0 || refund > 0))
                    return Error<HostelClearanceDto>("Invalid settlement amounts.");
                if (request.NoAdjustmentRequired)
                {
                    if (rent != 0 || refund != 0 || request.SettlementJournalId.HasValue)
                        return Error<HostelClearanceDto>("A nonzero settlement requires a posted journal; zero settlement must have no journal.");
                }
                else
                {
                    if (rent + refund == 0 || request.SettlementJournalId is null or <= 0)
                        return Error<HostelClearanceDto>("Provide a posted matching adjustment journal, or attest zero adjustment.");
                    var journal = await db.Journals.AsNoTracking().FirstOrDefaultAsync(x =>
                        x.Id == request.SettlementJournalId && x.TenantId == user.TenantId && !x.IsDeleted &&
                        x.SourceType == "Manual" && x.State == JournalState.Posted &&
                        x.ReferenceNumber == $"HOSTEL-CLEARANCE-{id}" && x.CampusId == row.CampusId &&
                        x.CurrencyCode == "BDT" && x.ApprovedByUserId.HasValue && x.ApprovedByUserId != x.CreatedBy, ct);
                    var verified = journal != null && await new ReservationFinancePolicy(db).HasValidPostedSettlementJournalAsync(
                        user.TenantId, journal.Id, ct);
                    if (!verified) return Error<HostelClearanceDto>("Settlement journal is not posted, independently approved, or clearance-bound.", 409);
                    var lines = await db.JournalLines.AsNoTracking().Where(x =>
                        x.TenantId == user.TenantId && x.JournalId == journal!.Id && !x.IsDeleted).ToListAsync(ct);
                    if (lines.Sum(x => x.DebitAmount) != rent + refund ||
                        !lines.Any(x => x.StudentId == row.StudentId) ||
                        lines.Any(x => x.StudentId.HasValue && x.StudentId != row.StudentId))
                        return Error<HostelClearanceDto>("Journal totals/student do not match approved settlement.", 409);
                }
                row.ApprovedRentCredit = rent;
                row.ApprovedDepositRefund = refund;
                if (manual) row.OverrideReason = request.OverrideReason?.Trim();
                row.NoAdjustmentRequired = request.NoAdjustmentRequired;
                row.SettlementJournalId = request.SettlementJournalId;
                }
                row.State = HostelClearanceState.Approved;
            }
            else row.State = HostelClearanceState.Rejected;
            row.ReconciliationNote = request.ReconciliationNote!.Trim();
            row.ReviewedByUserId = user.UserId;
            row.ReviewedAtUtc = clock.GetUtcNow().UtcDateTime;
            row.UpdatedAt = row.ReviewedAtUtc; row.UpdatedBy = user.UserId;
            AuditReservationFinance("HostelClearanceReviewed", nameof(HostelFinanceClearance), row.Id,
                previous, new { row.State, row.ApprovedRentCredit, row.ApprovedDepositRefund, row.ApprovedNetAdjustment,
                    row.OverrideReason, row.NoAdjustmentRequired, row.SettlementJournalId,
                    row.ReconciliationNote, row.ReviewedByUserId });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<HostelClearanceDto>.SuccessResponse(Map(row),
                request.Approve ? "Clearance approved. Payout remains Finance-owned." : "Clearance rejected.");
        }
        catch (DbUpdateConcurrencyException) { return Error<HostelClearanceDto>("Clearance changed.", 409); }
        catch (DbUpdateException) { return Error<HostelClearanceDto>("Conflicting clearance or journal.", 409); }
    }

    public async Task<ApiResponse<PagedResult<HostelClearanceDto>>> GetHostelClearancesAsync(long? campusId, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Authorized) return Error<PagedResult<HostelClearanceDto>>("Finance permission required.", 403);
        if (campusId is <= 0 || page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return Error<PagedResult<HostelClearanceDto>>("Invalid filter.");
        var query = db.HostelFinanceClearances.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && !x.IsDeleted && (!campusId.HasValue || x.CampusId == campusId.Value));
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<HostelClearanceDto>>.SuccessResponse(new PagedResult<HostelClearanceDto>
        { Page = page, PageSize = pageSize, TotalCount = count, Items = rows.Select(Map).ToList() });
    }
}
