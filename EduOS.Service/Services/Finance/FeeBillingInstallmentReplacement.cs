using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EduOS.Service.Services.Finance;

public sealed partial class FeeBillingService
{
    // Only a completely unbilled agreement can be replaced. Cancellation of the
    // original and submission of the new frozen version share one SQL transaction.
    // Two immutable event reasons cross-reference the old/new public IDs, avoiding
    // a speculative second agreement schema or edits to historical plan rows.
    public async Task<ApiResponse<StudentFeeInstallmentPlanDto>> ReplaceInstallmentPlanAsync(
        Guid reference, ReplaceStudentFeeInstallmentPlanDto request, CancellationToken ct = default)
    {
        if (!CanManage() || !_user.IsTenantAdmin ||
            _uow is not EduOSDbContext db || db.CurrentTenantId != _user.TenantId)
            return Error<StudentFeeInstallmentPlanDto>("Independent tenant administrator required.", 403);
        if (reference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            request.FeeStructureId <= 0 || string.IsNullOrWhiteSpace(request.RowVersion) ||
            request.RowVersion.Length > 128 || string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Trim().Length is < 3 or > 150 ||
            request.Reason.Any(char.IsControl))
            return Error<StudentFeeInstallmentPlanDto>("Invalid replacement request, version or reason.");
        var reason = request.Reason.Trim();
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var tenant = _user.TenantId;
                var old = await db.StudentFeeInstallmentPlans.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.PublicId == reference && !x.IsDeleted, token);
                if (old == null || !await CanAccessBillingCampusAsync(old.CampusId, token))
                    return Error<StudentFeeInstallmentPlanDto>("Agreement unavailable.", 404);
                if (Convert.ToBase64String(old.RowVersion) != request.RowVersion)
                    return Error<StudentFeeInstallmentPlanDto>("Agreement changed; refresh.", 409);
                var prior = await db.StudentFeeInstallmentPlans.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.ClientRequestId == request.ClientRequestId, token);
                if (prior != null)
                {
                    var expected = "Replaced by " + prior.PublicId.ToString("D") + ": " + reason;
                    if (prior.StudentEnrollmentId != old.StudentEnrollmentId ||
                        prior.CampusId != old.CampusId || prior.AcademicYearId != old.AcademicYearId ||
                        prior.StartMonth != old.StartMonth || prior.EndMonth != old.EndMonth ||
                        prior.FeeStructureId != request.FeeStructureId ||
                        !await db.StudentFeeInstallmentPlanEvents.AsNoTracking().AnyAsync(x =>
                            x.TenantId == tenant && x.StudentFeeInstallmentPlanId == old.Id &&
                            x.ActionCode == 4 && !x.IsDeleted && x.Reason == expected, token))
                        return Error<StudentFeeInstallmentPlanDto>("Request key belongs to another replacement.", 409);
                    var replay = await InstallmentPlanDtoAsync(db, prior, true, token);
                    if (replay != null && !FeeInstallmentDistribution.Matches(replay, request.MonthWeightsBasisPoints))
                        return Error<StudentFeeInstallmentPlanDto>("Request key belongs to a different distribution.", 409);
                    return replay == null
                        ? Error<StudentFeeInstallmentPlanDto>("Replacement provenance unavailable.", 409)
                        : ApiResponse<StudentFeeInstallmentPlanDto>.SuccessResponse(replay, "Existing replacement returned.");
                }
                var events = await db.StudentFeeInstallmentPlanEvents.AsNoTracking().Where(x =>
                    x.TenantId == tenant && x.StudentFeeInstallmentPlanId == old.Id && !x.IsDeleted)
                    .OrderBy(x => x.ActionCode).Take(4).ToListAsync(token);
                if (events.Count != 2 || events[0].ActionCode != 2 || events[1].ActionCode != 3)
                    return Error<StudentFeeInstallmentPlanDto>("Only an approved, uncancelled agreement can be replaced.", 409);
                if (old.PreparedByUserId == _user.UserId ||
                    events.Any(x => x.ActorUserId == _user.UserId))
                    return Error<StudentFeeInstallmentPlanDto>("Replacement checker must be independent of the original approval.", 403);
                if (await InstallmentPlanDtoAsync(db, old, true, token) == null)
                    return Error<StudentFeeInstallmentPlanDto>("Original agreement integrity check failed.", 409);
                if (await db.StudentInvoices.AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.StudentEnrollmentId == old.StudentEnrollmentId &&
                    !x.IsDeleted && x.InvoiceDate >= old.StartMonth &&
                    x.InvoiceDate < old.EndMonth.AddMonths(1), token))
                    return Error<StudentFeeInstallmentPlanDto>("Historical invoice evidence blocks replacement; use approved adjustment.", 409);
                if (await db.StudentFeeInstallmentPlans.AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.StudentEnrollmentId == old.StudentEnrollmentId &&
                    x.Id != old.Id && !x.IsDeleted &&
                    x.StartMonth <= old.EndMonth && x.EndMonth >= old.StartMonth &&
                    !db.StudentFeeInstallmentPlanEvents.Any(e =>
                        e.TenantId == tenant && e.StudentFeeInstallmentPlanId == x.Id &&
                        !e.IsDeleted && e.ActionCode == 4), token))
                    return Error<StudentFeeInstallmentPlanDto>("Another agreement overlaps this period.", 409);
                var enrollment = await db.StudentEnrollments.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == old.StudentEnrollmentId && !x.IsDeleted &&
                    x.IsCurrent && x.State == EnrollmentState.Active &&
                    x.CampusId == old.CampusId && x.AcademicYearId == old.AcademicYearId, token);
                if (enrollment == null)
                    return Error<StudentFeeInstallmentPlanDto>("Enrollment changed; replacement blocked.", 409);
                var structure = await db.FeeStructures.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == request.FeeStructureId &&
                    !x.IsDeleted && x.IsActive, token);
                if (structure == null || structure.CurrencyCode != old.CurrencyCode ||
                    structure.CampusId != old.CampusId || structure.AcademicYearId != old.AcademicYearId ||
                    (structure.AcademicBatchId.HasValue && structure.AcademicBatchId != enrollment.AcademicBatchId) ||
                    (structure.AcademicLevelId.HasValue && structure.AcademicLevelId != enrollment.AcademicLevelId) ||
                    (structure.AcademicProgramId.HasValue && structure.AcademicProgramId != enrollment.AcademicProgramId) ||
                    structure.EffectiveFrom > old.StartMonth ||
                    structure.EffectiveTo.HasValue && structure.EffectiveTo.Value < old.EndMonth.AddMonths(1).AddDays(-1))
                    return Error<StudentFeeInstallmentPlanDto>("Replacement structure does not match this enrollment or period.", 409);
                var source = await (from line in db.FeeStructureLines.AsNoTracking()
                    join head in db.FeeHeads.AsNoTracking()
                        on new { line.TenantId, Id = line.FeeHeadId }
                        equals new { head.TenantId, head.Id }
                    where line.TenantId == tenant && line.FeeStructureId == structure.Id &&
                        !line.IsDeleted && !head.IsDeleted && head.IsActive
                    orderby line.FeeHeadId
                    select new { line.FeeHeadId, line.Amount, line.DueDayOfMonth, line.Frequency,
                        head.Code, head.Name }).Take(51).ToListAsync(token);
                if (source.Count is < 1 or > 50 || source.Any(x =>
                    x.Frequency != FeeFrequencyType.Monthly || x.Amount <= 0m ||
                    x.Amount > 1000000000m || x.Amount != decimal.Round(x.Amount, 2) ||
                    x.DueDayOfMonth is null or < 1 or > 31))
                    return Error<StudentFeeInstallmentPlanDto>("Replacement requires 1–50 valid monthly fee heads.", 409);
                var now = _clock.GetUtcNow().UtcDateTime;
                var months = (old.EndMonth.Year - old.StartMonth.Year) * 12 +
                    old.EndMonth.Month - old.StartMonth.Month + 1;
                if (months is < 1 or > 12)
                    return Error<StudentFeeInstallmentPlanDto>("Original agreement period is invalid.", 409);
                var distribution = FeeInstallmentDistribution.Allocate(
                    source.Select(x => x.Amount).ToArray(), months, request.MonthWeightsBasisPoints);
                if (distribution == null)
                    return Error<StudentFeeInstallmentPlanDto>("Invalid monthly shares or zero-cent fee head.", 409);
                var replacement = new StudentFeeInstallmentPlan {
                    TenantId = tenant, PublicId = Guid.NewGuid(), ClientRequestId = request.ClientRequestId,
                    StudentEnrollmentId = old.StudentEnrollmentId, FeeStructureId = structure.Id,
                    CampusId = old.CampusId, AcademicYearId = old.AcademicYearId,
                    StartMonth = old.StartMonth, EndMonth = old.EndMonth,
                    CurrencyCode = old.CurrencyCode, LineCount = source.Count * months,
                    GrossAmount = source.Sum(x => x.Amount) * months,
                    PreparedByUserId = _user.UserId, CreatedAt = now, CreatedBy = _user.UserId
                };
                db.StudentFeeInstallmentPlans.Add(replacement);
                await db.SaveChangesAsync(token);
                for (var m = 0; m < months; m++)
                {
                    var month = old.StartMonth.AddMonths(m);
                    for (var i = 0; i < source.Count; i++)
                    {
                        var line = source[i];
                        db.StudentFeeInstallmentPlanLines.Add(new StudentFeeInstallmentPlanLine {
                            TenantId = tenant, StudentFeeInstallmentPlanId = replacement.Id,
                            FeeHeadId = line.FeeHeadId, MonthStart = month,
                            DueDate = new DateOnly(month.Year, month.Month,
                                Math.Min(line.DueDayOfMonth!.Value, DateTime.DaysInMonth(month.Year, month.Month))),
                            FeeHeadCodeSnapshot = line.Code, FeeHeadNameSnapshot = line.Name,
                            GrossAmount = distribution[m][i], CreatedAt = now, CreatedBy = _user.UserId
                        });
                    }
                }
                db.StudentFeeInstallmentPlanEvents.AddRange(
                    new StudentFeeInstallmentPlanEvent {
                        TenantId = tenant, StudentFeeInstallmentPlanId = old.Id,
                        ClientRequestId = Guid.NewGuid(), ActionCode = 4,
                        ActorUserId = _user.UserId, EventAt = now,
                        Reason = "Replaced by " + replacement.PublicId.ToString("D") + ": " + reason,
                        CreatedAt = now, CreatedBy = _user.UserId
                    },
                    new StudentFeeInstallmentPlanEvent {
                        TenantId = tenant, StudentFeeInstallmentPlanId = replacement.Id,
                        ClientRequestId = Guid.NewGuid(), ActionCode = 2,
                        ActorUserId = _user.UserId, EventAt = now,
                        Reason = "Replacement of " + old.PublicId.ToString("D"),
                        CreatedAt = now, CreatedBy = _user.UserId
                    });
                var gate = await db.Tenants.FirstAsync(x => x.Id == tenant && !x.IsDeleted, token);
                gate.UpdatedAt = now; gate.UpdatedBy = _user.UserId;
                await db.SaveChangesAsync(token);
                var dto = await InstallmentPlanDtoAsync(db, replacement, true, token);
                return dto == null
                    ? Error<StudentFeeInstallmentPlanDto>("Replacement evidence validation failed.", 409)
                    : ApiResponse<StudentFeeInstallmentPlanDto>.SuccessResponse(dto,
                        "Original cancelled atomically; replacement submitted for independent approval.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 1205 or 3960 or 2601 or 2627)
        {
            db.ChangeTracker.Clear();
            return Error<StudentFeeInstallmentPlanDto>("Competing replacement or duplicate request; refresh.", 409);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return Error<StudentFeeInstallmentPlanDto>("Agreement changed concurrently; refresh.", 409);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return Error<StudentFeeInstallmentPlanDto>("Replacement conflicts with existing evidence.", 409);
        }
    }
}
