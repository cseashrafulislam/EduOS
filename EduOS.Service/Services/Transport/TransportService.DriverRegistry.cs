using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace EduOS.Service.Services.Transport;

/// <summary>TenantAdmin-only driver registry. Drivers have no canonical CampusId;
/// campus operators must not receive tenant-wide identity, phone or license data.</summary>
public sealed partial class TransportService
{
    public async Task<ApiResponse<PagedResult<TransportDriverDto>>> GetDriversAsync(
        int page, int pageSize, string? search, CancellationToken ct = default)
    {
        if (!CanManageDriverRegistry()) return Denied<PagedResult<TransportDriverDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            search?.Trim().Length > 100)
            return ApiResponse<PagedResult<TransportDriverDto>>.ErrorResponse("Invalid driver filter.");
        var tenant = _currentUser.TenantId;
        var query = _drivers.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant && !x.IsDeleted);
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.FullName.Contains(term) ||
                (x.LicenseNumber != null && x.LicenseNumber.Contains(term)));
        var total = await query.CountAsync(ct);
        var drivers = await query.OrderBy(x => x.FullName).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<TransportDriverDto>>.SuccessResponse(new PagedResult<TransportDriverDto>
        {
            Items = drivers.Select(MapDriver).ToList(), TotalCount = total, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<TransportDriverDto>> SaveDriverAsync(
        long? driverId, SaveTransportDriverRequestDto request, CancellationToken ct = default)
    {
        if (!CanManageDriverRegistry()) return Denied<TransportDriverDto>();
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        if (request == null || driverId is <= 0 || string.IsNullOrWhiteSpace(request.FullName) ||
            request.FullName.Trim().Length > 200 || string.IsNullOrWhiteSpace(request.LicenseNumber) ||
            request.LicenseNumber.Trim().Length > 100 || request.Phone?.Trim().Length > 30 ||
            request.EmployeeId.HasValue || (request.IsActive &&
                (!request.LicenseExpiresOn.HasValue || request.LicenseExpiresOn.Value < today)))
            return ApiResponse<TransportDriverDto>.ErrorResponse(
                "Name, license and valid expiry are required. Employee linking awaits Platform verification.");
        if (driverId.HasValue && !TryDecodeVersion(request.RowVersion, out _))
            return ApiResponse<TransportDriverDto>.ErrorResponse("Reload the driver to obtain a row version.");
        var tenant = _currentUser.TenantId;
        var name = request.FullName.Trim();
        var license = request.LicenseNumber.Trim().ToUpperInvariant();
        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var duplicate = await _drivers.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.LicenseNumber != null &&
                    x.LicenseNumber.ToUpper() == license, token);
                if (!driverId.HasValue)
                {
                    if (duplicate != null)
                    {
                        if (duplicate.FullName != name || duplicate.Phone != phone ||
                            duplicate.LicenseExpiresOn != request.LicenseExpiresOn ||
                            duplicate.IsActive != request.IsActive)
                            return ApiResponse<TransportDriverDto>.ErrorResponse(
                                "License already belongs to another driver or has different details.", 409);
                        return ApiResponse<TransportDriverDto>.SuccessResponse(
                            MapDriver(duplicate), "Driver already exists; no duplicate created.");
                    }
                    var now = _clock.GetUtcNow().UtcDateTime;
                    var driver = new TransportDriver
                    {
                        TenantId = tenant, FullName = name, Phone = phone, LicenseNumber = license,
                        LicenseExpiresOn = request.LicenseExpiresOn, IsActive = request.IsActive,
                        CreatedAt = now, CreatedBy = _currentUser.UserId
                    };
                    await _drivers.AddAsync(driver);
                    await _unitOfWork.SaveChangesAsync(token);
                    return new ApiResponse<TransportDriverDto>
                    {
                        Success = true, StatusCode = 201, Message = "Driver registered.",
                        Data = MapDriver(driver)
                    };
                }
                var existing = await _drivers.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == driverId.Value && !x.IsDeleted, token);
                if (existing == null)
                    return ApiResponse<TransportDriverDto>.ErrorResponse("Driver not found.", 404);
                if (duplicate != null && duplicate.Id != existing.Id)
                    return ApiResponse<TransportDriverDto>.ErrorResponse("License is already registered.", 409);
                if (!TryDecodeVersion(request.RowVersion, out var version) ||
                    !existing.RowVersion.AsSpan().SequenceEqual(version))
                    return ApiResponse<TransportDriverDto>.ErrorResponse("Driver changed. Reload and retry.", 409);
                if (!request.IsActive && existing.IsActive &&
                    await _driverLinks.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && !x.IsDeleted && x.TransportDriverId == existing.Id &&
                        x.IsCurrent && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= today), token))
                    return ApiResponse<TransportDriverDto>.ErrorResponse(
                        "Close all current vehicle assignments before deactivating the driver.", 409);
                if (existing.FullName == name && existing.Phone == phone &&
                    existing.LicenseNumber == license && existing.LicenseExpiresOn == request.LicenseExpiresOn &&
                    existing.IsActive == request.IsActive)
                    return ApiResponse<TransportDriverDto>.SuccessResponse(
                        MapDriver(existing), "Driver already has the requested details.");
                existing.FullName = name;
                existing.Phone = phone;
                existing.LicenseNumber = license;
                existing.LicenseExpiresOn = request.LicenseExpiresOn;
                existing.IsActive = request.IsActive;
                existing.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                existing.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(token);
                return ApiResponse<TransportDriverDto>.SuccessResponse(MapDriver(existing), "Driver updated.");
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<TransportDriverDto>.ErrorResponse("Driver changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Driver registry conflict for tenant {TenantId}", tenant);
            return ApiResponse<TransportDriverDto>.ErrorResponse("Driver update conflicts with another request.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Driver registry transaction aborted for tenant {TenantId}", tenant);
            return ApiResponse<TransportDriverDto>.ErrorResponse("Driver update conflicts with another request.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Driver registry failed for tenant {TenantId}", tenant);
            return ApiResponse<TransportDriverDto>.ErrorResponse("Driver could not be saved.", 500);
        }
    }

    private bool CanManageDriverRegistry() => CanRead() && _currentUser.IsTenantAdmin;
    private static TransportDriverDto MapDriver(TransportDriver x) => new()
    {
        Id = x.Id, EmployeeId = x.EmployeeId, FullName = x.FullName, Phone = x.Phone,
        LicenseNumber = x.LicenseNumber, LicenseExpiresOn = x.LicenseExpiresOn,
        IsActive = x.IsActive, RowVersion = Convert.ToBase64String(x.RowVersion)
    };
}
