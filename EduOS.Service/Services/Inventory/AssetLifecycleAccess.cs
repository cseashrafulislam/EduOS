using EduOS.Core.Common;
using EduOS.Core.Entities.Inventory;
using Microsoft.EntityFrameworkCore;
namespace EduOS.Service.Services.Inventory;
public sealed partial class AssetLifecycleService
{
    private long Tenant => _user.TenantId;
    private DateOnly Today => DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;
    private bool AllCampuses() => _user.IsTenantAdmin || _user.IsInRole("Principal");
    private bool CanRead() => _user.IsAuthenticated && Tenant > 0 &&
        (AllCampuses() || _user.IsInRole("InventoryManager") || _user.IsInRole("StoreKeeper") || _user.IsInRole("Accountant"));
    private bool CanWrite() => _user.IsAuthenticated && Tenant > 0 &&
        (AllCampuses() || _user.IsInRole("InventoryManager"));
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Asset access denied.", 403);
    private static ApiResponse<T> Invalid<T>(string message) => ApiResponse<T>.ErrorResponse(message);
    private static string Code(string? value) => (value ?? "").Trim().ToUpperInvariant();
    private static string Text(string? value) => (value ?? "").Trim();
    private static bool Version(byte[] current, string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied)) return false;
        try { return current.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
    }
    private static bool Money(decimal value) => value >= 0m && value <= 999999999999m &&
        decimal.Round(value, 2, MidpointRounding.ToZero) == value;
    private Task<bool> CampusAllowedAsync(long? campusId, CancellationToken ct)
    {
        if (!campusId.HasValue) return Task.FromResult(AllCampuses());
        return _db.Campuses.AsNoTracking().AnyAsync(c => c.TenantId == Tenant && c.Id == campusId.Value &&
            c.IsActive && !c.IsDeleted && (AllCampuses() || _db.UserCampusAccesses.Any(a =>
                a.TenantId == Tenant && a.UserId == _user.UserId && a.CampusId == c.Id &&
                a.IsActive && !a.IsDeleted)), ct);
    }
    private IQueryable<Asset> VisibleAssets()
    {
        var q = _db.Assets.Where(x => x.TenantId == Tenant && !x.IsDeleted);
        if (AllCampuses()) return q;
        return q.Where(x => x.CampusId.HasValue && _db.UserCampusAccesses.Any(a =>
            a.TenantId == Tenant && a.UserId == _user.UserId && a.CampusId == x.CampusId &&
            a.IsActive && !a.IsDeleted && _db.Campuses.Any(c =>
                c.TenantId == Tenant && c.Id == a.CampusId && c.IsActive && !c.IsDeleted)));
    }
}
