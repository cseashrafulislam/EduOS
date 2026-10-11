using EduOS.Core.Common;
using EduOS.Core.DTOs.Communication;
using EduOS.Core.Entities.Communication;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using System.Net;

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
        if (!string.IsNullOrWhiteSpace(request.Endpoint) && !IsPublicHttpsEndpoint(request.Endpoint))
            return ApiResponse<CommunicationGatewayDto>.ErrorResponse("A public HTTPS endpoint is required.");
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
                if (request.Channel == NotificationChannelType.Sms && request.SmsSenderId != null)
                    row.SmsSenderId = EmptyToNull(request.SmsSenderId);
                if (request.Channel == NotificationChannelType.Email && code == "SMTP")
                {
                    if (request.SmtpHost != null) row.SmtpHost = EmptyToNull(request.SmtpHost);
                    if (request.SmtpPort.HasValue) row.SmtpPort = request.SmtpPort;
                    if (request.SmtpUsername != null) row.SmtpUsername = EmptyToNull(request.SmtpUsername);
                    if (request.FromEmail != null) row.FromEmail = EmptyToNull(request.FromEmail);
                    if (request.FromName != null) row.FromName = EmptyToNull(request.FromName);
                    if (request.UseSsl.HasValue) row.UseSsl = request.UseSsl.Value;
                }
                row.Endpoint = EmptyToNull(request.Endpoint);
                row.IsActive = request.IsActive;
                if (request.ClearCredential) row.ProtectedCredential = null;
                else if (!string.IsNullOrWhiteSpace(request.Credential) && request.Credential != "********")
                    row.ProtectedCredential = _protector.Protect(request.Credential);
                var validation = ValidateGateway(row);
                if (validation != null) return ApiResponse<CommunicationGatewayDto>.ErrorResponse(validation, 400);
                var makeDefault = request.IsDefault && row.IsActive;
                if (makeDefault)
                {
                    var others = await query.Where(x => x.Channel == row.Channel && x.IsDefault && x.Id != row.Id).ToListAsync(token);
                    foreach (var other in others)
                    {
                        other.IsDefault = false; other.UpdatedAt = DateTime.UtcNow; other.UpdatedBy = _user.UserId;
                    }
                    if (others.Count > 0) await _unitOfWork.SaveChangesAsync(token);
                }
                row.IsDefault = makeDefault;
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
    private static string? ValidateGateway(CommunicationGateway row)
    {
        if (!row.IsActive) return null;
        if (row.Channel == NotificationChannelType.Sms)
        {
            if (string.IsNullOrWhiteSpace(row.SmsSenderId) || row.SmsSenderId.Length > 20 ||
                !IsPublicHttpsEndpoint(row.Endpoint) || string.IsNullOrWhiteSpace(row.ProtectedCredential))
                return "Enabled SMS gateways require sender ID, public HTTPS API URL and API key.";
        }
        if (row.Channel == NotificationChannelType.Email && row.ProviderCode == "SMTP")
        {
            if (!IsPublicHost(row.SmtpHost) || row.SmtpPort is not (465 or 587 or 2525) ||
                string.IsNullOrWhiteSpace(row.SmtpUsername) ||
                string.IsNullOrWhiteSpace(row.FromEmail) ||
                !new EmailAddressAttribute().IsValid(row.FromEmail) ||
                string.IsNullOrWhiteSpace(row.ProtectedCredential) || !row.UseSsl)
                return "Enabled SMTP requires a public host, TLS port (465/587/2525), username, sender email and password.";
        }
        return null;
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsPublicHttpsEndpoint(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) &&
        IsPublicHost(uri.Host);

    private static bool IsPublicHost(string? value)
    {
        var host = value?.Trim();
        if (string.IsNullOrWhiteSpace(host) || host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)) return false;
        if (IPAddress.TryParse(host, out var address))
        {
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) ||
                address.Equals(IPAddress.IPv6Any) || address.IsIPv6LinkLocal ||
                address.IsIPv6SiteLocal || address.IsIPv6Multicast) return false;
            var bytes = address.GetAddressBytes();
            if (bytes.Length == 4)
                return bytes[0] != 0 && bytes[0] != 10 && bytes[0] != 127 &&
                    bytes[0] < 224 && !(bytes[0] == 169 && bytes[1] == 254) &&
                    !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31) &&
                    !(bytes[0] == 192 && bytes[1] == 168);
            return bytes.Length == 16 && (bytes[0] & 0xfe) != 0xfc;
        }
        return host.Contains('.') && Uri.CheckHostName(host) == UriHostNameType.Dns;
    }

}
