using System.Data;
using System.Security.Cryptography;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Academic;

/// <summary>Explicit sections owned by an academic batch. Legacy compatibility rows are immutable.</summary>
public sealed class AcademicSectionService
{
    private readonly IGenericRepository<AcademicSection> _sections;
    private readonly IGenericRepository<AcademicBatch> _batches;
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
        ICurrentUserService user, IUnitOfWork uow)
    {
        _sections = sections;
        _batches = batches;
        _enrollments = enrollments;
        _offerings = offerings;
        _attendance = attendance;
        _publications = publications;
        _fees = fees;
        _user = user;
        _uow = uow;
    }

    private bool Allowed => _user.IsAuthenticated && _user.IsTenantAdmin && _user.TenantId > 0;
    private static ApiResponse<T> Error<T>(string message, int code = 400) =>
        ApiResponse<T>.ErrorResponse(message, code);
    private static AcademicSectionDto Map(AcademicSection section, string batchName = "") => new()
    {
        Id = section.Id,
        AcademicBatchId = section.AcademicBatchId,
        AcademicBatchName = batchName,
        Name = section.Name,
        Code = section.Code,
        Capacity = section.Capacity,
        IsLegacyDefault = section.IsLegacyDefault,
        IsActive = section.IsActive,
        DisplayOrder = section.DisplayOrder,
        RowVersion = Convert.ToBase64String(section.RowVersion)
    };

    public async Task<ApiResponse<PagedResult<AcademicSectionDto>>> ListAsync(
        long? academicBatchId, string? search, int page, int pageSize, CancellationToken ct)
    {
        if (!Allowed) return Error<PagedResult<AcademicSectionDto>>("Section administration requires tenant administrator access.", 403);
        if (academicBatchId is <= 0 || page < 1 || pageSize is < 1 or > 100 ||
            search?.Length > 80 || (long)(page - 1) * pageSize > int.MaxValue)
            return Error<PagedResult<AcademicSectionDto>>("Invalid section filters.");
        var tenant = _user.TenantId;
        var query = _sections.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted);
        if (academicBatchId.HasValue)
            query = query.Where(x => x.AcademicBatchId == academicBatchId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var prefix = search.Trim();
            query = query.Where(x => x.Name.StartsWith(prefix) || x.Code.StartsWith(prefix));
        }
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var batchIds = rows.Select(x => x.AcademicBatchId).Distinct().ToArray();
        var names = await _batches.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && batchIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return ApiResponse<PagedResult<AcademicSectionDto>>.SuccessResponse(new PagedResult<AcademicSectionDto>
        {
            Page = page, PageSize = pageSize, TotalCount = count,
            Items = rows.Select(x => Map(x, names.GetValueOrDefault(x.AcademicBatchId) ?? "")).ToList()
        });
    }

    public async Task<ApiResponse<AcademicSectionDto>> GetAsync(long id, CancellationToken ct)
    {
        if (!Allowed) return Error<AcademicSectionDto>("Section administration requires tenant administrator access.", 403);
        if (id <= 0) return Error<AcademicSectionDto>("Invalid section ID.");
        var tenant = _user.TenantId;
        var row = await _sections.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == id && !x.IsDeleted, ct);
        if (row == null) return Error<AcademicSectionDto>("Section not found.", 404);
        var batchName = await _batches.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.Id == row.AcademicBatchId)
            .Select(x => x.Name).FirstOrDefaultAsync(ct);
        return ApiResponse<AcademicSectionDto>.SuccessResponse(Map(row, batchName ?? ""));
    }

    public async Task<ApiResponse<AcademicSectionDto>> CreateAsync(
        CreateAcademicSectionRequestDto request, CancellationToken ct)
    {
        if (!Allowed) return Error<AcademicSectionDto>("Section administration requires tenant administrator access.", 403);
        if (request == null || request.ClientRequestId == Guid.Empty || request.AcademicBatchId <= 0 ||
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
                    if (replay.AcademicBatchId != request.AcademicBatchId || replay.Name != name ||
                        replay.Code != code || replay.Capacity != request.Capacity ||
                        replay.DisplayOrder != request.DisplayOrder || replay.IsDeleted)
                        return Error<AcademicSectionDto>("Request ID already used for another section.", 409);
                    return ApiResponse<AcademicSectionDto>.SuccessResponse(Map(replay), "Section already saved.");
                }
                var batch = await _batches.GetQueryable().AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == request.AcademicBatchId &&
                        x.IsActive && !x.IsDeleted, token);
                if (batch == null) return Error<AcademicSectionDto>("Active academic batch not found.", 404);
                if (request.Capacity > batch.Capacity)
                    return Error<AcademicSectionDto>("Section capacity exceeds academic batch capacity.", 409);
                if (await _sections.GetQueryable().AnyAsync(x => x.TenantId == tenant &&
                    x.AcademicBatchId == batch.Id && x.Code == code && !x.IsDeleted, token))
                    return Error<AcademicSectionDto>("Section code already exists in this batch.", 409);
                var row = new AcademicSection
                {
                    TenantId = tenant, AcademicBatchId = batch.Id, ClientRequestId = request.ClientRequestId,
                    Name = name, Code = code, Capacity = request.Capacity,
                    IsActive = true, DisplayOrder = request.DisplayOrder,
                    CreatedBy = _user.UserId, CreatedAt = DateTime.UtcNow
                };
                await _sections.AddAsync(row);
                await _uow.SaveChangesAsync(token);
                return ApiResponse<AcademicSectionDto>.SuccessResponse(Map(row, batch.Name), "Section created.");
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
                if (row.IsLegacyDefault)
                    return Error<AcademicSectionDto>("Legacy section metadata is managed through its existing batch record.", 409);
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
                var batch = await _batches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == row.AcademicBatchId, token);
                if (batch == null || request.Capacity > batch.Capacity)
                    return Error<AcademicSectionDto>("Section capacity exceeds its parent academic batch.", 409);
                row.Name = name;
                row.Capacity = request.Capacity;
                row.DisplayOrder = request.DisplayOrder;
                row.IsActive = request.IsActive;
                row.UpdatedAt = DateTime.UtcNow;
                row.UpdatedBy = _user.UserId;
                _sections.Update(row);
                await _uow.SaveChangesAsync(token);
                return ApiResponse<AcademicSectionDto>.SuccessResponse(Map(row, batch.Name), "Section saved.");
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
                if (row.IsLegacyDefault) return Error<bool>("Historical section cannot be deleted.", 409);
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
