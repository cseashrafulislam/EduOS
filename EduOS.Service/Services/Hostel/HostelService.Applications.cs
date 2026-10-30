using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Entities.Hostel;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Globalization;
using System.Text;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    private static bool IsOpenApplication(HostelApplicationState state) =>
        state is HostelApplicationState.Submitted or HostelApplicationState.Waitlisted or HostelApplicationState.Offered;

    private async Task<bool> CanViewApplicationAsync(HostelApplication row, CancellationToken ct)
    {
        if (CanManage() && await CanManageCampusAsync(row.CampusId, row.TenantId, ct)) return true;
        return await CanApplyForStudentAsync(row.StudentId, row.TenantId, ct);
    }

    private static ApiResponse<HostelApplicationRowDto> ApplicationError(string message, int status = 400) =>
        ApiResponse<HostelApplicationRowDto>.ErrorResponse(message, status);

    // EF SQL Server translates DateTime.UtcNow inside this query to GETUTCDATE().
    // Acceptance checks expiry against the database clock inside SERIALIZABLE, not a browser timer.
    private Task<bool> HasLiveOfferAsync(HostelApplication app, CancellationToken ct) =>
        _applications.GetQueryable().AsNoTracking().AnyAsync(a => a.TenantId == app.TenantId &&
            a.Id == app.Id && a.State == HostelApplicationState.Offered &&
            a.OfferExpiresAtUtc > DateTime.UtcNow, ct);

    public async Task<ApiResponse<PagedResult<HostelApplicationOptionDto>>> GetMyApplicationOptionsAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanRead()) return Denied<PagedResult<HostelApplicationOptionDto>>();
        if (page < 1 || pageSize is < 1 or > 50 || (long)(page - 1) * pageSize > int.MaxValue)
            return ApiResponse<PagedResult<HostelApplicationOptionDto>>.ErrorResponse("Invalid options page.");
        var tenant = _currentUser.TenantId;
        var authorized = AuthorizedApplicantStudentIds(tenant);
        var query = from enrollment in _enrollments.GetQueryable().AsNoTracking()
            join student in _students.GetQueryable().AsNoTracking() on enrollment.StudentId equals student.Id
            join hostel in _hostels.GetQueryable().AsNoTracking() on enrollment.CampusId equals hostel.CampusId
            join campus in _campuses.GetQueryable().AsNoTracking() on hostel.CampusId equals campus.Id
            where enrollment.TenantId == tenant && student.TenantId == tenant && hostel.TenantId == tenant &&
                campus.TenantId == tenant && campus.IsActive && hostel.IsActive &&
                enrollment.IsCurrent && enrollment.State == EnrollmentState.Active &&
                student.StatusCode == "Active" && authorized.Contains(student.Id) &&
                (hostel.GenderRestriction == null || hostel.GenderRestriction == "" ||
                 hostel.GenderRestriction == student.Gender)
            select new HostelApplicationOptionDto
            {
                EnrollmentReference = enrollment.PublicId, HostelReference = hostel.PublicId,
                CampusName = campus.Name, HostelName = hostel.Name
            };
        var count = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.CampusName).ThenBy(x => x.HostelName)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<HostelApplicationOptionDto>>.SuccessResponse(new PagedResult<HostelApplicationOptionDto>
        {
            Items = items, TotalCount = count, Page = page, PageSize = pageSize
        });
    }

    private static HostelApplicationRowDto ApplicationDto(HostelApplication row, Guid enrollmentReference, DateTime utcNow) => new()
    {
        Id = row.Id, EnrollmentReference = enrollmentReference, CampusId = row.CampusId, HostelId = row.HostelId,
        StudentName = row.StudentNameAtSubmission, StudentCode = row.StudentCodeAtSubmission,
        HostelName = row.HostelNameAtSubmission, StartDate = row.StartDate, EndDate = row.EndDate,
        State = row.State == HostelApplicationState.Offered && row.OfferExpiresAtUtc <= utcNow
            ? HostelApplicationState.Expired : row.State,
        SubmittedAtUtc = row.SubmittedAtUtc, OfferedBedId = row.OfferedBedId,
        OfferExpiresAtUtc = row.OfferExpiresAtUtc, DecisionReason = row.DecisionReason,
        AllocationId = row.AllocationId, RowVersion = Convert.ToBase64String(row.RowVersion)
    };

    private async Task<HostelApplicationRowDto> MapApplicationAsync(HostelApplication row, CancellationToken ct)
    {
        var reference = await _enrollments.GetQueryable().AsNoTracking().Where(e =>
            e.TenantId == row.TenantId && e.Id == row.StudentEnrollmentId)
            .Select(e => e.PublicId).FirstOrDefaultAsync(ct);
        return ApplicationDto(row, reference, _clock.GetUtcNow().UtcDateTime);
    }

    public async Task<ApiResponse<HostelApplicationRowDto>> SubmitApplicationAsync(
        SubmitHostelApplicationRequestDto request, CancellationToken ct = default)
    {
        if (!CanRead()) return Denied<HostelApplicationRowDto>();
        if (request == null || request.ClientRequestId == Guid.Empty || request.StudentEnrollmentReference == Guid.Empty ||
            request.HostelReference == Guid.Empty || request.Note?.Length > 500)
            return ApplicationError("Enrollment, hostel and unique request ID are required.");
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var start = request.StartDate == default ? today : request.StartDate;
        if (!IsValidFiniteBookingPeriod(start, request.EndDate, today))
            return ApplicationError("Stay must start within 365 days and end within 90 inclusive days.");
        var tenant = _currentUser.TenantId;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(e =>
                    e.TenantId == tenant && e.PublicId == request.StudentEnrollmentReference && e.IsCurrent &&
                    e.State == EnrollmentState.Active, token);
                if (enrollment == null) return ApplicationError("Active enrollment not found.", 404);
                var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(s =>
                    s.TenantId == tenant && s.Id == enrollment.StudentId && s.StatusCode == "Active", token);
                if (student == null) return ApplicationError("Active student not found.", 404);
                var self = await CanApplyForStudentAsync(student.Id, tenant, token);
                if (!self && (!CanManage() || !await CanManageCampusAsync(enrollment.CampusId, tenant, token)))
                    return Denied<HostelApplicationRowDto>();
                var hostel = await _hostels.GetQueryable().AsNoTracking().FirstOrDefaultAsync(h =>
                    h.TenantId == tenant && h.PublicId == request.HostelReference && h.IsActive &&
                    h.CampusId == enrollment.CampusId, token);
                if (hostel == null || !await _campuses.GetQueryable().AsNoTracking().AnyAsync(c =>
                    c.TenantId == tenant && c.Id == enrollment.CampusId && c.IsActive, token))
                    return ApplicationError("Active hostel on enrollment campus not found.", 404);
                if (!string.IsNullOrWhiteSpace(hostel.GenderRestriction) &&
                    !string.Equals(hostel.GenderRestriction, student.Gender, StringComparison.OrdinalIgnoreCase))
                    return ApplicationError("Student is not eligible for this hostel.", 409);
                var duplicate = await _applications.GetQueryable().FirstOrDefaultAsync(a =>
                    a.TenantId == tenant && a.ClientRequestId == request.ClientRequestId, token);
                if (duplicate != null)
                {
                    if (duplicate.StudentId != student.Id || duplicate.StudentEnrollmentId != enrollment.Id ||
                        duplicate.HostelId != hostel.Id || duplicate.StartDate != start ||
                        duplicate.EndDate != request.EndDate || duplicate.Note != request.Note?.Trim())
                        return ApplicationError("Request ID already used with different application data.", 409);
                    return ApiResponse<HostelApplicationRowDto>.SuccessResponse(
                        ApplicationDto(duplicate, enrollment.PublicId, _clock.GetUtcNow().UtcDateTime), "Application already received.");
                }
                var end = request.EndDate ?? DateOnly.MaxValue;
                if (await _allocations.GetQueryable().AsNoTracking().AnyAsync(a => a.TenantId == tenant &&
                    a.StudentId == student.Id && a.State == HostelAllocationState.Active &&
                    a.StartDate <= end && (!a.EndDate.HasValue || a.EndDate.Value >= start), token))
                    return ApplicationError("Student already has an overlapping hostel stay.", 409);
                if (await _applications.GetQueryable().AsNoTracking().AnyAsync(a => a.TenantId == tenant &&
                    a.StudentId == student.Id && (a.State == HostelApplicationState.Submitted ||
                    a.State == HostelApplicationState.Waitlisted || a.State == HostelApplicationState.Offered) &&
                    a.StartDate <= end && (!a.EndDate.HasValue || a.EndDate.Value >= start), token))
                    return ApplicationError("Student already has an open overlapping application.", 409);
                var now = _clock.GetUtcNow().UtcDateTime;
                var application = new HostelApplication
                {
                    TenantId = tenant, ClientRequestId = request.ClientRequestId,
                    StudentId = student.Id, StudentEnrollmentId = enrollment.Id, CampusId = enrollment.CampusId,
                    HostelId = hostel.Id, StartDate = start, EndDate = request.EndDate,
                    SubmittedAtUtc = now, SubmittedByUserId = _currentUser.UserId,
                    StudentNameAtSubmission = student.FullName, StudentCodeAtSubmission = student.StudentCode,
                    HostelNameAtSubmission = hostel.Name, Note = request.Note?.Trim(),
                    CreatedAt = now, CreatedBy = _currentUser.UserId
                };
                await _applications.AddAsync(application);
                await _unitOfWork.SaveChangesAsync(token);
                await RecordApplicationEventAsync(application, null, HostelApplicationState.Submitted, null, now, token);
                await _unitOfWork.SaveChangesAsync(token);
                return ApiResponse<HostelApplicationRowDto>.SuccessResponse(
                    ApplicationDto(application, enrollment.PublicId, now), "Application submitted.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Hostel application duplicate or constraint conflict for tenant {TenantId}", tenant);
            return ApplicationError("Concurrent or duplicate hostel application. Reload and retry.", 409);
        }
        catch (Exception ex) when (HasHostelSqlSerializationConflict(ex) || ex is System.Transactions.TransactionAbortedException)
        {
            _logger.LogWarning(ex, "Hostel application transaction conflict for tenant {TenantId}", tenant);
            return ApplicationError("Concurrent application. Reload and retry.", 409);
        }
    }

    private async Task RecordApplicationEventAsync(HostelApplication application, HostelApplicationState? previous,
        HostelApplicationState next, string? reason, DateTime now, CancellationToken ct)
    {
        await _applicationEvents.AddAsync(new HostelApplicationEvent
        {
            TenantId = application.TenantId, HostelApplicationId = application.Id, FromState = previous,
            ToState = next, Reason = reason, ActorUserId = _currentUser.UserId,
            OccurredAtUtc = now, CreatedAt = now, CreatedBy = _currentUser.UserId
        });
    }

    private async Task<ApiResponse<PagedResult<HostelApplicationRowDto>>> ReadApplicationsAsync(
        IQueryable<HostelApplication> query, int page, int pageSize, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var rows = await (from a in query
            join e in _enrollments.GetQueryable().AsNoTracking() on a.StudentEnrollmentId equals e.Id
            where a.TenantId == e.TenantId
            orderby a.SubmittedAtUtc, a.Id
            select new { Application = a, EnrollmentReference = e.PublicId })
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var now = _clock.GetUtcNow().UtcDateTime;
        return ApiResponse<PagedResult<HostelApplicationRowDto>>.SuccessResponse(new PagedResult<HostelApplicationRowDto>
        {
            Items = rows.Select(x => ApplicationDto(x.Application, x.EnrollmentReference, now)).ToList(),
            TotalCount = total, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<PagedResult<HostelApplicationRowDto>>> GetApplicationQueueAsync(
        long? campusId, HostelApplicationState? state, int page, int pageSize, string? search, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<PagedResult<HostelApplicationRowDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            campusId is <= 0 || search?.Trim().Length > 100 || (state.HasValue && !Enum.IsDefined(state.Value)))
            return ApiResponse<PagedResult<HostelApplicationRowDto>>.ErrorResponse("Invalid application queue filter.");
        var tenant = _currentUser.TenantId;
        var query = _applications.GetQueryable().AsNoTracking().Where(a => a.TenantId == tenant &&
            (!campusId.HasValue || a.CampusId == campusId.Value));
        if (!_currentUser.IsTenantAdmin)
        {
            var allowed = AllowedCampusIds(tenant);
            query = query.Where(a => allowed.Contains(a.CampusId));
        }
        if (state.HasValue)
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            if (state.Value == HostelApplicationState.Expired)
                query = query.Where(a => a.State == state.Value || (a.State == HostelApplicationState.Offered && a.OfferExpiresAtUtc <= now));
            else if (state.Value == HostelApplicationState.Offered)
                query = query.Where(a => a.State == state.Value && a.OfferExpiresAtUtc > now);
            else query = query.Where(a => a.State == state.Value);
        }
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(a => a.StudentNameAtSubmission.Contains(term) ||
                a.StudentCodeAtSubmission.Contains(term) || a.HostelNameAtSubmission.Contains(term));
        return await ReadApplicationsAsync(query, page, pageSize, ct);
    }

    public async Task<ApiResponse<PagedResult<HostelApplicationRowDto>>> GetMyApplicationsAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanRead()) return Denied<PagedResult<HostelApplicationRowDto>>();
        if (page < 1 || pageSize is < 1 or > 50 || (long)(page - 1) * pageSize > int.MaxValue)
            return ApiResponse<PagedResult<HostelApplicationRowDto>>.ErrorResponse("Invalid application history page.");
        var tenant = _currentUser.TenantId;
        var mine = AuthorizedApplicantStudentIds(tenant);
        return await ReadApplicationsAsync(_applications.GetQueryable().AsNoTracking().Where(a =>
            a.TenantId == tenant && mine.Contains(a.StudentId)), page, pageSize, ct);
    }

    public async Task<ApiResponse<IReadOnlyList<HostelApplicationEventDto>>> GetApplicationEventsAsync(
        long id, CancellationToken ct = default)
    {
        if (!CanRead()) return Denied<IReadOnlyList<HostelApplicationEventDto>>();
        var tenant = _currentUser.TenantId;
        var app = await _applications.GetQueryable().AsNoTracking().FirstOrDefaultAsync(a =>
            a.TenantId == tenant && a.Id == id, ct);
        if (app == null) return ApiResponse<IReadOnlyList<HostelApplicationEventDto>>.ErrorResponse("Application not found.", 404);
        if (!await CanViewApplicationAsync(app, ct)) return Denied<IReadOnlyList<HostelApplicationEventDto>>();
        var entries = await _applicationEvents.GetQueryable().AsNoTracking().Where(e =>
            e.TenantId == tenant && e.HostelApplicationId == id)
            .OrderByDescending(e => e.Id).Take(100)
            .Select(e => new HostelApplicationEventDto
            {
                FromState = e.FromState, ToState = e.ToState,
                OccurredAtUtc = e.OccurredAtUtc, Reason = e.Reason
            }).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<HostelApplicationEventDto>>.SuccessResponse(entries);
    }

    public async Task<ApiResponse<HostelApplicationRowDto>> TransitionApplicationAsync(
        long id, string action, HostelApplicationDecisionRequestDto request, CancellationToken ct = default)
    {
        if (!CanRead()) return Denied<HostelApplicationRowDto>();
        if (id <= 0 || request == null || !TryDecodeVersion(request.RowVersion, out var version) ||
            !new[] { "waitlist", "offer", "reject", "withdraw" }.Contains(action))
            return ApplicationError("Invalid application transition.");
        if (action == "reject" && string.IsNullOrWhiteSpace(request.Reason))
            return ApplicationError("A rejection reason is required.");
        if (request.Reason?.Trim().Length > 500 || (action == "offer" &&
            (request.OfferedBedId is null or <= 0 || request.OfferHours is < 1 or > 168 or null)))
            return ApplicationError("Invalid decision reason, bed or offer expiry.");
        var tenant = _currentUser.TenantId;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var app = await _applications.GetQueryable().FirstOrDefaultAsync(a =>
                    a.TenantId == tenant && a.Id == id, token);
                if (app == null) return ApplicationError("Application not found.", 404);
                var manager = CanManage() && await CanManageCampusAsync(app.CampusId, tenant, token);
                var studentOwns = await CanApplyForStudentAsync(app.StudentId, tenant, token);
                if ((action == "withdraw" && !studentOwns) || (action != "withdraw" && !manager))
                    return Denied<HostelApplicationRowDto>();
                if (!app.RowVersion.AsSpan().SequenceEqual(version))
                    return ApplicationError("Application changed. Reload and retry.", 409);
                if (!IsOpenApplication(app.State))
                    return ApplicationError("This application can no longer be changed.", 409);
                var now = _clock.GetUtcNow().UtcDateTime;
                if (app.State == HostelApplicationState.Offered && !await HasLiveOfferAsync(app, token))
                {
                    await ApplyApplicationStateAsync(app, HostelApplicationState.Expired, "Offer expired.", now, token);
                    return ApplicationError("Offer expired. Request a new application.", 409);
                }
                HostelApplicationState target;
                if (action == "withdraw") target = HostelApplicationState.Withdrawn;
                else if (action == "reject") target = HostelApplicationState.Rejected;
                else if (action == "waitlist")
                {
                    if (app.State != HostelApplicationState.Submitted)
                        return ApplicationError("Only submitted applications can enter waitlist.", 409);
                    target = HostelApplicationState.Waitlisted;
                }
                else
                {
                    if (app.State is not (HostelApplicationState.Submitted or HostelApplicationState.Waitlisted))
                        return ApplicationError("Only queued applications can receive an offer.", 409);
                    // No bypass of an older waitlisted request for the same hostel.
                    if (await _applications.GetQueryable().AsNoTracking().AnyAsync(a =>
                        a.TenantId == tenant && a.HostelId == app.HostelId && a.Id != app.Id &&
                        a.State == HostelApplicationState.Waitlisted &&
                        (a.SubmittedAtUtc < app.SubmittedAtUtc ||
                         (a.SubmittedAtUtc == app.SubmittedAtUtc && a.Id < app.Id)), token))
                        return ApplicationError("Older waitlisted applications must be processed first.", 409);
                    var selected = await (from bed in _beds.GetQueryable().AsNoTracking()
                        join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
                        where bed.TenantId == tenant && room.TenantId == tenant &&
                            bed.Id == request.OfferedBedId && bed.IsActive && room.IsActive &&
                            room.HostelId == app.HostelId
                        select bed.Id).FirstOrDefaultAsync(token);
                    if (selected == 0) return ApplicationError("Active bed in the requested hostel not found.", 409);
                    app.OfferedBedId = selected;
                    app.OfferExpiresAtUtc = now.AddHours(request.OfferHours!.Value);
                    target = HostelApplicationState.Offered;
                }
                await ApplyApplicationStateAsync(app, target, request.Reason?.Trim(), now, token);
                return ApiResponse<HostelApplicationRowDto>.SuccessResponse(await MapApplicationAsync(app, token),
                    target == HostelApplicationState.Offered ? "Non-binding bed offer sent; capacity will be rechecked on acceptance." : "Application updated.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return ApplicationError("Application changed. Reload and retry.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Hostel application transition conflict tenant {TenantId}", tenant);
            return ApplicationError("Application transition conflict.", 409);
        }
        catch (Exception ex) when (HasHostelSqlSerializationConflict(ex) || ex is System.Transactions.TransactionAbortedException)
        {
            _logger.LogWarning(ex, "Hostel application transition SQL conflict tenant {TenantId}", tenant);
            return ApplicationError("Concurrent hostel application transition.", 409);
        }
    }

    private async Task ApplyApplicationStateAsync(HostelApplication app, HostelApplicationState state,
        string? reason, DateTime now, CancellationToken ct)
    {
        var previous = app.State;
        app.State = state; app.UpdatedAt = now; app.UpdatedBy = _currentUser.UserId;
        app.DecidedAtUtc = now; app.DecidedByUserId = _currentUser.UserId; app.DecisionReason = reason;
        await RecordApplicationEventAsync(app, previous, state, reason, now, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<ApiResponse<HostelApplicationRowDto>> AcceptApplicationOfferAsync(
        long id, HostelApplicationDecisionRequestDto request, CancellationToken ct = default)
    {
        if (!CanRead()) return Denied<HostelApplicationRowDto>();
        if (id <= 0 || request == null || !TryDecodeVersion(request.RowVersion, out var version))
            return ApplicationError("Application and version are required.");
        var tenant = _currentUser.TenantId;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var app = await _applications.GetQueryable().FirstOrDefaultAsync(a =>
                    a.TenantId == tenant && a.Id == id, token);
                if (app == null) return ApplicationError("Application not found.", 404);
                // A warden may review an application, but only the student or an explicitly
                // delegated guardian can accept an offer and create a binding allocation.
                if (!await CanApplyForStudentAsync(app.StudentId, tenant, token)) return Denied<HostelApplicationRowDto>();
                if (app.State == HostelApplicationState.Accepted && app.AllocationId.HasValue)
                {
                    // Already-accepted replay only discloses the caller's authorized application.
                    return ApiResponse<HostelApplicationRowDto>.SuccessResponse(await MapApplicationAsync(app, token), "Already accepted.");
                }
                if (!app.RowVersion.AsSpan().SequenceEqual(version))
                    return ApplicationError("Application changed. Reload and retry.", 409);
                if (app.State != HostelApplicationState.Offered || !app.OfferedBedId.HasValue)
                    return ApplicationError("There is no active offer to accept.", 409);
                var now = _clock.GetUtcNow().UtcDateTime;
                if (!await HasLiveOfferAsync(app, token))
                {
                    await ApplyApplicationStateAsync(app, HostelApplicationState.Expired, "Offer expired.", now, token);
                    return ApplicationError("Offer expired; no bed was allocated.", 409);
                }
                var reference = await _enrollments.GetQueryable().AsNoTracking().Where(e =>
                    e.TenantId == tenant && e.Id == app.StudentEnrollmentId &&
                    e.StudentId == app.StudentId && e.CampusId == app.CampusId)
                    .Select(e => e.PublicId).FirstOrDefaultAsync(token);
                if (reference == Guid.Empty) return ApplicationError("Enrollment is no longer eligible.", 409);
                var allocationResult = await AllocateValidatedAsync(new AllocateStudentHostelRequestDto
                {
                    ClientRequestId = app.AllocationRequestId, StudentEnrollmentReference = reference,
                    HostelBedId = app.OfferedBedId.Value, StartDate = app.StartDate, EndDate = app.EndDate
                }, true, token);
                if (!allocationResult.Success || allocationResult.Data == null)
                    return ApplicationError(allocationResult.Message ?? "Bed is no longer available.", allocationResult.StatusCode);
                app.AllocationId = allocationResult.Data.Id;
                await ApplyApplicationStateAsync(app, HostelApplicationState.Accepted, "Offer accepted.", now, token);
                return ApiResponse<HostelApplicationRowDto>.SuccessResponse(await MapApplicationAsync(app, token),
                    "Bed allocated. Financial charges, if any, follow Finance approval.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return ApplicationError("Application changed. Reload and retry.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Hostel offer acceptance conflict tenant {TenantId}", tenant);
            return ApplicationError("Concurrent bed allocation. Reload availability.", 409);
        }
        catch (Exception ex) when (HasHostelSqlSerializationConflict(ex) || ex is System.Transactions.TransactionAbortedException)
        {
            _logger.LogWarning(ex, "Hostel offer acceptance SQL conflict tenant {TenantId}", tenant);
            return ApplicationError("Concurrent bed allocation. Reload availability.", 409);
        }
    }

    public async Task<ApiResponse<string>> ExportApplicationQueueCsvAsync(
        long? campusId, HostelApplicationState? state, string? search, int page, CancellationToken ct = default)
    {
        var result = await GetApplicationQueueAsync(campusId, state, page, 100, search, ct);
        if (!result.Success || result.Data == null)
            return ApiResponse<string>.ErrorResponse(result.Message ?? "Queue unavailable.", result.StatusCode);
        var csv = new StringBuilder("Application,Student Code,Student,Hostel,Start,End,Status,Submitted UTC,Offer Expiry UTC\r\n");
        foreach (var row in result.Data.Items)
            csv.Append(row.Id).Append(',').Append(CsvCell(row.StudentCode)).Append(',')
                .Append(CsvCell(row.StudentName)).Append(',').Append(CsvCell(row.HostelName)).Append(',')
                .Append(row.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "").Append(',')
                .Append(row.State).Append(',').Append(row.SubmittedAtUtc.ToString("O", CultureInfo.InvariantCulture))
                .Append(',').Append(row.OfferExpiresAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "")
                .Append("\r\n");
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }
}
