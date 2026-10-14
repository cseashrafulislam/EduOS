using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Security.Cryptography;

namespace EduOS.Service.Services.Academic;

/// <summary>Curriculum prerequisite authoring. Existing enrollments freeze the curriculum's dependency graph.</summary>
public sealed class SubjectPrerequisiteService : ISubjectPrerequisiteService
{
    private readonly IGenericRepository<SubjectPrerequisite> _links;
    private readonly IGenericRepository<AcademicCurriculum> _curricula;
    private readonly IGenericRepository<CurriculumSubject> _mappings;
    private readonly IGenericRepository<Subject> _subjects;
    private readonly IGenericRepository<StudentEnrollment> _enrollments;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly ILogger<SubjectPrerequisiteService> _logger;

    public SubjectPrerequisiteService(IGenericRepository<SubjectPrerequisite> links,
        IGenericRepository<AcademicCurriculum> curricula, IGenericRepository<CurriculumSubject> mappings,
        IGenericRepository<Subject> subjects, IGenericRepository<StudentEnrollment> enrollments,
        IUnitOfWork uow, ICurrentUserService user, ILogger<SubjectPrerequisiteService> logger)
    {
        _links = links; _curricula = curricula; _mappings = mappings; _subjects = subjects;
        _enrollments = enrollments; _uow = uow; _user = user; _logger = logger;
    }

