using System.Security.Cryptography;
using System.Text.RegularExpressions;
using EduOS.Core.Common;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.AI;

/// <summary>A single source of truth for each institution's own AI provider key.</summary>
public sealed class TenantAiProviderSettingsService(
    EduOSDbContext db, ICurrentUserService current, IDataProtectionProvider protection,
    ILogger<TenantAiProviderSettingsService> logger)
{
    public sealed class SettingRow
    {
        public long TenantId { get; set; }
        public string ProviderCode { get; set; } = "";
        public string Model { get; set; } = "";
        public string? ProtectedApiKey { get; set; }
        public bool IsEnabled { get; set; }
        public bool StudentTutorEnabled { get; set; }
        public byte[] RowVersion { get; set; } = [];
    }

    public sealed record PublicSettings(string ProviderCode, string Model, bool IsEnabled,
        bool StudentTutorEnabled, bool HasApiKey, string? RowVersion);

    public sealed record ResolvedSettings(string Model, string ApiKey, bool StudentTutorEnabled);

    public sealed class SaveRequest
    {
        public string ProviderCode { get; set; } = "OpenAI";
        public string Model { get; set; } = "";
        public string? ApiKey { get; set; }
        public bool IsEnabled { get; set; }
        public bool StudentTutorEnabled { get; set; }
        public bool ClearApiKey { get; set; }
        public string? RowVersion { get; set; }
    }

    private bool Trusted => current.IsAuthenticated && current.TenantId > 0 &&
        current.UserId > 0 && db.CurrentTenantId == current.TenantId;

    private async Task<bool> CanManageAsync(CancellationToken ct) =>
        Trusted && current.IsTenantAdmin &&
        await CentralPrivilegedAccess.HasRoleAsync(db, current, new[] { "TENANTADMIN" }, ct);

    private IDataProtector Protector(long tenantId) =>
        protection.CreateProtector("EduOS.AI.TenantProviderCredentials.v1", tenantId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private Task<SettingRow?> GetRowAsync(CancellationToken ct) =>
        db.Database.SqlQueryRaw<SettingRow>(
            @"SELECT [TenantId], [ProviderCode], [Model], [ProtectedApiKey], [IsEnabled],
                    [StudentTutorEnabled], [RowVersion]
              FROM dbo.TenantAiProviderSettings WHERE [TenantId] = {0}",
            current.TenantId).SingleOrDefaultAsync(ct);

    private static PublicSettings Map(SettingRow? row) => row == null
        ? new PublicSettings("OpenAI", "gpt-4.1-mini", false, false, false, null)
        : new PublicSettings(row.ProviderCode, row.Model, row.IsEnabled,
            row.StudentTutorEnabled, !string.IsNullOrEmpty(row.ProtectedApiKey),
            Convert.ToBase64String(row.RowVersion));

    public async Task<ApiResponse<PublicSettings>> GetAsync(CancellationToken ct)
    {
        if (!await CanManageAsync(ct))
            return ApiResponse<PublicSettings>.ErrorResponse("Tenant administrator required.", 403);
        var row = await GetRowAsync(ct);
        return ApiResponse<PublicSettings>.SuccessResponse(Map(row));
    }

    public async Task<ResolvedSettings?> ResolveAsync(CancellationToken ct)
    {
        if (!Trusted) return null;
        var row = await GetRowAsync(ct);
        if (row?.IsEnabled != true || row.ProviderCode != "OpenAI" ||
            string.IsNullOrWhiteSpace(row.ProtectedApiKey)) return null;
        try
        {
            var key = Protector(current.TenantId).Unprotect(row.ProtectedApiKey);
            return string.IsNullOrWhiteSpace(key) ? null :
                new ResolvedSettings(row.Model, key, row.StudentTutorEnabled);
        }
        catch (CryptographicException)
        {
            logger.LogError("Tenant AI provider credential cannot be decrypted; check the durable Data Protection key ring.");
            return null;
        }
    }

    public async Task<ApiResponse<PublicSettings>> SaveAsync(SaveRequest? request, CancellationToken ct)
    {
        if (!await CanManageAsync(ct))
            return ApiResponse<PublicSettings>.ErrorResponse("Tenant administrator required.", 403);
        if (request == null || request.ProviderCode != "OpenAI" ||
            string.IsNullOrWhiteSpace(request.Model) ||
            !Regex.IsMatch(request.Model, @"^[A-Za-z0-9][A-Za-z0-9._-]{1,99}$") ||
            (request.ApiKey != null && request.ApiKey.Length > 0 &&
             !Regex.IsMatch(request.ApiKey, @"^[\x21-\x7E]{20,2000}$")) ||
            (request.ClearApiKey && !string.IsNullOrEmpty(request.ApiKey)) ||
            (request.ClearApiKey && request.IsEnabled) ||
            (request.StudentTutorEnabled && !request.IsEnabled))
            return ApiResponse<PublicSettings>.ErrorResponse("Invalid provider configuration.", 400);

        var old = await GetRowAsync(ct);
        byte[]? expected = null;
        if (old != null)
        {
            try { expected = Convert.FromBase64String(request.RowVersion ?? ""); }
            catch (FormatException) { return ApiResponse<PublicSettings>.ErrorResponse("Settings changed. Reload before saving.", 409); }
            if (!old.RowVersion.AsSpan().SequenceEqual(expected))
                return ApiResponse<PublicSettings>.ErrorResponse("Settings changed. Reload before saving.", 409);
        }
        else if (!string.IsNullOrWhiteSpace(request.RowVersion))
            return ApiResponse<PublicSettings>.ErrorResponse("Settings changed. Reload before saving.", 409);

        var protectedKey = old?.ProtectedApiKey;
        if (request.ClearApiKey) protectedKey = null;
        else if (!string.IsNullOrEmpty(request.ApiKey))
            protectedKey = Protector(current.TenantId).Protect(request.ApiKey);
        if (request.IsEnabled && string.IsNullOrWhiteSpace(protectedKey))
            return ApiResponse<PublicSettings>.ErrorResponse("Enter your institution's API key before enabling AI.", 400);
        if (protectedKey?.Length > 4000)
            return ApiResponse<PublicSettings>.ErrorResponse("Provider key is too long.", 400);

        try
        {
            int affected;
            if (old == null)
                affected = await db.Database.ExecuteSqlInterpolatedAsync(
                    $@"INSERT dbo.TenantAiProviderSettings
                       ([TenantId], [ProviderCode], [Model], [ProtectedApiKey], [IsEnabled],
                        [StudentTutorEnabled], [UpdatedAtUtc], [UpdatedByUserId])
                       VALUES ({current.TenantId}, {"OpenAI"}, {request.Model}, {protectedKey},
                        {request.IsEnabled}, {request.StudentTutorEnabled}, {DateTime.UtcNow},
                        {current.UserId})", ct);
            else
                affected = await db.Database.ExecuteSqlInterpolatedAsync(
                    $@"UPDATE dbo.TenantAiProviderSettings
                       SET [Model] = {request.Model}, [ProtectedApiKey] = {protectedKey},
                           [IsEnabled] = {request.IsEnabled},
                           [StudentTutorEnabled] = {request.StudentTutorEnabled},
                           [UpdatedAtUtc] = {DateTime.UtcNow},
                           [UpdatedByUserId] = {current.UserId}
                       WHERE [TenantId] = {current.TenantId} AND [RowVersion] = {expected}", ct);
            if (affected != 1)
                return ApiResponse<PublicSettings>.ErrorResponse("Settings changed. Reload before saving.", 409);
            return ApiResponse<PublicSettings>.SuccessResponse(Map(await GetRowAsync(ct)));
        }
        catch (SqlException e) when (e.Number is 2601 or 2627)
        {
            return ApiResponse<PublicSettings>.ErrorResponse("Settings changed. Reload before saving.", 409);
        }
    }
}