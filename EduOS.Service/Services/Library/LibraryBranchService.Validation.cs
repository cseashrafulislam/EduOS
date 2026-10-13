using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Library;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
namespace EduOS.Service.Services.Library;
public sealed partial class LibraryBranchService
{
    private async Task<ApiResponse<LibraryBranchDto>> ApplyBranchAsync(LibraryBranch? row,
        SaveLibraryBranchRequestDto request, string name, string code, CancellationToken ct)
    {
        var tenant = _user.TenantId;
        if (row != null && row.CampusId != request.CampusId &&
            await _copies.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.LibraryBranchId == row.Id && !x.IsDeleted, ct))
            return ApiResponse<LibraryBranchDto>.ErrorResponse("Move copies before changing campus.", 409);
        if (row != null && row.IsActive && !request.IsActive &&
            await _copies.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.LibraryBranchId == row.Id && !x.IsDeleted &&
                x.State != BookCopyState.Retired, ct))
            return ApiResponse<LibraryBranchDto>.ErrorResponse("Move or retire active copies before deactivation.", 409);
        var now = _clock.GetUtcNow().UtcDateTime;
        if (row == null)
        {
            row = new LibraryBranch { TenantId = tenant, CreatedAt = now, CreatedBy = _user.UserId };
            await _branches.AddAsync(row);
        }
        else { row.UpdatedAt = now; row.UpdatedBy = _user.UserId; _branches.Update(row); }
        row.CampusId = request.CampusId; row.Name = name; row.Code = code;
        row.Location = Normalize(request.Location); row.IsActive = request.IsActive;
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<LibraryBranchDto>.SuccessResponse(Map(row), "Branch saved.");
    }
    private static string? Normalize(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    private static bool VersionMatches(byte[] actual, string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded)) return false;
        try { return actual.AsSpan().SequenceEqual(Convert.FromBase64String(encoded)); }
        catch (FormatException) { return false; }
    }
}