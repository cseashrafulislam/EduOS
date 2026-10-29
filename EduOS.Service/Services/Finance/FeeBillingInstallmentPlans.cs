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
    private async Task<bool> HasUnlinkedInstallmentPlanAsync(
        IEnumerable<long> enrollmentIds, DateOnly monthStart, CancellationToken ct)
    {
        if (_uow is not EduOSDbContext db || db.CurrentTenantId != _user.TenantId) return true;
        var ids = enrollmentIds.Distinct().ToArray();
        if (ids.Length == 0) return false;
        return await db.StudentFeeInstallmentPlans.AsNoTracking().AnyAsync(x =>
            x.TenantId == _user.TenantId && ids.Contains(x.StudentEnrollmentId) &&
            !x.IsDeleted && x.StartMonth <= monthStart && x.EndMonth >= monthStart &&
            !db.StudentFeeInstallmentPlanEvents.Any(e =>
                e.TenantId == x.TenantId && e.StudentFeeInstallmentPlanId == x.Id &&
                !e.IsDeleted && e.ActionCode == 4), ct);
    }

    // Immutable gross agreements. Review eligibility is a display hint only;
    // the approval command still checks current actor, version and invoice evidence.
    public async Task<ApiResponse<PagedResult<StudentFeeInstallmentPlanDto>>> GetInstallmentPlansAsync(
        long? campusId = null, int page = 1, int pageSize = 25, CancellationToken ct = default,
        string? search = null, int? stateCode = null, bool myApprovalQueue = false)
    {
        if (!CanManage() || _uow is not EduOSDbContext db || db.CurrentTenantId != _user.TenantId)
            return Error<PagedResult<StudentFeeInstallmentPlanDto>>("Finance access required.", 403);
        if (!ValidPage(page, pageSize) || pageSize > 50 || campusId is <= 0 ||
            search?.Length > 100 || stateCode is < 1 or > 4)
            return Error<PagedResult<StudentFeeInstallmentPlanDto>>("Invalid campus, search, state or page.");
        if (myApprovalQueue && !_user.IsTenantAdmin)
            return Error<PagedResult<StudentFeeInstallmentPlanDto>>("Independent approval role required.", 403);
        var skip = Skip(page, pageSize);
        if (skip < 0) return Error<PagedResult<StudentFeeInstallmentPlanDto>>("Page exceeds limit.");
        if (campusId.HasValue &&
            (!await db.Campuses.AsNoTracking().AnyAsync(x =>
                x.TenantId == _user.TenantId && x.Id == campusId.Value && !x.IsDeleted, ct) ||
             !await CanAccessBillingCampusAsync(campusId.Value, ct)))
            return Error<PagedResult<StudentFeeInstallmentPlanDto>>("Campus unavailable.", 404);

        var tenant = _user.TenantId;
        var actor = _user.UserId;
        var grants = GrantedBillingCampuses(db);
        var visible = from plan in db.StudentFeeInstallmentPlans.AsNoTracking()
            join enrollment in db.StudentEnrollments.AsNoTracking()
                on new { plan.TenantId, Id = plan.StudentEnrollmentId }
                equals new { enrollment.TenantId, enrollment.Id }
            join student in db.Students.AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.StudentId }
                equals new { student.TenantId, student.Id }
            where plan.TenantId == tenant && !plan.IsDeleted &&
                !enrollment.IsDeleted && !student.IsDeleted &&
                (!campusId.HasValue || plan.CampusId == campusId.Value) &&
                (_user.IsTenantAdmin || grants.Contains(plan.CampusId))
            select new {
                Plan = plan, enrollment.AcademicBatchId,
                StudentReference = student.PublicId, student.StudentCode,
                StudentName = student.FullName,
                State = db.StudentFeeInstallmentPlanEvents
                    .Where(e => e.TenantId == tenant &&
                        e.StudentFeeInstallmentPlanId == plan.Id && !e.IsDeleted)
                    .Max(e => (int?)e.ActionCode) ?? 1,
                SubmittedByActor = db.StudentFeeInstallmentPlanEvents.Any(e =>
                    e.TenantId == tenant && e.StudentFeeInstallmentPlanId == plan.Id &&
                    !e.IsDeleted && e.ActionCode == 2 && e.ActorUserId == actor)
            };
        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
            visible = visible.Where(x => x.StudentCode.StartsWith(term) ||
                x.StudentName.StartsWith(term));
        if (stateCode.HasValue)
            visible = visible.Where(x => x.State == stateCode.Value);
        if (myApprovalQueue)
            visible = visible.Where(x => x.State == 2 &&
                x.Plan.PreparedByUserId != actor && !x.SubmittedByActor);

        var count = await visible.CountAsync(ct);
        var plans = await visible.OrderByDescending(x => x.Plan.CreatedAt)
            .ThenByDescending(x => x.Plan.Id).Skip(skip).Take(pageSize).ToListAsync(ct);
        var items = plans.Select(x => new StudentFeeInstallmentPlanDto {
            Reference = x.Plan.PublicId, StudentReference = x.StudentReference,
            StudentCode = x.StudentCode, StudentName = x.StudentName,
            CampusId = x.Plan.CampusId, AcademicBatchId = x.AcademicBatchId,
            FeeStructureId = x.Plan.FeeStructureId, StartMonth = x.Plan.StartMonth,
            EndMonth = x.Plan.EndMonth, CurrencyCode = x.Plan.CurrencyCode,
            GrossAmount = x.Plan.GrossAmount, LineCount = x.Plan.LineCount,
            StateCode = x.State, PreparedByUserId = x.Plan.PreparedByUserId,
            CanReviewForApproval = _user.IsTenantAdmin && x.State == 2 &&
                x.Plan.PreparedByUserId != actor && !x.SubmittedByActor,
            RowVersion = Convert.ToBase64String(x.Plan.RowVersion)
        }).ToList();
        return ApiResponse<PagedResult<StudentFeeInstallmentPlanDto>>.SuccessResponse(
            new PagedResult<StudentFeeInstallmentPlanDto> {
                Page = page, PageSize = pageSize, TotalCount = count, Items = items });
    }

    public async Task<ApiResponse<StudentFeeInstallmentPlanDto>> GetInstallmentPlanAsync(
        Guid reference, CancellationToken ct = default)
    {
        if (!CanManage() || _uow is not EduOSDbContext db || db.CurrentTenantId != _user.TenantId)
            return Error<StudentFeeInstallmentPlanDto>("Finance access required.", 403);
        var plan = await db.StudentFeeInstallmentPlans.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.PublicId == reference && !x.IsDeleted, ct);
        if (plan == null || !await CanAccessBillingCampusAsync(plan.CampusId, ct))
            return Error<StudentFeeInstallmentPlanDto>("Plan unavailable.", 404);
        var dto = await InstallmentPlanDtoAsync(db, plan, true, ct);
        return dto == null
            ? Error<StudentFeeInstallmentPlanDto>("Plan provenance unavailable.", 409)
            : ApiResponse<StudentFeeInstallmentPlanDto>.SuccessResponse(dto);
    }

    public async Task<ApiResponse<StudentFeeInstallmentPlanDto>> CreateInstallmentPlanAsync(
        CreateStudentFeeInstallmentPlanDto request, CancellationToken ct = default)
    {
        if (!CanManage() || _uow is not EduOSDbContext db || db.CurrentTenantId != _user.TenantId)
            return Error<StudentFeeInstallmentPlanDto>("Finance access required.", 403);
        if (request == null || request.ClientRequestId == Guid.Empty ||
            request.StudentReference == Guid.Empty || request.AcademicBatchId <= 0 ||
            request.FeeStructureId <= 0 || request.Months is < 1 or > 12 ||
            request.StartMonth.Year is < 2000 or > 2200 || request.StartMonth.Day != 1)
            return Error<StudentFeeInstallmentPlanDto>("Invalid student, structure, month or request key.");
        var lastMonth = request.StartMonth.AddMonths(request.Months - 1);
        var endDate = lastMonth.AddMonths(1).AddDays(-1);
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        try
        {
            var tenant = _user.TenantId;
            var existing = await db.StudentFeeInstallmentPlans.AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenant &&
                    x.ClientRequestId == request.ClientRequestId, ct);
            if (existing != null)
            {
                var prior = await InstallmentPlanDtoAsync(db, existing, true, ct);
                if (prior == null || !await CanAccessBillingCampusAsync(existing.CampusId, ct))
                    return Error<StudentFeeInstallmentPlanDto>("Plan unavailable.", 404);
                return prior.StudentReference == request.StudentReference &&
                    prior.AcademicBatchId == request.AcademicBatchId &&
                    prior.FeeStructureId == request.FeeStructureId &&
                    prior.StartMonth == request.StartMonth && prior.EndMonth == lastMonth
                    ? ApiResponse<StudentFeeInstallmentPlanDto>.SuccessResponse(prior, "Existing plan returned.")
                    : Error<StudentFeeInstallmentPlanDto>("Request key belongs to a different plan.", 409);
            }
            var scope = await (from e in db.StudentEnrollments.AsNoTracking()
                join s in db.Students.AsNoTracking()
                    on new { e.TenantId, Id = e.StudentId } equals new { s.TenantId, s.Id }
                where e.TenantId == tenant && !e.IsDeleted && e.IsCurrent &&
                    e.State == EnrollmentState.Active && e.AcademicBatchId == request.AcademicBatchId &&
                    !s.IsDeleted && s.StatusCode == "Active" && s.PublicId == request.StudentReference
                select new { Enrollment = e, Student = s }).Take(2).ToListAsync(ct);
            if (scope.Count != 1)
                return Error<StudentFeeInstallmentPlanDto>("One active matching enrollment is required.", 409);
            var enrollment = scope[0].Enrollment;
            if (!await CanAccessBillingCampusAsync(enrollment.CampusId, ct))
                return Error<StudentFeeInstallmentPlanDto>("Enrollment unavailable.", 404);
            var structure = await db.FeeStructures.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.FeeStructureId &&
                x.IsActive && !x.IsDeleted, ct);
            var year = await db.AcademicYears.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == enrollment.AcademicYearId && !x.IsDeleted, ct);
            if (structure == null || year == null || structure.CurrencyCode != "BDT" ||
                structure.CampusId != enrollment.CampusId ||
                structure.AcademicYearId != enrollment.AcademicYearId ||
                (structure.AcademicBatchId.HasValue && structure.AcademicBatchId != enrollment.AcademicBatchId) ||
                (structure.AcademicLevelId.HasValue && structure.AcademicLevelId != enrollment.AcademicLevelId) ||
                (structure.AcademicProgramId.HasValue && structure.AcademicProgramId != enrollment.AcademicProgramId) ||
                structure.EffectiveFrom > request.StartMonth ||
                structure.EffectiveTo.HasValue && structure.EffectiveTo.Value < endDate ||
                year.StartDate > request.StartMonth || year.EndDate < endDate)
                return Error<StudentFeeInstallmentPlanDto>("Structure or dates do not match active enrollment.", 409);
            var source = await (from line in db.FeeStructureLines.AsNoTracking()
                join head in db.FeeHeads.AsNoTracking()
                    on new { line.TenantId, Id = line.FeeHeadId } equals new { head.TenantId, head.Id }
                where line.TenantId == tenant && line.FeeStructureId == structure.Id &&
                    !line.IsDeleted && !head.IsDeleted && head.IsActive
                orderby line.FeeHeadId
                select new { line.FeeHeadId, line.Amount, line.DueDayOfMonth, line.Frequency,
                    head.Code, head.Name }).Take(51).ToListAsync(ct);
            if (source.Count is < 1 or > 50 ||
                source.Any(x => x.Frequency != FeeFrequencyType.Monthly ||
                    x.Amount <= 0m || x.Amount > 1000000000m ||
                    x.Amount != decimal.Round(x.Amount, 2) ||
                    x.DueDayOfMonth is null or < 1 or > 31))
                return Error<StudentFeeInstallmentPlanDto>("Plan requires 1–50 positive monthly heads with explicit due days.", 409);
            if (await db.StudentInvoices.AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.StudentEnrollmentId == enrollment.Id &&
                !x.IsDeleted && x.State != InvoiceState.Cancelled &&
                x.InvoiceDate >= request.StartMonth && x.InvoiceDate < lastMonth.AddMonths(1), ct))
                return Error<StudentFeeInstallmentPlanDto>("Existing invoices must be reviewed before planning.", 409);
            if (await db.StudentFeeInstallmentPlans.AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.StudentEnrollmentId == enrollment.Id &&
                !x.IsDeleted && x.StartMonth <= lastMonth && x.EndMonth >= request.StartMonth &&
                !db.StudentFeeInstallmentPlanEvents.Any(e => e.TenantId == tenant &&
                    e.StudentFeeInstallmentPlanId == x.Id && e.ActionCode == 4), ct))
                return Error<StudentFeeInstallmentPlanDto>("An uncancelled plan overlaps these months.", 409);

            var now = _clock.GetUtcNow().UtcDateTime;
            var plan = new StudentFeeInstallmentPlan {
                TenantId = tenant, PublicId = Guid.NewGuid(), ClientRequestId = request.ClientRequestId,
                StudentEnrollmentId = enrollment.Id, FeeStructureId = structure.Id,
                CampusId = enrollment.CampusId, AcademicYearId = enrollment.AcademicYearId,
                StartMonth = request.StartMonth, EndMonth = lastMonth,
                CurrencyCode = structure.CurrencyCode, LineCount = source.Count * request.Months,
                GrossAmount = source.Sum(x => x.Amount) * request.Months,
                PreparedByUserId = _user.UserId, CreatedAt = now, CreatedBy = _user.UserId
            };
            db.StudentFeeInstallmentPlans.Add(plan);
            await db.SaveChangesAsync(ct);
            for (var m = 0; m < request.Months; m++)
            {
                var month = request.StartMonth.AddMonths(m);
                foreach (var line in source)
                    db.StudentFeeInstallmentPlanLines.Add(new StudentFeeInstallmentPlanLine {
                        TenantId = tenant, StudentFeeInstallmentPlanId = plan.Id,
                        FeeHeadId = line.FeeHeadId, MonthStart = month,
                        DueDate = new DateOnly(month.Year, month.Month,
                            Math.Min(line.DueDayOfMonth!.Value, DateTime.DaysInMonth(month.Year, month.Month))),
                        FeeHeadCodeSnapshot = line.Code, FeeHeadNameSnapshot = line.Name,
                        GrossAmount = line.Amount, CreatedAt = now, CreatedBy = _user.UserId
                    });
            }
            await db.SaveChangesAsync(ct);
            // Shared tenant rowversion gate prevents a racing monthly invoice
            // from bypassing this newly created agreement.
            var tenantGate = await db.Tenants.FirstAsync(x => x.Id == tenant && !x.IsDeleted, ct);
            tenantGate.UpdatedAt = now; tenantGate.UpdatedBy = _user.UserId;
            await db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            var dto = await InstallmentPlanDtoAsync(db, plan, true, ct);
            return ApiResponse<StudentFeeInstallmentPlanDto>.SuccessResponse(dto!, "Gross plan draft saved; no invoices issued.");
        }
        catch (SqlException ex) when (ex.Number is 1205 or 3960 or 2601 or 2627)
        {
            if (tx != null) await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return Error<StudentFeeInstallmentPlanDto>("Competing plan or duplicate request; refresh.", 409);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (tx != null) await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return Error<StudentFeeInstallmentPlanDto>("Invoice or plan changed concurrently; refresh.", 409);
        }
        catch (DbUpdateException)
        {
            if (tx != null) await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return Error<StudentFeeInstallmentPlanDto>("Plan conflicts with existing financial evidence.", 409);
        }
    }

    public async Task<ApiResponse<StudentFeeInstallmentPlanDto>> TransitionInstallmentPlanAsync(
        Guid reference, int actionCode, StudentFeeInstallmentTransitionDto request, CancellationToken ct = default)
    {
        if (!CanManage() || _uow is not EduOSDbContext db || db.CurrentTenantId != _user.TenantId)
            return Error<StudentFeeInstallmentPlanDto>("Finance access required.", 403);
        if (reference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            actionCode is < 2 or > 4 || string.IsNullOrWhiteSpace(request.RowVersion) ||
            request.RowVersion.Length > 128 || request.Reason?.Length > 250 ||
            request.Reason?.Any(char.IsControl) == true ||
            (actionCode == 4 && string.IsNullOrWhiteSpace(request.Reason)) ||
            (actionCode == 2 && request.Reason?.StartsWith("Replacement of ", StringComparison.OrdinalIgnoreCase) == true))
            return Error<StudentFeeInstallmentPlanDto>("Invalid transition, version or reason.");
        if (actionCode is 3 or 4 && !_user.IsTenantAdmin)
            return Error<StudentFeeInstallmentPlanDto>("Independent tenant administrator approval required.", 403);
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        try
        {
            var tenant = _user.TenantId;
            var plan = await db.StudentFeeInstallmentPlans.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == reference && !x.IsDeleted, ct);
            if (plan == null || !await CanAccessBillingCampusAsync(plan.CampusId, ct))
                return Error<StudentFeeInstallmentPlanDto>("Plan unavailable.", 404);
            if (Convert.ToBase64String(plan.RowVersion) != request.RowVersion)
                return Error<StudentFeeInstallmentPlanDto>("Plan version changed; refresh.", 409);
            var reason = request.Reason?.Trim();
            var replay = await db.StudentFeeInstallmentPlanEvents.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.ClientRequestId == request.ClientRequestId, ct);
            if (replay != null)
            {
                if (replay.StudentFeeInstallmentPlanId != plan.Id || replay.ActionCode != actionCode ||
                    replay.Reason != reason)
                    return Error<StudentFeeInstallmentPlanDto>("Request key belongs to another transition.", 409);
                var prior = await InstallmentPlanDtoAsync(db, plan, true, ct);
                return ApiResponse<StudentFeeInstallmentPlanDto>.SuccessResponse(prior!, "Existing transition returned.");
            }
            var history = await db.StudentFeeInstallmentPlanEvents.AsNoTracking().Where(x =>
                x.TenantId == tenant && x.StudentFeeInstallmentPlanId == plan.Id && !x.IsDeleted)
                .OrderBy(x => x.ActionCode).Take(4).ToListAsync(ct);
            var state = history.Count == 0 ? 1 : history[^1].ActionCode;
            if (state == 4 || (actionCode == 2 && state != 1) ||
                (actionCode == 3 && state != 2) || (actionCode == 4 && state is < 1 or > 3))
                return Error<StudentFeeInstallmentPlanDto>("Invalid or competing plan transition.", 409);
            if (actionCode == 3 && (plan.PreparedByUserId == _user.UserId ||
                history.Single(x => x.ActionCode == 2).ActorUserId == _user.UserId))
                return Error<StudentFeeInstallmentPlanDto>("Checker must differ from preparer and submitter.", 403);
            if (actionCode == 4 && state > 1 && (plan.PreparedByUserId == _user.UserId ||
                history.Any(x => x.ActorUserId == _user.UserId)))
                return Error<StudentFeeInstallmentPlanDto>("Independent cancellation checker required.", 403);
            if (actionCode == 3)
            {
                var submission = history.Single(x => x.ActionCode == 2);
                const string replacementPrefix = "Replacement of ";
                if (submission.Reason?.StartsWith(replacementPrefix, StringComparison.Ordinal) == true)
                {
                    if (!Guid.TryParseExact(submission.Reason[replacementPrefix.Length..], "D", out var oldReference))
                        return Error<StudentFeeInstallmentPlanDto>("Replacement provenance is invalid.", 409);
                    var old = await db.StudentFeeInstallmentPlans.AsNoTracking().FirstOrDefaultAsync(x =>
                        x.TenantId == tenant && x.PublicId == oldReference && !x.IsDeleted, ct);
                    var cancellationPrefix = "Replaced by " + plan.PublicId.ToString("D") + ": ";
                    if (old == null || old.Id == plan.Id ||
                        old.StudentEnrollmentId != plan.StudentEnrollmentId ||
                        old.CampusId != plan.CampusId || old.AcademicYearId != plan.AcademicYearId ||
                        old.StartMonth != plan.StartMonth || old.EndMonth != plan.EndMonth ||
                        !await db.StudentFeeInstallmentPlanEvents.AsNoTracking().AnyAsync(e =>
                            e.TenantId == tenant && e.StudentFeeInstallmentPlanId == old.Id &&
                            !e.IsDeleted && e.ActionCode == 4 &&
                            e.Reason != null && e.Reason.StartsWith(cancellationPrefix), ct))
                        return Error<StudentFeeInstallmentPlanDto>("Replacement cancellation evidence is missing.", 409);
                }
            }
            if (await db.StudentInvoices.AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.StudentEnrollmentId == plan.StudentEnrollmentId &&
                !x.IsDeleted && x.State != InvoiceState.Cancelled &&
                x.InvoiceDate >= plan.StartMonth && x.InvoiceDate < plan.EndMonth.AddMonths(1), ct))
                return Error<StudentFeeInstallmentPlanDto>("Existing invoice evidence blocks plan transitions.", 409);
            if (actionCode == 3 && !await db.StudentEnrollments.AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.Id == plan.StudentEnrollmentId &&
                x.IsCurrent && !x.IsDeleted && x.State == EnrollmentState.Active &&
                x.CampusId == plan.CampusId && x.AcademicYearId == plan.AcademicYearId, ct))
                return Error<StudentFeeInstallmentPlanDto>("Enrollment changed; approval blocked.", 409);
            if (await InstallmentPlanDtoAsync(db, plan, true, ct) == null)
                return Error<StudentFeeInstallmentPlanDto>(
                    "Plan line count or gross total is inconsistent; approval blocked.", 409);
            db.StudentFeeInstallmentPlanEvents.Add(new StudentFeeInstallmentPlanEvent {
                TenantId = tenant, StudentFeeInstallmentPlanId = plan.Id,
                ClientRequestId = request.ClientRequestId, ActionCode = actionCode,
                ActorUserId = _user.UserId, EventAt = _clock.GetUtcNow().UtcDateTime,
                Reason = reason, CreatedBy = _user.UserId
            });
            await db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            var dto = await InstallmentPlanDtoAsync(db, plan, true, ct);
            return ApiResponse<StudentFeeInstallmentPlanDto>.SuccessResponse(dto!,
                actionCode == 3 ? "Gross plan approved; billing is a separate operation." :
                actionCode == 4 ? "Plan cancelled with immutable history." : "Plan submitted for independent approval.");
        }
        catch (SqlException ex) when (ex.Number is 1205 or 3960 or 2601 or 2627)
        {
            if (tx != null) await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return Error<StudentFeeInstallmentPlanDto>("Competing or duplicate transition; refresh.", 409);
        }
        catch (DbUpdateException)
        {
            if (tx != null) await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return Error<StudentFeeInstallmentPlanDto>("Plan transition conflicts with existing evidence.", 409);
        }
    }

    private async Task<StudentFeeInstallmentPlanDto?> InstallmentPlanDtoAsync(
        EduOSDbContext db, StudentFeeInstallmentPlan plan, bool detail, CancellationToken ct)
    {
        var scope = await (from enrollment in db.StudentEnrollments.AsNoTracking()
            join student in db.Students.AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.StudentId }
                equals new { student.TenantId, student.Id }
            where enrollment.TenantId == _user.TenantId && enrollment.Id == plan.StudentEnrollmentId &&
                !enrollment.IsDeleted && !student.IsDeleted
            select new { enrollment.AcademicBatchId, student.PublicId, student.StudentCode,
                student.FullName }).FirstOrDefaultAsync(ct);
        if (scope == null) return null;
        var events = await db.StudentFeeInstallmentPlanEvents.AsNoTracking()
            .Where(x => x.TenantId == _user.TenantId &&
                x.StudentFeeInstallmentPlanId == plan.Id && !x.IsDeleted)
            .OrderBy(x => x.ActionCode).Take(4)
            .Select(x => new StudentFeeInstallmentPlanEventDto {
                ActionCode = x.ActionCode, ActorUserId = x.ActorUserId,
                EventAt = x.EventAt, Reason = x.Reason }).ToListAsync(ct);
        var lines = detail ? await db.StudentFeeInstallmentPlanLines.AsNoTracking()
            .Where(x => x.TenantId == _user.TenantId &&
                x.StudentFeeInstallmentPlanId == plan.Id && !x.IsDeleted)
            .OrderBy(x => x.MonthStart).ThenBy(x => x.FeeHeadId).Take(601)
            .Select(x => new StudentFeeInstallmentPlanLineDto {
                MonthStart = x.MonthStart, DueDate = x.DueDate,
                FeeHeadId = x.FeeHeadId, FeeHeadCode = x.FeeHeadCodeSnapshot,
                FeeHeadName = x.FeeHeadNameSnapshot, GrossAmount = x.GrossAmount }).ToListAsync(ct)
            : new List<StudentFeeInstallmentPlanLineDto>();
        if (detail && (lines.Count != plan.LineCount || lines.Sum(x => x.GrossAmount) != plan.GrossAmount))
            return null;
        if (detail && lines.Count > 0)
        {
            // Stable, plan-owned month keys provide a durable invoice readback without
            // reinterpreting mutable fee structures or exposing another enrollment.
            var keys = lines.Select(x => x.MonthStart).Distinct()
                .Select(month => InstallmentMonthKey(plan.PublicId, month)).ToArray();
            var invoices = await db.StudentInvoices.AsNoTracking().Where(x =>
                x.TenantId == _user.TenantId && x.StudentEnrollmentId == plan.StudentEnrollmentId &&
                !x.IsDeleted && keys.Contains(x.ClientRequestId))
                .Select(x => new { x.PublicId, x.InvoiceNumber, x.ClientRequestId })
                .Take(13).ToListAsync(ct);
            if (invoices.Count > keys.Length || invoices.Select(x => x.ClientRequestId).Distinct().Count() != invoices.Count)
                return null;
            foreach (var line in lines)
            {
                var key = InstallmentMonthKey(plan.PublicId, line.MonthStart);
                var invoice = invoices.FirstOrDefault(x => x.ClientRequestId == key);
                if (invoice == null) continue;
                line.InvoiceReference = invoice.PublicId;
                line.InvoiceNumber = invoice.InvoiceNumber;
            }
        }
        return new StudentFeeInstallmentPlanDto {
            Reference = plan.PublicId, StudentReference = scope.PublicId,
            StudentCode = scope.StudentCode, StudentName = scope.FullName,
            CampusId = plan.CampusId, AcademicBatchId = scope.AcademicBatchId,
            FeeStructureId = plan.FeeStructureId, StartMonth = plan.StartMonth,
            EndMonth = plan.EndMonth, CurrencyCode = plan.CurrencyCode,
            GrossAmount = plan.GrossAmount, LineCount = plan.LineCount,
            StateCode = events.Count == 0 ? 1 : events[^1].ActionCode,
            CanReviewForApproval = _user.IsTenantAdmin && events.Count > 0 &&
                events[^1].ActionCode == 2 && plan.PreparedByUserId != _user.UserId &&
                !events.Any(x => x.ActionCode == 2 && x.ActorUserId == _user.UserId),
            PreparedByUserId = plan.PreparedByUserId,
            RowVersion = Convert.ToBase64String(plan.RowVersion),
            Lines = lines, Events = detail ? events : []
        };
    }
}
