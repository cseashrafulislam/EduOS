using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Academic;

public sealed class ClassService : IClassService
{
    private readonly IClassRepository _classes;
    private readonly IGenericRepository<AcademicProgram> _programs;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _user;
    private readonly ILogger<ClassService> _logger;

    public ClassService(IClassRepository classes, IGenericRepository<AcademicProgram> programs,
        IUnitOfWork unitOfWork, ICurrentUserService user, ILogger<ClassService> logger)
    {
        _classes = classes;
        _programs = programs;
        _unitOfWork = unitOfWork;
        _user = user;
        _logger = logger;
    }

    public async Task<ApiResponse<PagedResult<ClassDto>>> GetAllAsync(ClassListFilterDto filter)
    {
        if (!CanRead()) return ApiResponse<PagedResult<ClassDto>>.ErrorResponse("Academic access is required.", 403);
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var query = _classes.GetQueryable().AsNoTracking().Where(x => x.TenantId == _user.TenantId);
        if (filter.IsActive.HasValue) query = query.Where(x => x.IsActive == filter.IsActive.Value);
        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var search = filter.SearchTerm.Trim();
            query = query.Where(x => x.Name.Contains(search) || x.Code.Contains(search));
        }
        var total = await query.CountAsync();
        var rows = await query.OrderBy(x => x.AcademicProgramId).ThenBy(x => x.LevelNo).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return ApiResponse<PagedResult<ClassDto>>.SuccessResponse(new PagedResult<ClassDto>
        {
            Items = rows.Select(ToDto).ToList(), TotalCount = total, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<ClassDto>> GetByIdAsync(long id)
    {
        if (!CanRead()) return ApiResponse<ClassDto>.ErrorResponse("Academic access is required.", 403);
        var row = await _classes.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == _user.TenantId && x.Id == id);
        return row == null ? ApiResponse<ClassDto>.ErrorResponse("Academic level not found.", 404)
            : ApiResponse<ClassDto>.SuccessResponse(ToDto(row));
    }

    public async Task<ApiResponse<ClassDto>> CreateAsync(ClassCreateDto dto)
    {
        if (!CanManage()) return ApiResponse<ClassDto>.ErrorResponse("Academic administration permission is required.", 403);
        var error = Validate(dto.Name, dto.NumericValue);
        if (error != null) return ApiResponse<ClassDto>.ErrorResponse(error, 400);
        var code = dto.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (dto.AcademicProgramId <= 0 || code.Length == 0 || code.Length > 50
            || code.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            return ApiResponse<ClassDto>.ErrorResponse("Valid program ID and academic level code are required.", 400);
        var tenantId = _user.TenantId;
        var programExists = await _programs.GetQueryable().AsNoTracking()
            .AnyAsync(x => x.TenantId == tenantId && x.Id == dto.AcademicProgramId && x.IsActive);
        if (!programExists) return ApiResponse<ClassDto>.ErrorResponse("Academic program not found.", 404);
        var duplicate = await _classes.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenantId && x.AcademicProgramId == dto.AcademicProgramId
            && (x.Code == code || x.Name == dto.Name.Trim()));
        if (duplicate) return ApiResponse<ClassDto>.ErrorResponse("Level name or code already exists in this program.", 409);
        var now = DateTime.UtcNow;
        var row = new AcademicLevel
        {
            TenantId = tenantId, AcademicProgramId = dto.AcademicProgramId, Code = code,
            Name = dto.Name.Trim(), LevelNo = dto.NumericValue, IsActive = dto.IsActive,
            CreatedAt = now, CreatedBy = _user.UserId
        };
        try
        {
            await _classes.AddAsync(row);
            await _unitOfWork.SaveChangesAsync();
            return ApiResponse<ClassDto>.SuccessResponse(ToDto(row), "Academic level created.");
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Academic level insert rejected for tenant {TenantId}", tenantId);
            return ApiResponse<ClassDto>.ErrorResponse("Academic level already exists or has invalid references.", 409);
        }
    }

    public async Task<ApiResponse<ClassDto>> UpdateAsync(long id, ClassUpdateDto dto)
    {
        if (!CanManage()) return ApiResponse<ClassDto>.ErrorResponse("Academic administration permission is required.", 403);
        var error = Validate(dto.Name, dto.NumericValue);
        if (error != null) return ApiResponse<ClassDto>.ErrorResponse(error, 400);
        var row = await _classes.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == _user.TenantId && x.Id == id);
        if (row == null) return ApiResponse<ClassDto>.ErrorResponse("Academic level not found.", 404);
        if (string.IsNullOrWhiteSpace(dto.RowVersion))
            return ApiResponse<ClassDto>.ErrorResponse("RowVersion is required for safe updates.", 400);
        byte[] version;
        try { version = Convert.FromBase64String(dto.RowVersion); }
        catch (FormatException) { return ApiResponse<ClassDto>.ErrorResponse("Invalid RowVersion.", 400); }
        if (!row.RowVersion.SequenceEqual(version))
            return ApiResponse<ClassDto>.ErrorResponse("Academic level was modified by another user.", 409);
        if (await _classes.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == _user.TenantId
            && x.AcademicProgramId == row.AcademicProgramId && x.Id != id && x.Name == dto.Name.Trim()))
            return ApiResponse<ClassDto>.ErrorResponse("Level name already exists in this program.", 409);
        row.Name = dto.Name.Trim();
        row.LevelNo = dto.NumericValue;
        row.IsActive = dto.IsActive;
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedBy = _user.UserId;
        try
        {
            await _unitOfWork.SaveChangesAsync();
            return ApiResponse<ClassDto>.SuccessResponse(ToDto(row), "Academic level updated.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<ClassDto>.ErrorResponse("Academic level was modified by another user.", 409);
        }
    }

