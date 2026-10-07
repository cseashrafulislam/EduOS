using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Transactions;

namespace EduOS.Service.Services.Academic;

public sealed class AcademicCalendarService : IAcademicCalendarService
{
    private readonly IGenericRepository<AcademicCalendarPolicy> _policies;
    private readonly IGenericRepository<AcademicCalendarEvent> _events;
    private readonly IGenericRepository<AcademicYear> _years;
    private readonly IGenericRepository<AcademicTerm> _terms;
    private readonly IGenericRepository<Campus> _campuses;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _clock;
    private readonly ILogger<AcademicCalendarService> _logger;

    public AcademicCalendarService(
        IGenericRepository<AcademicCalendarPolicy> policies,
        IGenericRepository<AcademicCalendarEvent> events,
        IGenericRepository<AcademicYear> years,
        IGenericRepository<AcademicTerm> terms,
        IGenericRepository<Campus> campuses,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        TimeProvider clock,
        ILogger<AcademicCalendarService> logger)
    {
        _policies = policies;
        _events = events;
        _years = years;
        _terms = terms;
        _campuses = campuses;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ApiResponse<AcademicCalendarPolicyDto>> GetPolicyAsync(long academicYearId, long? campusId, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<AcademicCalendarPolicyDto>();
        var scope = await ResolveScopeAsync(academicYearId, null, campusId, cancellationToken);
        if (!scope.Success) return Error<AcademicCalendarPolicyDto>(scope.Error!, scope.StatusCode);

        var policy = await EffectivePolicyQuery(campusId).AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (policy == null) return Error<AcademicCalendarPolicyDto>("Academic calendar policy is not configured.", 404);

        return ApiResponse<AcademicCalendarPolicyDto>.SuccessResponse(await MapPolicyAsync(policy, cancellationToken));
    }

    public Task<ApiResponse<AcademicCalendarPolicyDto>> SavePolicyAsync(SaveAcademicCalendarPolicyDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<AcademicCalendarPolicyDto>());
        if (request == null || request.AcademicYearId <= 0 || (request.CampusId.HasValue && request.CampusId.Value <= 0))
            return Task.FromResult(Error<AcademicCalendarPolicyDto>("Academic year is required; campus, when supplied, must be positive."));

        var days = request.WeekendDays?.Distinct().OrderBy(x => x).ToList() ?? [];
        if (days.Count is < 1 or > 2 || days.Any(x => !Enum.IsDefined(typeof(DayOfWeek), x)))
            return Task.FromResult(Error<AcademicCalendarPolicyDto>("The final calendar policy supports one or two distinct weekend days."));

        return ExecuteWriteAsync("save academic calendar policy", async () =>
        {
            var scope = await ResolveScopeAsync(request.AcademicYearId, null, request.CampusId, cancellationToken);
            if (!scope.Success) return Error<AcademicCalendarPolicyDto>(scope.Error!, scope.StatusCode);

            var tenantId = _currentUser.TenantId;
            var existing = await _policies.GetQueryable()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.CampusId == request.CampusId && x.IsActive, cancellationToken);

            if (existing != null)
            {
                if (existing.WeekendDay1 == days[0] && existing.WeekendDay2 == (days.Count > 1 ? days[1] : null))
                    return ApiResponse<AcademicCalendarPolicyDto>.SuccessResponse(await MapPolicyAsync(existing, cancellationToken), "Academic calendar policy already matches the request.");

                if (!TryDecodeRowVersion(request.RowVersion, out var rowVersion) || !VersionsMatch(existing.RowVersion, rowVersion))
                    return Error<AcademicCalendarPolicyDto>("Academic calendar policy changed. Reload and try again.", 409);

                existing.WeekendDay1 = days[0];
                existing.WeekendDay2 = days.Count > 1 ? days[1] : null;
                existing.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                existing.UpdatedBy = _currentUser.UserId;
                _policies.Update(existing);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return ApiResponse<AcademicCalendarPolicyDto>.SuccessResponse(await MapPolicyAsync(existing, cancellationToken), "Academic calendar policy updated.");
            }

            var row = new AcademicCalendarPolicy
            {
                TenantId = tenantId,
                CampusId = request.CampusId,
                WeekendDay1 = days[0],
                WeekendDay2 = days.Count > 1 ? days[1] : null,
                IsActive = true,
                CreatedAt = _clock.GetUtcNow().UtcDateTime,
                CreatedBy = _currentUser.UserId
            };
            await _policies.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Created(await MapPolicyAsync(row, cancellationToken), "Academic calendar policy created.");
        });
    }

    public async Task<ApiResponse<IReadOnlyList<AcademicCalendarEventDto>>> GetEventsAsync(
        long academicYearId, long? campusId, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<IReadOnlyList<AcademicCalendarEventDto>>();
        var range = NormalizeRange(fromDate, toDate);
        if (!range.Success) return Error<IReadOnlyList<AcademicCalendarEventDto>>(range.Error!);
        var scope = await ResolveScopeAsync(academicYearId, null, campusId, cancellationToken);
        if (!scope.Success) return Error<IReadOnlyList<AcademicCalendarEventDto>>(scope.Error!, scope.StatusCode);
        if (!Within(range.From, range.To, scope.Year!))
            return Error<IReadOnlyList<AcademicCalendarEventDto>>("Calendar range is outside the academic year.", 409);

        var query = _events.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId &&
                        x.AcademicYearId == academicYearId &&
                        x.StartDate <= range.To &&
                        x.EndDate >= range.From &&
                        (!x.CampusId.HasValue || x.CampusId == campusId));
        if (!CanManage()) query = query.Where(x => x.IsPublicVisible);

        var rows = await query.OrderBy(x => x.StartDate).ThenBy(x => x.Title).Take(1000).ToListAsync(cancellationToken);
        IReadOnlyList<AcademicCalendarEventDto> result = await MapEventsAsync(rows, cancellationToken);
        return ApiResponse<IReadOnlyList<AcademicCalendarEventDto>>.SuccessResponse(result);
    }

    public Task<ApiResponse<AcademicCalendarEventDto>> CreateEventAsync(CreateAcademicCalendarEventDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<AcademicCalendarEventDto>());
        if (request == null || request.ClientRequestId == Guid.Empty || request.AcademicYearId <= 0 ||
            (request.CampusId.HasValue && request.CampusId.Value <= 0))
            return Task.FromResult(Error<AcademicCalendarEventDto>("A valid request ID and academic year are required."));
        if (!TryMapEventType(request.EventType, out var eventType))
            return Task.FromResult(Error<AcademicCalendarEventDto>("Calendar event type is invalid."));
        var inputError = ValidateEvent(request.Title, request.StartDate, request.EndDate);
        if (inputError != null) return Task.FromResult(Error<AcademicCalendarEventDto>(inputError));

        return ExecuteWriteAsync("create academic calendar event", async () =>
        {
            var scope = await ResolveScopeAsync(request.AcademicYearId, request.AcademicTermId, request.CampusId, cancellationToken);
            if (!scope.Success) return Error<AcademicCalendarEventDto>(scope.Error!, scope.StatusCode);

            var start = DateOnly.FromDateTime(request.StartDate.Date);
            var end = DateOnly.FromDateTime(request.EndDate.Date);
            var dateError = ValidateDates(start, end, scope.Year!, scope.Term);
            if (dateError != null) return Error<AcademicCalendarEventDto>(dateError, 409);

            var title = request.Title.Trim();
            var holiday = request.IsHoliday || eventType == CalendarEventKind.Holiday;
            var tenantId = _currentUser.TenantId;
            var existing = await _events.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId &&
                                          x.AcademicYearId == request.AcademicYearId &&
                                          x.AcademicTermId == request.AcademicTermId &&
                                          x.CampusId == request.CampusId &&
                                          x.EventType == eventType &&
                                          x.Title == title &&
                                          x.StartDate == start &&
                                          x.EndDate == end, cancellationToken);
            if (existing != null)
            {
                if (existing.Description != Trim(request.Description) ||
                    existing.IsHoliday != holiday ||
                    existing.IsPublicVisible != request.IsPublicVisible)
                    return Error<AcademicCalendarEventDto>("An academic calendar event already uses this natural key with different details.", 409);

                return ApiResponse<AcademicCalendarEventDto>.SuccessResponse(
                    (await MapEventsAsync([existing], cancellationToken))[0],
                    "Academic calendar event already exists.");
            }

            var row = new AcademicCalendarEvent
            {
                TenantId = tenantId,
                CampusId = request.CampusId,
                AcademicYearId = request.AcademicYearId,
                AcademicTermId = request.AcademicTermId,
                EventType = eventType,
                Title = title,
                Description = Trim(request.Description),
                StartDate = start,
                EndDate = end,
                IsHoliday = holiday,
                IsPublicVisible = request.IsPublicVisible,
                CreatedAt = _clock.GetUtcNow().UtcDateTime,
                CreatedBy = _currentUser.UserId
            };
            await _events.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Created((await MapEventsAsync([row], cancellationToken))[0], "Academic calendar event created.");
        });
    }

    public Task<ApiResponse<AcademicCalendarEventDto>> UpdateEventAsync(
        long id, UpdateAcademicCalendarEventDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<AcademicCalendarEventDto>());
        if (id <= 0 || request == null || !TryDecodeRowVersion(request.RowVersion, out var rowVersion))
            return Task.FromResult(Error<AcademicCalendarEventDto>("A valid event and row version are required."));
        if (!request.IsActive)
            return Task.FromResult(Error<AcademicCalendarEventDto>("Calendar-event deactivation is not supported by the final domain model.", 409));
        if (!TryMapEventType(request.EventType, out var eventType))
            return Task.FromResult(Error<AcademicCalendarEventDto>("Calendar event type is invalid."));
        var inputError = ValidateEvent(request.Title, request.StartDate, request.EndDate);
        if (inputError != null) return Task.FromResult(Error<AcademicCalendarEventDto>(inputError));

        return ExecuteWriteAsync("update academic calendar event", async () =>
        {
            var row = await _events.GetQueryable()
                .FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == id, cancellationToken);
            if (row == null) return Error<AcademicCalendarEventDto>("Academic calendar event not found.", 404);
            if (!VersionsMatch(row.RowVersion, rowVersion))
                return Error<AcademicCalendarEventDto>("Academic calendar event changed. Reload and try again.", 409);

            var scope = await ResolveScopeAsync(row.AcademicYearId, row.AcademicTermId, row.CampusId, cancellationToken);
            if (!scope.Success) return Error<AcademicCalendarEventDto>(scope.Error!, scope.StatusCode);
            var start = DateOnly.FromDateTime(request.StartDate.Date);
            var end = DateOnly.FromDateTime(request.EndDate.Date);
            var dateError = ValidateDates(start, end, scope.Year!, scope.Term);
            if (dateError != null) return Error<AcademicCalendarEventDto>(dateError, 409);

            var title = request.Title.Trim();
            if (await _events.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == _currentUser.TenantId &&
                    x.Id != row.Id &&
                    x.AcademicYearId == row.AcademicYearId &&
                    x.AcademicTermId == row.AcademicTermId &&
                    x.CampusId == row.CampusId &&
                    x.EventType == eventType &&
                    x.Title == title &&
                    x.StartDate == start &&
                    x.EndDate == end, cancellationToken))
                return Error<AcademicCalendarEventDto>("Another academic calendar event already uses this natural key.", 409);

            row.EventType = eventType;
            row.Title = title;
            row.Description = Trim(request.Description);
            row.StartDate = start;
            row.EndDate = end;
            row.IsHoliday = request.IsHoliday || eventType == CalendarEventKind.Holiday;
            row.IsPublicVisible = request.IsPublicVisible;
            row.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
            row.UpdatedBy = _currentUser.UserId;
            _events.Update(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ApiResponse<AcademicCalendarEventDto>.SuccessResponse(
                (await MapEventsAsync([row], cancellationToken))[0],
                "Academic calendar event updated.");
        });
    }

    public async Task<ApiResponse<IReadOnlyList<AcademicWorkingDayDto>>> GetWorkingDaysAsync(
        long academicYearId, long? campusId, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<IReadOnlyList<AcademicWorkingDayDto>>();
        var range = NormalizeRange(fromDate, toDate);
        if (!range.Success) return Error<IReadOnlyList<AcademicWorkingDayDto>>(range.Error!);
        var scope = await ResolveScopeAsync(academicYearId, null, campusId, cancellationToken);
        if (!scope.Success) return Error<IReadOnlyList<AcademicWorkingDayDto>>(scope.Error!, scope.StatusCode);
        if (!Within(range.From, range.To, scope.Year!))
            return Error<IReadOnlyList<AcademicWorkingDayDto>>("Working-day range is outside the academic year.", 409);

        var policy = await EffectivePolicyQuery(campusId).AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (policy == null)
            return Error<IReadOnlyList<AcademicWorkingDayDto>>("Configure an academic calendar policy before calculating working days.", 409);

        var holidays = await _events.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId &&
                        x.AcademicYearId == academicYearId &&
                        x.IsHoliday &&
                        x.StartDate <= range.To &&
                        x.EndDate >= range.From &&
                        (!x.CampusId.HasValue || x.CampusId == campusId))
            .Select(x => new { x.Title, x.StartDate, x.EndDate, x.IsPublicVisible })
            .Take(1000).ToListAsync(cancellationToken);

        var canManage = CanManage();
        var rows = new List<AcademicWorkingDayDto>();
        for (var date = range.From; date <= range.To; date = date.AddDays(1))
        {
            var weekend = date.DayOfWeek == policy.WeekendDay1 || (policy.WeekendDay2.HasValue && date.DayOfWeek == policy.WeekendDay2.Value);
            IReadOnlyList<string> names = holidays
                .Where(x => x.StartDate <= date && x.EndDate >= date)
                .Select(x => canManage || x.IsPublicVisible ? x.Title : "Holiday")
                .Distinct().OrderBy(x => x).ToList();
            rows.Add(new AcademicWorkingDayDto
            {
                Date = date.ToDateTime(TimeOnly.MinValue),
                IsWeekend = weekend,
                HolidayNames = names,
                IsWorkingDay = !weekend && names.Count == 0
            });
        }

        return ApiResponse<IReadOnlyList<AcademicWorkingDayDto>>.SuccessResponse(rows);
    }

    private IQueryable<AcademicCalendarPolicy> EffectivePolicyQuery(long? campusId) =>
        _policies.GetQueryable()
            .Where(x => x.TenantId == _currentUser.TenantId && x.IsActive && (x.CampusId == campusId || x.CampusId == null))
            .OrderByDescending(x => x.CampusId == campusId)
            .ThenByDescending(x => x.Id);

    private async Task<CalendarScope> ResolveScopeAsync(long academicYearId, long? academicTermId, long? campusId, CancellationToken cancellationToken)
    {
        var tenantId = _currentUser.TenantId;
        var year = await _years.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == academicYearId && x.IsActive, cancellationToken);
        if (year == null) return CalendarScope.Fail("Academic year not found.", 404);

        Campus? campus = null;
        if (campusId.HasValue)
        {
            campus = await _campuses.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == campusId.Value && x.IsActive, cancellationToken);
            if (campus == null) return CalendarScope.Fail("Campus not found.", 404);
            if (year.CampusId.HasValue && year.CampusId.Value != campus.Id)
                return CalendarScope.Fail("Academic year belongs to a different campus.", 409);
        }

        AcademicTerm? term = null;
        if (academicTermId.HasValue)
        {
            term = await _terms.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId &&
                                          x.Id == academicTermId.Value &&
                                          x.AcademicYearId == year.Id &&
                                          x.IsActive, cancellationToken);
            if (term == null) return CalendarScope.Fail("Academic term not found in this academic year.", 404);
        }

        return CalendarScope.Ok(year, term, campus);
    }

    private async Task<AcademicCalendarPolicyDto> MapPolicyAsync(AcademicCalendarPolicy x, CancellationToken cancellationToken)
    {
        string? campusName = null;
        if (x.CampusId.HasValue)
            campusName = await _campuses.GetQueryable().AsNoTracking()
                .Where(c => c.TenantId == x.TenantId && c.Id == x.CampusId.Value)
                .Select(c => c.Name).FirstOrDefaultAsync(cancellationToken);

        return new AcademicCalendarPolicyDto
        {
            Id = x.Id,
            CampusId = x.CampusId,
            CampusName = campusName,
            WeekendDay1 = x.WeekendDay1,
            WeekendDay2 = x.WeekendDay2,
            IsActive = x.IsActive,
            RowVersion = Convert.ToBase64String(x.RowVersion)
        };
    }

    private async Task<List<AcademicCalendarEventDto>> MapEventsAsync(IReadOnlyList<AcademicCalendarEvent> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return [];
        var campusIds = rows.Where(x => x.CampusId.HasValue).Select(x => x.CampusId!.Value).Distinct().ToArray();
        var campuses = campusIds.Length == 0
            ? new Dictionary<long, string>()
            : await _campuses.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == _currentUser.TenantId && campusIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        return rows.Select(x => new AcademicCalendarEventDto
        {
            Id = x.Id,
            CampusId = x.CampusId,
            CampusName = x.CampusId.HasValue && campuses.TryGetValue(x.CampusId.Value, out var name) ? name : null,
            AcademicYearId = x.AcademicYearId,
            AcademicTermId = x.AcademicTermId,
            EventType = x.EventType,
            Title = x.Title,
            Description = x.Description,
            StartDate = x.StartDate,
            EndDate = x.EndDate,
            IsHoliday = x.IsHoliday,
            IsPublicVisible = x.IsPublicVisible,
            RowVersion = Convert.ToBase64String(x.RowVersion)
        }).ToList();
    }

    private Task<ApiResponse<T>> ExecuteWriteAsync<T>(string operation, Func<Task<ApiResponse<T>>> action) => ExecuteAsync(operation, action);

    private async Task<ApiResponse<T>> ExecuteAsync<T>(string operation, Func<Task<ApiResponse<T>>> action)
    {
        try
        {
            var strategy = _unitOfWork.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var scope = new TransactionScope(
                    TransactionScopeOption.Required,
                    new TransactionOptions { IsolationLevel = IsolationLevel.Serializable },
                    TransactionScopeAsyncFlowOption.Enabled);
                var response = await action();
                if (response.Success) scope.Complete();
                return response;
            });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Stale academic calendar write during {Operation} for tenant {TenantId}", operation, _currentUser.TenantId);
            return Error<T>("Academic calendar changed. Reload and try again.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Conflicting academic calendar write during {Operation} for tenant {TenantId}", operation, _currentUser.TenantId);
            return Error<T>("Academic calendar conflicts with another update. Reload and try again.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Serialized academic calendar write aborted during {Operation} for tenant {TenantId}", operation, _currentUser.TenantId);
            return Error<T>("Academic calendar conflicts with another update. Reload and try again.", 409);
        }
    }

    private static string? ValidateEvent(string? title, DateTime start, DateTime end)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200)
            return "Calendar event title is required and cannot exceed 200 characters.";
        if (start == default || end == default || end.Date < start.Date)
            return "Calendar event date range is invalid.";
        return null;
    }

    private static string? ValidateDates(DateOnly start, DateOnly end, AcademicYear year, AcademicTerm? term)
    {
        if (!Within(start, end, year)) return "Calendar event is outside the academic year.";
        if (term != null && (start < term.StartDate || end > term.EndDate))
            return "Calendar event is outside the academic term.";
        return null;
    }

    private static (bool Success, DateOnly From, DateOnly To, string? Error) NormalizeRange(DateTime from, DateTime to)
    {
        var start = DateOnly.FromDateTime(from.Date);
        var end = DateOnly.FromDateTime(to.Date);
        if (from == default || to == default || end < start)
            return (false, start, end, "Calendar date range is invalid.");
        if (end.DayNumber - start.DayNumber > 366)
            return (false, start, end, "Calendar date range cannot exceed 367 days.");
        return (true, start, end, null);
    }

    private static bool Within(DateOnly start, DateOnly end, AcademicYear year) =>
        start >= year.StartDate && end <= year.EndDate;

    private static bool TryMapEventType(EduOS.Core.Enums.Academics.AcademicCalendarEventType value, out CalendarEventKind mapped)
    {
        if (Enum.TryParse<CalendarEventKind>(value.ToString(), true, out mapped) && Enum.IsDefined(mapped))
            return true;
        mapped = CalendarEventKind.Other;
        return false;
    }

    private static bool TryDecodeRowVersion(string? value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            bytes = Convert.FromBase64String(value);
            return bytes.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool VersionsMatch(byte[] left, byte[] right) =>
        left.Length == right.Length && left.Length > 0 && CryptographicOperations.FixedTimeEquals(left, right);

    private bool CanRead() =>
        _currentUser.IsAuthenticated && _currentUser.TenantId > 0 &&
        (CanManage() || _currentUser.IsInRole("Teacher") || _currentUser.IsInRole("Student") ||
         _currentUser.IsInRole("Guardian") || _currentUser.IsInRole("Parent"));

    private bool CanManage() =>
        _currentUser.IsAuthenticated && _currentUser.TenantId > 0 &&
        (_currentUser.IsTenantAdmin || _currentUser.IsInRole("Principal") || _currentUser.IsInRole("VicePrincipal"));

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static ApiResponse<T> Created<T>(T data, string message) => new() { Success = true, StatusCode = 201, Message = message, Data = data };
    private static ApiResponse<T> Error<T>(string message, int statusCode = 400) => ApiResponse<T>.ErrorResponse(message, statusCode);
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Academic calendar access is required.", 403);

    private sealed record CalendarScope(bool Success, AcademicYear? Year, AcademicTerm? Term, Campus? Campus, string? Error, int StatusCode)
    {
        public static CalendarScope Ok(AcademicYear year, AcademicTerm? term, Campus? campus) => new(true, year, term, campus, null, 200);
        public static CalendarScope Fail(string error, int statusCode) => new(false, null, null, null, error, statusCode);
    }
}
