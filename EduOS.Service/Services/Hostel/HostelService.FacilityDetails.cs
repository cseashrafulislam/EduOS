using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    /// <summary>Edits descriptive facility fields without moving the campus, changing the code or touching rent.</summary>
    public async Task<ApiResponse<HostelFacilityDto>> UpdateFacilityAsync(Guid reference,
        UpdateHostelFacilityRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<HostelFacilityDto>();
        if (reference == Guid.Empty || request == null || string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Trim().Length is < 2 or > 150 || request.GenderRestriction?.Trim().Length > 50 ||
            request.Address?.Trim().Length > 500 ||
            !TryDecodeVersion(request.RowVersion, out var version) || version.Length != 8)
            return ApiResponse<HostelFacilityDto>.ErrorResponse("Valid facility details and row version are required.");
        var name = request.Name.Trim();
        var gender = string.IsNullOrWhiteSpace(request.GenderRestriction) ? null : request.GenderRestriction.Trim();
        var address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
        if (gender != null && gender is not ("Male" or "Female" or "Other"))
            return ApiResponse<HostelFacilityDto>.ErrorResponse("Invalid gender restriction.");
        var tenant = _currentUser.TenantId;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var hostel = await _hostels.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.PublicId == reference, token);
                if (hostel == null) return ApiResponse<HostelFacilityDto>.ErrorResponse("Facility not found.", 404);
                var campus = await _campuses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == hostel.CampusId && !x.IsDeleted && x.IsActive, token);
                if (campus == null) return ApiResponse<HostelFacilityDto>.ErrorResponse("Active campus not found.", 404);
                if (!await CanManageCampusAsync(campus.Id, tenant, token)) return Denied<HostelFacilityDto>();

                HostelFacilityDto Result() => new()
                {
                    Reference = hostel.PublicId, CampusId = campus.Id, CampusName = campus.Name,
                    Name = hostel.Name, Code = hostel.Code, GenderRestriction = hostel.GenderRestriction,
                    Address = hostel.Address, IsActive = hostel.IsActive,
                    RowVersion = Convert.ToBase64String(hostel.RowVersion)
                };
                if (hostel.Name == name && hostel.GenderRestriction == gender && hostel.Address == address)
                    return ApiResponse<HostelFacilityDto>.SuccessResponse(Result(), "Facility details already match.");
                if (!hostel.RowVersion.AsSpan().SequenceEqual(version))
                    return ApiResponse<HostelFacilityDto>.ErrorResponse("Facility changed. Reload and retry.", 409);

                // A change of eligibility rules must not invalidate current, future or unclosed allocations.
                if (hostel.GenderRestriction != gender)
                {
                    var hasActiveAllocations = await (from allocation in _allocations.GetQueryable().AsNoTracking()
                        join bed in _beds.GetQueryable().AsNoTracking() on allocation.HostelBedId equals bed.Id
                        join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
                        where allocation.TenantId == tenant && bed.TenantId == tenant && room.TenantId == tenant &&
                            !allocation.IsDeleted && !bed.IsDeleted && !room.IsDeleted &&
                            room.HostelId == hostel.Id && allocation.State == HostelAllocationState.Active
                        select allocation.Id).AnyAsync(token);
                    if (hasActiveAllocations)
                        return ApiResponse<HostelFacilityDto>.ErrorResponse(
                            "Close active allocations before changing the gender restriction.", 409);
                }

                hostel.Name = name;
                hostel.GenderRestriction = gender;
                hostel.Address = address;
                hostel.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                hostel.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(token);
                return ApiResponse<HostelFacilityDto>.SuccessResponse(Result(), "Facility details updated.");
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<HostelFacilityDto>.ErrorResponse("Facility changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Hostel facility details conflict for tenant {TenantId}", tenant);
            return ApiResponse<HostelFacilityDto>.ErrorResponse("Facility details conflict with another change.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hostel facility details update failed for tenant {TenantId}", tenant);
            return ApiResponse<HostelFacilityDto>.ErrorResponse("Facility details update failed.", 500);
        }
    }
}
