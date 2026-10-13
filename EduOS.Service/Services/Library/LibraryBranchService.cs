using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
namespace EduOS.Service.Services.Library;
public sealed partial class LibraryBranchService : ILibraryBranchService
{
    private readonly IGenericRepository<LibraryBranch> _branches;
    private readonly IGenericRepository<Campus> _campuses;
    private readonly IGenericRepository<BookCopy> _copies;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<LibraryBranchService> _logger;
    public LibraryBranchService(IGenericRepository<LibraryBranch> branches,
        IGenericRepository<Campus> campuses, IGenericRepository<BookCopy> copies,
        IUnitOfWork uow, ICurrentUserService user, TimeProvider clock, ILogger<LibraryBranchService> logger)
    {
        _branches = branches; _campuses = campuses; _copies = copies;
        _uow = uow; _user = user; _clock = clock; _logger = logger;
    }
    private bool CanManage() => _user.IsAuthenticated && _user.TenantId > 0 && _user.IsTenantAdmin;
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Library branch administrator permission required.", 403);
    private static LibraryBranchDto Map(LibraryBranch x) => new()
    {
        Id = x.Id, CampusId = x.CampusId, Name = x.Name, Code = x.Code,
        Location = x.Location, IsActive = x.IsActive, RowVersion = Convert.ToBase64String(x.RowVersion)
    };
    public async Task<ApiResponse<IReadOnlyList<LibraryCampusOptionDto>>> CampusesAsync(CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<IReadOnlyList<LibraryCampusOptionDto>>();
        var rows = await _campuses.GetQueryable().AsNoTracking().Where(x => x.TenantId == _user.TenantId &&
            x.IsActive && !x.IsDeleted).OrderBy(x => x.Name).ThenBy(x => x.Id).Take(1000)
            .Select(x => new LibraryCampusOptionDto { Id = x.Id, Name = x.Name, Code = x.Code }).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<LibraryCampusOptionDto>>.SuccessResponse(rows);
    }
    public async Task<ApiResponse<IReadOnlyList<LibraryBranchDto>>> ListAsync(long? campusId, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<IReadOnlyList<LibraryBranchDto>>();
        if (campusId is <= 0) return ApiResponse<IReadOnlyList<LibraryBranchDto>>.ErrorResponse("Invalid campus.");
        var query = _branches.GetQueryable().AsNoTracking().Where(x => x.TenantId == _user.TenantId && !x.IsDeleted);
        if (campusId.HasValue) query = query.Where(x => x.CampusId == campusId.Value);
        var rows = await query.OrderBy(x => x.CampusId).ThenBy(x => x.Name).ThenBy(x => x.Id).Take(1000).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<LibraryBranchDto>>.SuccessResponse(rows.Select(Map).ToList());
    }
}