    public async Task<ApiResponse<IReadOnlyList<SubjectDto>>> OptionsAsync(long curriculumId, CancellationToken ct = default)
    {
        if (!CanRead()) return Error<IReadOnlyList<SubjectDto>>("Academic permission required.", 403);
        var tenant = _user.TenantId;
        if (curriculumId <= 0 || !await _curricula.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.Id == curriculumId && x.IsActive && !x.IsDeleted, ct))
            return Error<IReadOnlyList<SubjectDto>>("Curriculum not found.", 404);
        var ids = await _mappings.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.AcademicCurriculumId == curriculumId && x.IsActive && !x.IsDeleted)
            .Select(x => x.SubjectId).Distinct().Take(501).ToListAsync(ct);
        if (ids.Count > 500) return Error<IReadOnlyList<SubjectDto>>("Too many mapped subjects. Contact support.", 409);
        var subjects = await _subjects.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted && x.IsActive && ids.Contains(x.Id))
            .OrderBy(x => x.Name).Select(x => new SubjectDto
            {
                Id = x.Id, Name = x.Name, Code = x.Code, ShortName = x.ShortName,
                DefaultCreditHours = x.DefaultCreditHours, IsActive = x.IsActive
            }).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<SubjectDto>>.SuccessResponse(subjects);
    }

    public async Task<ApiResponse<PagedResult<SubjectPrerequisiteDto>>> SearchAsync(long curriculumId,
        string? search, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (!CanRead()) return Error<PagedResult<SubjectPrerequisiteDto>>("Academic permission required.", 403);
        if (curriculumId <= 0 || page < 1 || pageSize is < 1 or > 100 || search?.Length > 100)
            return Error<PagedResult<SubjectPrerequisiteDto>>("Invalid search parameters.");
        var tenant = _user.TenantId;
        if (!await _curricula.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.Id == curriculumId && !x.IsDeleted, ct))
            return Error<PagedResult<SubjectPrerequisiteDto>>("Curriculum not found.", 404);
        var query = _links.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.AcademicCurriculumId == curriculumId && !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var subjectIds = _subjects.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && !x.IsDeleted && (x.Name.Contains(term) || x.Code.Contains(term))).Select(x => x.Id);
            query = query.Where(x => subjectIds.Contains(x.SubjectId) || subjectIds.Contains(x.PrerequisiteSubjectId));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.SubjectId).ThenBy(x => x.PrerequisiteSubjectId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = rows.SelectMany(x => new[] { x.SubjectId, x.PrerequisiteSubjectId }).Distinct().ToArray();
        var names = await _subjects.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant && ids.Contains(x.Id))
            .Select(x => new { x.Id, x.Name }).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return ApiResponse<PagedResult<SubjectPrerequisiteDto>>.SuccessResponse(new PagedResult<SubjectPrerequisiteDto>
        {
            Items = rows.Select(x => Map(x, names)).ToList(), TotalCount = total, Page = page, PageSize = pageSize
        });
    }

    public Task<ApiResponse<SubjectPrerequisiteDto>> CreateAsync(SaveSubjectPrerequisiteRequestDto request,
        CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Error<SubjectPrerequisiteDto>("Academic management permission required.", 403));
        if (request == null || request.AcademicCurriculumId <= 0 || request.SubjectId <= 0 ||
            request.PrerequisiteSubjectId <= 0 || request.SubjectId == request.PrerequisiteSubjectId)
            return Task.FromResult(Error<SubjectPrerequisiteDto>("Select two different mapped subjects."));
        return WriteAsync("create prerequisite", async token =>
        {
            var tenant = _user.TenantId;
            var curriculum = await EditableCurriculumAsync(request.AcademicCurriculumId, token);
            if (curriculum == null) return Error<SubjectPrerequisiteDto>("Curriculum unavailable or already used by enrolled students.", 409);
            var ids = await _mappings.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.AcademicCurriculumId == curriculum.Id &&
                x.IsActive && !x.IsDeleted && (x.SubjectId == request.SubjectId || x.SubjectId == request.PrerequisiteSubjectId))
                .Select(x => x.SubjectId).Distinct().ToListAsync(token);
            if (ids.Count != 2) return Error<SubjectPrerequisiteDto>("Both subjects must be active mappings of this curriculum.", 409);
            var existing = await _links.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.AcademicCurriculumId == curriculum.Id && !x.IsDeleted &&
                x.SubjectId == request.SubjectId && x.PrerequisiteSubjectId == request.PrerequisiteSubjectId, token);
            if (existing != null)
                return existing.IsMandatory == request.IsMandatory
                    ? ApiResponse<SubjectPrerequisiteDto>.SuccessResponse(await MapAsync(existing, token), "Already registered.")
                    : Error<SubjectPrerequisiteDto>("Existing prerequisite differs. Edit using its row version.", 409);
            var edges = await _links.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.AcademicCurriculumId == curriculum.Id && !x.IsDeleted)
                .Select(x => new { x.SubjectId, x.PrerequisiteSubjectId }).Take(2001).ToListAsync(token);
            if (edges.Count > 2000) return Error<SubjectPrerequisiteDto>("Prerequisite graph exceeds the supported size.", 409);
            if (CreatesCycle(request.SubjectId, request.PrerequisiteSubjectId,
                edges.Select(x => (x.SubjectId, x.PrerequisiteSubjectId)).ToArray()))
                return Error<SubjectPrerequisiteDto>("Prerequisite would create a circular dependency.", 409);
            var row = new SubjectPrerequisite
            {
                TenantId = tenant, AcademicCurriculumId = curriculum.Id,
                SubjectId = request.SubjectId, PrerequisiteSubjectId = request.PrerequisiteSubjectId,
                IsMandatory = request.IsMandatory, CreatedBy = _user.UserId
            };
            await _links.AddAsync(row);
            await _uow.SaveChangesAsync(token);
            return new ApiResponse<SubjectPrerequisiteDto>
            { Success = true, StatusCode = 201, Message = "Prerequisite added.", Data = await MapAsync(row, token) };
        }, ct);
    }

    public Task<ApiResponse<SubjectPrerequisiteDto>> UpdateAsync(long id, SaveSubjectPrerequisiteRequestDto request,
        CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Error<SubjectPrerequisiteDto>("Academic management permission required.", 403));
        if (id <= 0 || request == null || !TryVersion(request.RowVersion, out var expected))
            return Task.FromResult(Error<SubjectPrerequisiteDto>("Valid ID and row version are required."));
        return WriteAsync("update prerequisite", async token =>
        {
            var row = await _links.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == _user.TenantId && x.Id == id && !x.IsDeleted, token);
            if (row == null) return Error<SubjectPrerequisiteDto>("Prerequisite not found.", 404);
            if (row.AcademicCurriculumId != request.AcademicCurriculumId ||
                row.SubjectId != request.SubjectId || row.PrerequisiteSubjectId != request.PrerequisiteSubjectId)
                return Error<SubjectPrerequisiteDto>("Prerequisite endpoints cannot be changed. Archive and create a new link.", 409);
            if (!VersionsMatch(row.RowVersion, expected))
                return Error<SubjectPrerequisiteDto>("Prerequisite changed. Reload before editing.", 409);
            if (row.IsMandatory == request.IsMandatory)
                return ApiResponse<SubjectPrerequisiteDto>.SuccessResponse(await MapAsync(row, token));
            if (await EditableCurriculumAsync(row.AcademicCurriculumId, token) == null)
                return Error<SubjectPrerequisiteDto>("Curriculum is unavailable or already used.", 409);
            row.IsMandatory = request.IsMandatory; row.UpdatedAt = DateTime.UtcNow; row.UpdatedBy = _user.UserId;
            _links.Update(row); await _uow.SaveChangesAsync(token);
            return ApiResponse<SubjectPrerequisiteDto>.SuccessResponse(await MapAsync(row, token), "Prerequisite updated.");
        }, ct);
    }

    public Task<ApiResponse<bool>> ArchiveAsync(long id, string rowVersion, CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Error<bool>("Academic management permission required.", 403));
        if (id <= 0 || !TryVersion(rowVersion, out var expected))
            return Task.FromResult(Error<bool>("Valid ID and row version are required."));
        return WriteAsync("archive prerequisite", async token =>
        {
            var row = await _links.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == _user.TenantId && x.Id == id, token);
            if (row == null) return Error<bool>("Prerequisite not found.", 404);
            if (!VersionsMatch(row.RowVersion, expected))
                return Error<bool>("Prerequisite changed. Reload before removal.", 409);
            if (row.IsDeleted) return ApiResponse<bool>.SuccessResponse(true, "Already archived.");
            if (await EditableCurriculumAsync(row.AcademicCurriculumId, token) == null)
                return Error<bool>("Curriculum is unavailable or already used.", 409);
            row.IsDeleted = true; row.DeletedAt = DateTime.UtcNow; row.DeletedBy = _user.UserId;
            row.UpdatedAt = DateTime.UtcNow; row.UpdatedBy = _user.UserId;
            _links.Update(row); await _uow.SaveChangesAsync(token);
            return ApiResponse<bool>.SuccessResponse(true, "Prerequisite archived.");
        }, ct);
    }

    private async Task<AcademicCurriculum?> EditableCurriculumAsync(long id, CancellationToken ct)
    {
        var tenant = _user.TenantId;
        var curriculum = await _curricula.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == id && x.IsActive && !x.IsDeleted, ct);
        if (curriculum == null || curriculum.EffectiveTo < DateOnly.FromDateTime(DateTime.UtcNow)) return null;
        if (await _enrollments.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.AcademicCurriculumId == id && !x.IsDeleted, ct)) return null;
        return curriculum;
    }

    private async Task<SubjectPrerequisiteDto> MapAsync(SubjectPrerequisite row, CancellationToken ct)
    {
        var ids = new[] { row.SubjectId, row.PrerequisiteSubjectId };
        var names = await _subjects.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && ids.Contains(x.Id))
            .Select(x => new { x.Id, x.Name }).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return Map(row, names);
    }

    private static SubjectPrerequisiteDto Map(SubjectPrerequisite row, IReadOnlyDictionary<long, string> names) => new()
    {
        Id = row.Id, AcademicCurriculumId = row.AcademicCurriculumId, SubjectId = row.SubjectId,
        PrerequisiteSubjectId = row.PrerequisiteSubjectId, IsMandatory = row.IsMandatory,
        SubjectName = names.GetValueOrDefault(row.SubjectId, "Unknown"),
        PrerequisiteSubjectName = names.GetValueOrDefault(row.PrerequisiteSubjectId, "Unknown"),
        RowVersion = Convert.ToBase64String(row.RowVersion)
    };

    public static bool CreatesCycle(long subjectId, long prerequisiteId,
        IReadOnlyCollection<(long SubjectId, long PrerequisiteSubjectId)> edges)
    {
        if (subjectId == prerequisiteId) return true;
        var seen = new HashSet<long>();
        var pending = new Stack<long>();
        pending.Push(prerequisiteId);
        while (pending.TryPop(out var node))
        {
            if (node == subjectId) return true;
            if (!seen.Add(node)) continue;
            foreach (var edge in edges)
                if (edge.SubjectId == node) pending.Push(edge.PrerequisiteSubjectId);
        }
        return false;
    }

    private async Task<ApiResponse<T>> WriteAsync<T>(string operation,
        Func<CancellationToken, Task<ApiResponse<T>>> action, CancellationToken ct)
    {
        try { return await _uow.ExecuteInTransactionAsync(action, IsolationLevel.Serializable, ct); }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Prerequisite concurrency conflict: {Operation} tenant {TenantId}", operation, _user.TenantId);
            return Error<T>("Prerequisite changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Prerequisite database conflict: {Operation} tenant {TenantId}", operation, _user.TenantId);
            return Error<T>("Conflicting prerequisite already exists.", 409);
        }
    }

    private bool CanManage() => _user.IsAuthenticated && _user.TenantId > 0 &&
        (_user.IsTenantAdmin || _user.IsInRole("Principal") || _user.IsInRole("VicePrincipal"));
    private bool CanRead() => CanManage() || _user.IsAuthenticated && _user.TenantId > 0 && _user.IsInRole("Teacher");
    private static bool TryVersion(string? value, out byte[] version)
    {
        version = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { version = Convert.FromBase64String(value); return version.Length == 8; }
        catch (FormatException) { return false; }
    }
    private static bool VersionsMatch(byte[] actual, byte[] expected) =>
        actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    private static ApiResponse<T> Error<T>(string message, int status = 400) => ApiResponse<T>.ErrorResponse(message, status);
}
