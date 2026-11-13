using System.Data;
using System.Security.Cryptography;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Academic;

/// <summary>Campus/class Section administration, independent of admission cohorts.</summary>
public sealed class AcademicSectionService
{
    private readonly IGenericRepository<AcademicSection> _sections;
    private readonly IGenericRepository<AcademicBatch> _batches;
    private readonly IGenericRepository<AcademicLevel>? _levels;
    private readonly IGenericRepository<Campus>? _campuses;
    private readonly IGenericRepository<StudentEnrollment> _enrollments;
    private readonly IGenericRepository<SubjectOffering> _offerings;
    private readonly IGenericRepository<AttendanceSession> _attendance;
    private readonly IGenericRepository<ResultPublication> _publications;
    private readonly IGenericRepository<FeeStructure> _fees;
    private readonly ICurrentUserService _user;
    private readonly IUnitOfWork _uow;

    public AcademicSectionService(
        IGenericRepository<AcademicSection> sections,
        IGenericRepository<AcademicBatch> batches,
        IGenericRepository<StudentEnrollment> enrollments,
        IGenericRepository<SubjectOffering> offerings,
        IGenericRepository<AttendanceSession> attendance,
        IGenericRepository<ResultPublication> publications,
        IGenericRepository<FeeStructure> fees,
        ICurrentUserService user, IUnitOfWork uow,
        IGenericRepository<AcademicLevel>? levels = null,
        IGenericRepository<Campus>? campuses = null)
    {
        _sections = sections;
        _batches = batches;
        _levels = levels;
        _campuses = campuses;
        _enrollments = enrollments;
        _offerings = offerings;
        _attendance = attendance;
        _publications = publications;
        _fees = fees;
        _user = user;
        _uow = uow;
    }

    private bool CanRead => _user.IsAuthenticated && _user.TenantId > 0 &&
        (_user.IsTenantAdmin || _user.IsInRole("Principal") ||
         _user.IsInRole("VicePrincipal") || _user.IsInRole("AdmissionOfficer"));
    private bool Allowed => _user.IsAuthenticated && _user.IsTenantAdmin && _user.TenantId > 0;
    private static ApiResponse<T> Error<T>(string message, int code = 400) =>
        ApiResponse<T>.ErrorResponse(message, code);
    private static AcademicSectionDto Map(AcademicSection section, string campusName = "", string levelName = "") => new()
    {
        Id = section.Id,
        CampusId = section.CampusId,
        AcademicLevelId = section.AcademicLevelId,
        CampusName = campusName,
        AcademicLevelName = levelName,
        Name = section.Name,
        Code = section.Code,
        Capacity = section.Capacity,
        IsActive = section.IsActive,
        DisplayOrder = section.DisplayOrder,
        RowVersion = Convert.ToBase64String(section.RowVersion)
    };

