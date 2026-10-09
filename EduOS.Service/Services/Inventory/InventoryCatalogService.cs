using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Inventory;

public sealed partial class InventoryCatalogService : IInventoryCatalogService
{
    private readonly EduOSDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<InventoryCatalogService> _logger;

    public InventoryCatalogService(EduOSDbContext db, ICurrentUserService user, TimeProvider clock, ILogger<InventoryCatalogService> logger)
    {
        _db = db; _user = user; _clock = clock; _logger = logger;
    }

    private bool CanRead() => _user.IsAuthenticated && _user.TenantId > 0 &&
        (_user.IsTenantAdmin || _user.IsInRole("Principal") || _user.IsInRole("InventoryManager") ||
         _user.IsInRole("StoreKeeper") || _user.IsInRole("Accountant"));
    private bool CanWrite() => _user.IsAuthenticated && _user.TenantId > 0 &&
        (_user.IsTenantAdmin || _user.IsInRole("Principal") || _user.IsInRole("InventoryManager"));
    private bool CanAccessAllCampuses() => _user.IsTenantAdmin || _user.IsInRole("Principal");
    private Task<bool> CanAccessCampusAsync(long campusId, CancellationToken ct) =>
        CanAccessAllCampuses() ? Task.FromResult(true) : _db.UserCampusAccesses.AsNoTracking()
            .AnyAsync(x => x.TenantId == _user.TenantId && x.UserId == _user.UserId &&
                x.CampusId == campusId && x.IsActive && !x.IsDeleted, ct);
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Inventory access denied.", 403);
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Code(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();
    private static bool VersionMatches(byte[] actual, string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied)) return false;
        try { return actual.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
    }
    private static InventoryItemDto Map(InventoryItem x) => new()
    {
        Id = x.Id, Reference = x.PublicId, Name = x.Name, Code = x.Code, UnitCode = x.UnitCode,
        CategoryCode = x.CategoryCode, IsStockTracked = x.IsStockTracked, IsAssetTracked = x.IsAssetTracked,
        ReorderLevel = x.ReorderLevel, IsActive = x.IsActive, RowVersion = Convert.ToBase64String(x.RowVersion)
    };
    private static InventoryLocationDto Map(InventoryLocation x) => new()
    {
        Id = x.Id, CampusId = x.CampusId, Code = x.Code, Name = x.Name,
        IsActive = x.IsActive, RowVersion = Convert.ToBase64String(x.RowVersion)
    };
}
