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
        var policy = await EffectivePolicyQuery(academicYearId, campusId).AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return policy == null ? Error<AcademicCalendarPolicyDto>("Academic calendar policy is not configured.", 404) : ApiResponse<AcademicCalendarPolicyDto>.SuccessResponse(Map(policy));
    }

    public Task<ApiResponse<AcademicCalendarPolicyDto>> SavePolicyAsync(SaveAcademicCalendarPolicyDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<AcademicCalendarPolicyDto>());
        if (request == null || request.ClientRequestId == Guid.Empty || request.AcademicYearId <= 0 || request.CampusId <= 0 || request.WeekendDays == null)
            return Task.FromResult(Error<AcademicCalendarPolicyDto>("Request ID and academic year are required; campus, when supplied, must be positive."));
        var days = request.WeekendDays.Distinct().OrderBy(x => x).ToList();
        if (days.Count is < 1 or > 6 || days.Any(x => !Enum.IsDefined(typeof(DayOfWeek), x)))
            return Task.FromResult(Error<AcademicCalendarPolicyDto>("Configure between one and six distinct weekend days."));
        var mask = ToMask(days);
        return ExecuteWriteAsync("save academic calendar policy", async () =>
        {
            var tenantId = _currentUser.TenantId;
            var replay = await _policies.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ClientRequestId == request.ClientRequestId, cancellationToken);
            if (replay != null)
            {
                if (replay.AcademicYearId != request.AcademicYearId || replay.CampusId != request.CampusId || replay.WeekendDaysMask != mask)
                    return Error<AcademicCalendarPolicyDto>("Client request ID was already used for a different calendar policy.", 409);
                return ApiResponse<AcademicCalendarPolicyDto>.SuccessResponse(Map(replay), "Academic calendar policy already exists.");
            }
            var scope = await ResolveScopeAsync(request.AcademicYearId, null, request.CampusId, cancellationToken);
            if (!scope.Success) return Error<AcademicCalendarPolicyDto>(scope.Error!, scope.StatusCode);
            var existing = await _policies.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.AcademicYearId == request.AcademicYearId && x.CampusId == request.CampusId && x.IsActive, cancellationToken);
            if (existing != null)
            {
                if (existing.WeekendDaysMask == mask) return ApiResponse<AcademicCalendarPolicyDto>.SuccessResponse(Map(existing), "Academic calendar policy already matches.");
                if (!TryDecodeRowVersion(request.RowVersion, out var rowVersion) || !CryptographicOperations.FixedTimeEquals(existing.RowVersion, rowVersion))
                    return Error<AcademicCalendarPolicyDto>("Academic calendar policy changed. Reload and try again.", 409);
                existing.WeekendDaysMask = mask;
                existing.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                existing.UpdatedBy = _currentUser.UserId;
                _policies.Update(existing);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return ApiResponse<AcademicCalendarPolicyDto>.SuccessResponse(Map(existing), "Academic calendar policy updated.");
            }
            var row = new AcademicCalendarPolicy
            {
                TenantId = tenantId,
                ClientRequestId = request.ClientRequestId,
                AcademicYearId = request.AcademicYearId,
                CampusId = request.CampusId,
                WeekendDaysMask = mask,
                IsActive = true,
                AcademicYear = scope.Year,
                Campus = scope.Campus,
                CreatedAt = _clock.GetUtcNow().UtcDateTime,
                CreatedBy = _currentUser.UserId
            };
            await _policies.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Created(Map(row), "Academic calendar policy created.");
        });
    }

    public async Task<ApiResponse<IReadOnlyList<AcademicCalendarEventDto>>> GetEventsAsync(long academicYearId, long? campusId, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<IReadOnlyList<AcademicCalendarEventDto>>();
        var range = NormalizeRange(fromDate, toDate);
        if (!range.Success) return Error<IReadOnlyList<AcademicCalendarEventDto>>(range.Error!);
        var scope = await ResolveScopeAsync(academicYearId, null, campusId, cancellationToken);
        if (!scope.Success) return Error<IReadOnlyList<AcademicCalendarEventDto>>(scope.Error!, scope.StatusCode);
        if (!Within(range.From, range.To, scope.Year!)) return Error<IReadOnlyList<AcademicCalendarEventDto>>("Calendar range is outside the academic year.", 409);
        var query = _events.GetQueryable().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId && x.AcademicYearId == academicYearId && x.IsActive && x.StartDate <= range.To && x.EndDate >= range.From && (!x.CampusId.HasValue || x.CampusId == campusId));
        if (!CanManage()) query = query.Where(x => x.IsPublicVisible);
        IReadOnlyList<AcademicCalendarEventDto> rows = (await query.OrderBy(x => x.StartDate).ThenBy(x => x.Title).ToListAsync(cancellationToken)).Select(Map).ToList();
        return ApiResponse<IReadOnlyList<AcademicCalendarEventDto>>.SuccessResponse(rows);
    }

    public Task<ApiResponse<AcademicCalendarEventDto>> CreateEventAsync(CreateAcademicCalendarEventDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<AcademicCalendarEventDto>());
        var inputError = ValidateEvent(request?.ClientRequestId ?? Guid.Empty, request?.EventType ?? (AcademicCalendarEventType)0, request?.Title, request?.StartDate ?? default, request?.EndDate ?? default);
        if (request == null || inputError != null || request.AcademicYearId <= 0 || request.CampusId <= 0) return Task.FromResult(Error<AcademicCalendarEventDto>(inputError ?? "Academic year is required; campus, when supplied, must be positive."));
        return ExecuteWriteAsync("create academic calendar event", async () =>
        {
            var tenantId = _currentUser.TenantId;
            var start = request.StartDate.Date;
            var end = request.EndDate.Date;
            var title = request.Title.Trim();
            var holiday = request.IsHoliday || request.EventType == AcademicCalendarEventType.Holiday;
            var replay = await _events.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ClientRequestId == request.ClientRequestId, cancellationToken);
            if (replay != null)
            {
                if (!SameEvent(replay, request.CampusId, request.AcademicYearId, request.AcademicTermId, request.EventType, title, Trim(request.Description), start, end, Trim(request.Location), holiday, request.IsPublicVisible))
                    return Error<AcademicCalendarEventDto>("Client request ID was already used for a different calendar event.", 409);
                return ApiResponse<AcademicCalendarEventDto>.SuccessResponse(Map(replay), "Academic calendar event already exists.");
            }
            var scope = await ResolveScopeAsync(request.AcademicYearId, request.AcademicTermId, request.CampusId, cancellationToken);
            if (!scope.Success) return Error<AcademicCalendarEventDto>(scope.Error!, scope.StatusCode);
            var dateError = ValidateDates(start, end, scope.Year!, scope.Term);
            if (dateError != null) return Error<AcademicCalendarEventDto>(dateError, 409);
            var existing = await _events.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.AcademicYearId == request.AcademicYearId && x.AcademicTermId == request.AcademicTermId && x.CampusId == request.CampusId && x.Title == title && x.StartDate == start && x.EndDate == end && x.IsActive, cancellationToken);
            if (existing != null)
            {
                if (!SameEvent(existing, request.CampusId, request.AcademicYearId, request.AcademicTermId, request.EventType, title, Trim(request.Description), start, end, Trim(request.Location), holiday, request.IsPublicVisible)) return Error<AcademicCalendarEventDto>("An active calendar event already uses this title and date range.", 409);
                return ApiResponse<AcademicCalendarEventDto>.SuccessResponse(Map(existing), "Academic calendar event already exists.");
            }
            var row = new AcademicCalendarEvent
            {
                TenantId = tenantId,
                ClientRequestId = request.ClientRequestId,
                CampusId = request.CampusId,
                AcademicYearId = request.AcademicYearId,
                AcademicTermId = request.AcademicTermId,
                EventType = request.EventType,
                Title = title,
                Description = Trim(request.Description),
                StartDate = start,
                EndDate = end,
                Location = Trim(request.Location),
                IsHoliday = holiday,
                IsPublicVisible = request.IsPublicVisible,
                IsActive = true,
                Campus = scope.Campus,
                AcademicYear = scope.Year,
                AcademicTerm = scope.Term,
                CreatedAt = _clock.GetUtcNow().UtcDateTime,
                CreatedBy = _currentUser.UserId
            };
            await _events.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Created(Map(row), "Academic calendar event created.");
        });
    }

    public Task<ApiResponse<AcademicCalendarEventDto>> UpdateEventAsync(long id, UpdateAcademicCalendarEventDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<AcademicCalendarEventDto>());
        var inputError = ValidateEvent(Guid.NewGuid(), request?.EventType ?? (AcademicCalendarEventType)0, request?.Title, request?.StartDate ?? default, request?.EndDate ?? default);
        if (id <= 0 || request == null || inputError != null || !TryDecodeRowVersion(request.RowVersion, out var rowVersion)) return Task.FromResult(Error<AcademicCalendarEventDto>(inputError ?? "A valid event and row version are required."));
        return ExecuteWriteAsync("update academic calendar event", async () =>
        {
            var row = await _events.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == id, cancellationToken);
            if (row == null) return Error<AcademicCalendarEventDto>("Academic calendar event not found.", 404);
            if (!CryptographicOperations.FixedTimeEquals(row.RowVersion, rowVersion)) return Error<AcademicCalendarEventDto>("Academic calendar event changed. Reload and try again.", 409);
            if (!row.AcademicYearId.HasValue) return Error<AcademicCalendarEventDto>("Legacy calendar event has no academic year and cannot use this workflow.", 409);
            var scope = await ResolveScopeAsync(row.AcademicYearId.Value, row.AcademicTermId, row.CampusId, cancellationToken);
            if (!scope.Success) return Error<AcademicCalendarEventDto>(scope.Error!, scope.StatusCode);
            var start = request.StartDate.Date;
            var end = request.EndDate.Date;
            var dateError = ValidateDates(start, end, scope.Year!, scope.Term);
            if (dateError != null) return Error<AcademicCalendarEventDto>(dateError, 409);
            if (await _events.GetQueryable().AnyAsync(x => x.TenantId == _currentUser.TenantId && x.Id != row.Id && x.AcademicYearId == row.AcademicYearId && x.AcademicTermId == row.AcademicTermId && x.CampusId == row.CampusId && x.Title == request.Title.Trim() && x.StartDate == start && x.EndDate == end && x.IsActive, cancellationToken))
                return Error<AcademicCalendarEventDto>("An active calendar event already uses this title and date range.", 409);
            row.EventType = request.EventType;
            row.Title = request.Title.Trim();
            row.Description = Trim(request.Description);
            row.StartDate = start;
            row.EndDate = end;
            row.Location = Trim(request.Location);
            row.IsHoliday = request.IsHoliday || request.EventType == AcademicCalendarEventType.Holiday;
            row.IsPublicVisible = request.IsPublicVisible;
            row.IsActive = request.IsActive;
            row.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
            row.UpdatedBy = _currentUser.UserId;
            _events.Update(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ApiResponse<AcademicCalendarEventDto>.SuccessResponse(Map(row), "Academic calendar event updated.");
        });
    }

    public async Task<ApiResponse<IReadOnlyList<AcademicWorkingDayDto>>> GetWorkingDaysAsync(long academicYearId, long? campusId, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<IReadOnlyList<AcademicWorkingDayDto>>();
        var range = NormalizeRange(fromDate, toDate);
        if (!range.Success) return Error<IReadOnlyList<AcademicWorkingDayDto>>(range.Error!);
        var scope = await ResolveScopeAsync(academicYearId, null, campusId, cancellationToken);
        if (!scope.Success) return Error<IReadOnlyList<AcademicWorkingDayDto>>(scope.Error!, scope.StatusCode);
        if (!Within(range.From, range.To, scope.Year!)) return Error<IReadOnlyList<AcademicWorkingDayDto>>("Working-day range is outside the academic year.", 409);
        var policy = await EffectivePolicyQuery(academicYearId, campusId).AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (policy == null) return Error<IReadOnlyList<AcademicWorkingDayDto>>("Configure an academic calendar policy before calculating working days.", 409);
        var canManage = CanManage();
        var holidays = await _events.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && x.AcademicYearId == academicYearId && x.IsActive && x.IsHoliday && x.StartDate <= range.To && x.EndDate >= range.From && (!x.CampusId.HasValue || x.CampusId == campusId))
            .Select(x => new { x.Title, x.StartDate, x.EndDate, x.IsPublicVisible })
            .ToListAsync(cancellationToken);
        var rows = new List<AcademicWorkingDayDto>();
        for (var date = range.From; date <= range.To; date = date.AddDays(1))
        {
            var weekend = (policy.WeekendDaysMask & (1 << (int)date.DayOfWeek)) != 0;
            IReadOnlyList<string> names = holidays.Where(x => x.StartDate.Date <= date && x.EndDate.Date >= date)
                .Select(x => canManage || x.IsPublicVisible ? x.Title : "Holiday")
                .Distinct().OrderBy(x => x).ToList();
            rows.Add(new AcademicWorkingDayDto { Date = date, IsWeekend = weekend, HolidayNames = names, IsWorkingDay = !weekend && names.Count == 0 });
        }
        return ApiResponse<IReadOnlyList<AcademicWorkingDayDto>>.SuccessResponse(rows);
    }

    private IQueryable<AcademicCalendarPolicy> EffectivePolicyQuery(long academicYearId, long? campusId) => _policies.GetQueryable()
        .Where(x => x.TenantId == _currentUser.TenantId && x.AcademicYearId == academicYearId && x.IsActive && (x.CampusId == campusId || x.CampusId == null))
        .OrderByDescending(x => x.CampusId == campusId).ThenByDescending(x => x.Id);

    private async Task<CalendarScope> ResolveScopeAsync(long academicYearId, long? academicTermId, long? campusId, CancellationToken cancellationToken)
    {
        var tenantId = _currentUser.TenantId;
        var year = await _years.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == academicYearId && x.IsActive, cancellationToken);
        if (year == null) return CalendarScope.Fail("Academic year not found.", 404);
        Campus? campus = null;
        if (campusId.HasValue)
        {
            campus = await _campuses.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == campusId.Value && x.IsActive, cancellationToken);
            if (campus == null) return CalendarScope.Fail("Campus not found.", 404);
        }
        AcademicTerm? term = null;
        if (academicTermId.HasValue)
        {
            term = await _terms.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == academicTermId.Value && x.AcademicYearId == year.Id && x.IsActive, cancellationToken);
            if (term == null) return CalendarScope.Fail("Academic term not found in this academic year.", 404);
        }
        return CalendarScope.Ok(year, term, campus);
    }

    private Task<ApiResponse<T>> ExecuteWriteAsync<T>(string operation, Func<Task<ApiResponse<T>>> action) => ExecuteAsync(operation, action);

    private async Task<ApiResponse<T>> ExecuteAsync<T>(string operation, Func<Task<ApiResponse<T>>> action)
    {
        try
        {
            var strategy = _unitOfWork.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var scope = new TransactionScope(TransactionScopeOption.Required, new TransactionOptions { IsolationLevel = IsolationLevel.Serializable }, TransactionScopeAsyncFlowOption.Enabled);
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

    private static string? ValidateEvent(Guid clientRequestId, AcademicCalendarEventType eventType, string? title, DateTime start, DateTime end)
    {
        if (clientRequestId == Guid.Empty) return "Client request ID is required.";
        if (!Enum.IsDefined(typeof(AcademicCalendarEventType), eventType)) return "Calendar event type is invalid.";
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200) return "Calendar event title is required and cannot exceed 200 characters.";
        if (start == default || end == default || end.Date < start.Date) return "Calendar event date range is invalid.";
        return null;
    }

    private static string? ValidateDates(DateTime start, DateTime end, AcademicYear year, AcademicTerm? term)
    {
        if (!Within(start, end, year)) return "Calendar event is outside the academic year.";
        if (term != null && (!term.StartDate.HasValue || !term.EndDate.HasValue)) return "Academic term dates must be configured before adding calendar events.";
        if (term != null && (start < term.StartDate!.Value.Date || end > term.EndDate!.Value.Date)) return "Calendar event is outside the academic term.";
        return null;
    }

    private static (bool Success, DateTime From, DateTime To, string? Error) NormalizeRange(DateTime from, DateTime to)
    {
        var start = from.Date;
        var end = to.Date;
        if (from == default || to == default || end < start) return (false, start, end, "Calendar date range is invalid.");
        if ((end - start).TotalDays > 366) return (false, start, end, "Calendar date range cannot exceed 367 days.");
        return (true, start, end, null);
    }

    private static bool Within(DateTime start, DateTime end, AcademicYear year) => start >= year.StartDate.Date && end <= year.EndDate.Date;
    private static bool SameEvent(AcademicCalendarEvent row, long? campusId, long academicYearId, long? academicTermId, AcademicCalendarEventType type, string title, string? description, DateTime start, DateTime end, string? location, bool holiday, bool isPublic) => row.CampusId == campusId && row.AcademicYearId == academicYearId && row.AcademicTermId == academicTermId && row.EventType == type && row.Title == title && row.Description == description && row.StartDate.Date == start && row.EndDate.Date == end && row.Location == location && row.IsHoliday == holiday && row.IsPublicVisible == isPublic;
    private static int ToMask(IEnumerable<DayOfWeek> days) => days.Aggregate(0, (mask, day) => mask | (1 << (int)day));
    private static IReadOnlyList<DayOfWeek> FromMask(int mask) => Enum.GetValues<DayOfWeek>().Where(day => (mask & (1 << (int)day)) != 0).ToList();
    private static bool TryDecodeRowVersion(string? value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { bytes = Convert.FromBase64String(value); return bytes.Length > 0; }
        catch (FormatException) { return false; }
    }
    private bool CanRead() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0 && (CanManage() || _currentUser.IsInRole("Teacher") || _currentUser.IsInRole("Student") || _currentUser.IsInRole("Guardian") || _currentUser.IsInRole("Parent"));
    private bool CanManage() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0 && (_currentUser.IsTenantAdmin || _currentUser.IsInRole("Principal") || _currentUser.IsInRole("VicePrincipal"));
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static AcademicCalendarPolicyDto Map(AcademicCalendarPolicy x) => new() { Id = x.Id, AcademicYearId = x.AcademicYearId, CampusId = x.CampusId, WeekendDays = FromMask(x.WeekendDaysMask), IsActive = x.IsActive, RowVersion = Convert.ToBase64String(x.RowVersion) };
    private static AcademicCalendarEventDto Map(AcademicCalendarEvent x) => new() { Id = x.Id, CampusId = x.CampusId, AcademicYearId = x.AcademicYearId, AcademicTermId = x.AcademicTermId, EventType = x.EventType, Title = x.Title, Description = x.Description, StartDate = x.StartDate, EndDate = x.EndDate, Location = x.Location, IsHoliday = x.IsHoliday, IsPublicVisible = x.IsPublicVisible, IsActive = x.IsActive, RowVersion = Convert.ToBase64String(x.RowVersion) };
    private static ApiResponse<T> Created<T>(T data, string message) => new() { Success = true, StatusCode = 201, Message = message, Data = data };
    private static ApiResponse<T> Error<T>(string message, int statusCode = 400) => ApiResponse<T>.ErrorResponse(message, statusCode);
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Academic calendar access is required.", 403);

    private sealed record CalendarScope(bool Success, AcademicYear? Year, AcademicTerm? Term, Campus? Campus, string? Error, int StatusCode)
    {
        public static CalendarScope Ok(AcademicYear year, AcademicTerm? term, Campus? campus) => new(true, year, term, campus, null, 200);
        public static CalendarScope Fail(string error, int statusCode) => new(false, null, null, null, error, statusCode);
    }
}