    public async Task<ApiResponse<PagedResult<AcademicSectionDto>>> ListAsync(
        long? academicBatchId, string? search, int page, int pageSize, CancellationToken ct,
        long? campusId = null, long? academicLevelId = null)
    {
        if (!CanRead) return Error<PagedResult<AcademicSectionDto>>("Section administration requires tenant administrator access.", 403);
        if (academicBatchId is <= 0 || campusId is <= 0 || academicLevelId is <= 0 ||
            page < 1 || pageSize is < 1 or > 100 ||
            search?.Length > 80 || (long)(page - 1) * pageSize > int.MaxValue)
            return Error<PagedResult<AcademicSectionDto>>("Invalid section filters.");
        var tenant = _user.TenantId;
        var query = _sections.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted);
        if (academicBatchId.HasValue)
        {
            // Compatibility: old batch-specific consumers get sections for that
            // batch's campus/class, without making the Section owned by the batch.
            var cohort = await _batches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == academicBatchId.Value && !x.IsDeleted, ct);
            if (cohort == null)
                return Error<PagedResult<AcademicSectionDto>>("Academic batch not found.", 404);
            if (campusId.HasValue && campusId != cohort.CampusId ||
                academicLevelId.HasValue && academicLevelId != cohort.AcademicLevelId)
                return Error<PagedResult<AcademicSectionDto>>("Batch and class filters conflict.", 409);
            campusId = cohort.CampusId;
            academicLevelId = cohort.AcademicLevelId;
        }
        if (campusId.HasValue) query = query.Where(x => x.CampusId == campusId.Value);
        if (academicLevelId.HasValue) query = query.Where(x => x.AcademicLevelId == academicLevelId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var prefix = search.Trim();
            query = query.Where(x => x.Name.StartsWith(prefix) || x.Code.StartsWith(prefix));
        }
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var campusIds = rows.Select(x => x.CampusId).Distinct().ToArray();
        var levelIds = rows.Select(x => x.AcademicLevelId).Distinct().ToArray();
        var campusNames = _campuses == null ? new Dictionary<long, string>() :
            await _campuses.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && campusIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var levelNames = _levels == null ? new Dictionary<long, string>() :
            await _levels.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && levelIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return ApiResponse<PagedResult<AcademicSectionDto>>.SuccessResponse(new PagedResult<AcademicSectionDto>
        {
            Page = page, PageSize = pageSize, TotalCount = count,
            Items = rows.Select(x => Map(x, campusNames.GetValueOrDefault(x.CampusId) ?? "",
                levelNames.GetValueOrDefault(x.AcademicLevelId) ?? "")).ToList()
        });
    }

    public async Task<ApiResponse<AcademicSectionDto>> GetAsync(long id, CancellationToken ct)
    {
        if (!CanRead) return Error<AcademicSectionDto>("Section administration requires tenant administrator access.", 403);
        if (id <= 0) return Error<AcademicSectionDto>("Invalid section ID.");
        var tenant = _user.TenantId;
        var row = await _sections.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == id && !x.IsDeleted, ct);
        if (row == null) return Error<AcademicSectionDto>("Section not found.", 404);
        var campusName = _campuses == null ? "" : await _campuses.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.Id == row.CampusId)
            .Select(x => x.Name).FirstOrDefaultAsync(ct) ?? "";
        var levelName = _levels == null ? "" : await _levels.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.Id == row.AcademicLevelId)
            .Select(x => x.Name).FirstOrDefaultAsync(ct) ?? "";
        return ApiResponse<AcademicSectionDto>.SuccessResponse(Map(row, campusName, levelName));
    }

    public async Task<ApiResponse<AcademicSectionDto>> CreateAsync(
        CreateAcademicSectionRequestDto request, CancellationToken ct)
    {
        if (!Allowed) return Error<AcademicSectionDto>("Section administration requires tenant administrator access.", 403);
        if (request == null || request.ClientRequestId == Guid.Empty ||
            request.CampusId <= 0 || request.AcademicLevelId <= 0 ||
            string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Code) ||
            request.Name.Trim().Length > 150 || request.Code.Trim().Length > 50 ||
            request.Capacity is < 0 or > 100000 || request.DisplayOrder < 0)
            return Error<AcademicSectionDto>("Invalid section details.");
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var tenant = _user.TenantId;
                var name = request.Name.Trim();
                var code = request.Code.Trim().ToUpperInvariant();
                var replay = await _sections.GetQueryable().AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantId == tenant && x.ClientRequestId == request.ClientRequestId, token);
                if (replay != null)
                {
                    if (replay.CampusId != request.CampusId ||
                        replay.AcademicLevelId != request.AcademicLevelId || replay.Name != name ||
                        replay.Code != code || replay.Capacity != request.Capacity ||
                        replay.DisplayOrder != request.DisplayOrder || replay.IsActive != request.IsActive ||
                        replay.IsDeleted)
                        return Error<AcademicSectionDto>("Request ID already used for another section.", 409);
                    return ApiResponse<AcademicSectionDto>.SuccessResponse(Map(replay), "Section already saved.");
                }
                if (_campuses == null || _levels == null)
                    return Error<AcademicSectionDto>("Section master data service unavailable.", 503);
                var campus = await _campuses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == request.CampusId && x.IsActive && !x.IsDeleted, token);
                var level = await _levels.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == request.AcademicLevelId && x.IsActive && !x.IsDeleted, token);
                if (campus == null || level == null)
                    return Error<AcademicSectionDto>("Active campus or class not found.", 404);
                if (await _sections.GetQueryable().AnyAsync(x => x.TenantId == tenant &&
                    x.CampusId == campus.Id && x.AcademicLevelId == level.Id &&
                    x.Code == code && !x.IsDeleted, token))
                    return Error<AcademicSectionDto>("This class already has the selected section code.", 409);
                var row = new AcademicSection
                {
                    TenantId = tenant, CampusId = campus.Id, AcademicLevelId = level.Id,
                    AcademicBatchId = null, ClientRequestId = request.ClientRequestId,
                    Name = name, Code = code, Capacity = request.Capacity,
                    IsActive = request.IsActive, DisplayOrder = request.DisplayOrder,
                    CreatedBy = _user.UserId, CreatedAt = DateTime.UtcNow
                };
                await _sections.AddAsync(row);
                await _uow.SaveChangesAsync(token);
                return ApiResponse<AcademicSectionDto>.SuccessResponse(Map(row, campus.Name, level.Name), "Section created.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateException)
        {
            return Error<AcademicSectionDto>("Section creation conflicted with another write. Reload and retry.", 409);
        }
    }

    private async Task<bool> ReferencedAsync(long id, long tenant, CancellationToken ct) =>
        await _enrollments.GetQueryable().AnyAsync(x => x.TenantId == tenant && x.AcademicSectionId == id, ct) ||
        await _offerings.GetQueryable().AnyAsync(x => x.TenantId == tenant && x.AcademicSectionId == id, ct) ||
        await _attendance.GetQueryable().AnyAsync(x => x.TenantId == tenant && x.AcademicSectionId == id, ct) ||
        await _publications.GetQueryable().AnyAsync(x => x.TenantId == tenant && x.AcademicSectionId == id, ct) ||
        await _fees.GetQueryable().AnyAsync(x => x.TenantId == tenant && x.AcademicSectionId == id, ct);

    private static byte[]? Version(string? value)
    {
        try
        {
            var bytes = Convert.FromBase64String(value ?? "");
            return bytes.Length == 8 ? bytes : null;
        }
        catch (FormatException) { return null; }
    }

    public async Task<ApiResponse<AcademicSectionDto>> UpdateAsync(long id,
        UpdateAcademicSectionRequestDto request, CancellationToken ct)
    {
        if (!Allowed) return Error<AcademicSectionDto>("Section administration requires tenant administrator access.", 403);
        var expected = Version(request?.RowVersion);
        if (id <= 0 || request == null || expected == null || string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Trim().Length > 150 || request.Capacity is < 0 or > 100000 ||
            request.DisplayOrder < 0)
            return Error<AcademicSectionDto>("Invalid section update or version.");
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var tenant = _user.TenantId;
                var row = await _sections.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == id && !x.IsDeleted, token);
                if (row == null) return Error<AcademicSectionDto>("Section not found.", 404);
                var name = request.Name.Trim();
                if (row.Name == name && row.Capacity == request.Capacity &&
                    row.DisplayOrder == request.DisplayOrder && row.IsActive == request.IsActive)
                    return ApiResponse<AcademicSectionDto>.SuccessResponse(Map(row), "Section already up to date.");
                if (!CryptographicOperations.FixedTimeEquals(row.RowVersion, expected))
                    return Error<AcademicSectionDto>("Section changed. Reload before saving.", 409);
                if (request.Capacity < row.Capacity || !request.IsActive && row.IsActive)
                {
                    if (await ReferencedAsync(id, tenant, token))
                        return Error<AcademicSectionDto>("Section has linked transactions; change capacity/status through controlled reconciliation.", 409);
                }
                // Section capacity belongs to the class group, not to an optional cohort.
                // The update cannot change Campus/Class; moving students requires a controlled transfer.
                row.Name = name;
                row.Capacity = request.Capacity;
                row.DisplayOrder = request.DisplayOrder;
                row.IsActive = request.IsActive;
                row.UpdatedAt = DateTime.UtcNow;
                row.UpdatedBy = _user.UserId;
                _sections.Update(row);
                await _uow.SaveChangesAsync(token);
                return ApiResponse<AcademicSectionDto>.SuccessResponse(Map(row), "Section saved.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error<AcademicSectionDto>("Section was modified concurrently. Reload.", 409);
        }
    }

    public async Task<ApiResponse<bool>> DeleteAsync(long id,
        DeleteAcademicSectionRequestDto request, CancellationToken ct)
    {
        if (!Allowed) return Error<bool>("Section administration requires tenant administrator access.", 403);
        var expected = Version(request?.RowVersion);
        if (id <= 0 || expected == null) return Error<bool>("Section ID and version are required.");
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var tenant = _user.TenantId;
                var row = await _sections.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == id, token);
                if (row == null) return Error<bool>("Section not found.", 404);
                if (row.IsDeleted) return ApiResponse<bool>.SuccessResponse(true, "Section already removed.");
                if (!CryptographicOperations.FixedTimeEquals(row.RowVersion, expected))
                    return Error<bool>("Section changed. Reload before deleting.", 409);
                if (await ReferencedAsync(id, tenant, token))
                    return Error<bool>("Section has linked data. Delete is not allowed.", 409);
                row.IsActive = false;
                row.IsDeleted = true;
                row.DeletedAt = DateTime.UtcNow;
                row.DeletedBy = _user.UserId;
                _sections.Update(row);
                await _uow.SaveChangesAsync(token);
                return ApiResponse<bool>.SuccessResponse(true, "Unused section removed.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error<bool>("Section changed concurrently. Reload.", 409);
        }
    }
}