    public async Task<ApiResponse<bool>> DeleteAsync(long id)
    {
        if (!CanManage()) return ApiResponse<bool>.ErrorResponse("Academic administration permission is required.", 403);
        var row = await _classes.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == _user.TenantId && x.Id == id);
        if (row == null) return ApiResponse<bool>.ErrorResponse("Academic level not found.", 404);
        if (!row.IsActive) return ApiResponse<bool>.SuccessResponse(true, "Academic level already inactive.");
        row.IsActive = false; // Do not destroy historical enrollment/report references.
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedBy = _user.UserId;
        try
        {
            await _unitOfWork.SaveChangesAsync();
            return ApiResponse<bool>.SuccessResponse(true, "Academic level deactivated.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<bool>.ErrorResponse("Academic level was modified by another user.", 409);
        }
    }

    public async Task<ApiResponse<List<ClassDto>>> GetActiveClassesAsync()
    {
        if (!CanRead()) return ApiResponse<List<ClassDto>>.ErrorResponse("Academic access is required.", 403);
        var rows = await _classes.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _user.TenantId && x.IsActive)
            .OrderBy(x => x.AcademicProgramId).ThenBy(x => x.LevelNo).ThenBy(x => x.Id)
            .Take(500).ToListAsync();
        return ApiResponse<List<ClassDto>>.SuccessResponse(rows.Select(ToDto).ToList());
    }

    private bool CanRead() => _user.IsAuthenticated && _user.TenantId > 0;
    private bool CanManage() => CanRead() && (_user.IsTenantAdmin || _user.IsInRole("Principal") || _user.IsInRole("AcademicAdmin"));
    private static string? Validate(string? name, int levelNo) =>
        string.IsNullOrWhiteSpace(name) || name.Trim().Length > 150 ? "Academic level name is invalid." :
        levelNo < 0 ? "Academic level number cannot be negative." : null;

    private static ClassDto ToDto(AcademicLevel row) => new ClassDto
    {
        Id = row.Id, AcademicProgramId = row.AcademicProgramId, Code = row.Code,
        Name = row.Name, NumericValue = row.LevelNo, IsActive = row.IsActive,
        CreatedAt = row.CreatedAt, RowVersion = Convert.ToBase64String(row.RowVersion)
    };
}
