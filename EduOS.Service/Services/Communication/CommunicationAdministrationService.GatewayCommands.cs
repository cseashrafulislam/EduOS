using Microsoft.AspNetCore.DataProtection;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Communication;
using EduOS.Core.Entities.Communication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.DataProtection;

namespace EduOS.Service.Services.Communication;

public sealed partial class CommunicationAdministrationService
{
    public async Task<ApiResponse<CommunicationGatewayDto>> SaveGatewayAsync(long? gatewayId,
        SaveCommunicationGatewayRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return ApiResponse<CommunicationGatewayDto>.ErrorResponse("Tenant administrator required.", 403);
        if (request == null || string.IsNullOrWhiteSpace(request.ProviderCode) || request.ProviderCode.Trim().Length > 100
            || !ValidChannel(request.Channel) || request.Endpoint?.Length > 1000 || request.Credential?.Length > 4000
            || (request.ClearCredential && !string.IsNullOrEmpty(request.Credential)))
            return ApiResponse<CommunicationGatewayDto>.ErrorResponse("Invalid gateway.");
        if (!string.IsNullOrWhiteSpace(request.Endpoint) &&
            (!Uri.TryCreate(request.Endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
             || !string.IsNullOrEmpty(uri.UserInfo) || uri.IsLoopback))
            return ApiResponse<CommunicationGatewayDto>.ErrorResponse("External HTTPS endpoint required.");
        var code = request.ProviderCode.Trim().ToUpperInvariant();
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var query = _gateways.GetQueryable().Where(x => x.TenantId == _user.TenantId);
                var row = gatewayId.HasValue
                    ? await query.FirstOrDefaultAsync(x => x.Id == gatewayId.Value, token)
                    : await query.FirstOrDefaultAsync(x => x.ProviderCode == code && x.Channel == request.Channel, token);
                if (!gatewayId.HasValue && row != null) return ApiResponse<CommunicationGatewayDto>.ErrorResponse("Gateway already exists.", 409);
                if (gatewayId.HasValue && row == null) return ApiResponse<CommunicationGatewayDto>.ErrorResponse("Not found.", 404);
                if (gatewayId.HasValue && !MatchesVersion(row!.RowVersion, request.RowVersion))
                    return ApiResponse<CommunicationGatewayDto>.ErrorResponse("Reload changed gateway.", 409);
                if (row != null && (row.ProviderCode != code || row.Channel != request.Channel))
                    return ApiResponse<CommunicationGatewayDto>.ErrorResponse("Gateway identity is immutable.", 409);
                row ??= new CommunicationGateway { TenantId = _user.TenantId, ProviderCode = code,
                    Channel = request.Channel, CreatedBy = _user.UserId };
                row.Endpoint = string.IsNullOrWhiteSpace(request.Endpoint) ? null : request.Endpoint.Trim();
                row.IsActive = request.IsActive; row.IsDefault = request.IsDefault && request.IsActive;
                if (request.ClearCredential) row.ProtectedCredential = null;
                else if (!string.IsNullOrWhiteSpace(request.Credential) && request.Credential != "********")
                    row.ProtectedCredential = _protector.Protect(request.Credential);
                if (row.IsDefault)
                {
                    var others = await query.Where(x => x.Channel == row.Channel && x.IsDefault && x.Id != row.Id).ToListAsync(token);
                    foreach (var other in others) { other.IsDefault = false; other.UpdatedAt = DateTime.UtcNow; other.UpdatedBy = _user.UserId; }
                }
                if (row.Id == 0) await _gateways.AddAsync(row);
                else { row.UpdatedAt = DateTime.UtcNow; row.UpdatedBy = _user.UserId; }
                await _unitOfWork.SaveChangesAsync(token);
                return ApiResponse<CommunicationGatewayDto>.SuccessResponse(Map(row));
            }, ct);
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<CommunicationGatewayDto>.ErrorResponse("Reload changed gateway.", 409); }
        catch (DbUpdateException) { return ApiResponse<CommunicationGatewayDto>.ErrorResponse("Conflicting gateway update.", 409); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Gateway save failed for tenant {TenantId}", _user.TenantId);
            return ApiResponse<CommunicationGatewayDto>.ErrorResponse("Gateway save failed.", 500);
        }
    }
}
