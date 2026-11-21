using System.Security.Cryptography;
using System.Text.RegularExpressions;
using EduOS.Core.Common;
using EduOS.AI;
using Microsoft.Extensions.Configuration;
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
    ILogger<TenantAiProviderSettingsService> logger, IConfiguration configuration)
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

    public sealed class UsageDayRow
    {
        public DateOnly UsageDateUtc { get; set; }
        public int ReservedCalls { get; set; }
        public long ObservedInputTokens { get; set; }
        public long ObservedOutputTokens { get; set; }
        public long ObservedResponses { get; set; }
    }

    public sealed record UsageSummary(int PeriodDays, int TotalReservedCalls, int DailyLimit,
        long ObservedInputTokens, long ObservedOutputTokens, long ObservedResponses,
        IReadOnlyList<UsageDayRow> Days, string Notice);

    public sealed class AgentRow
    {
        public string AgentCode { get; set; } = "";
        public bool IsEnabled { get; set; }
        public byte[] RowVersion { get; set; } = [];
    }

    public sealed record AgentPolicy(string Code, string Title, string Description,
        bool IsEnabled, string? RowVersion);

    public sealed class SaveAgentRequest
    {
        public bool IsEnabled { get; set; }
        public string? RowVersion { get; set; }
    }

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

    /// <summary>
    /// Reporting is based on durable SQL reservations, not guessed pricing or billed tokens.
    /// No prompts, student data, request contents, key or cost estimates are returned.
    /// </summary>
    private Task<List<AgentRow>> GetAgentRowsAsync(CancellationToken ct) =>
        db.Database.SqlQueryRaw<AgentRow>(
            @"SELECT [AgentCode], [IsEnabled], [RowVersion] FROM dbo.TenantAiAgentControls
              WHERE [TenantId] = {0}", current.TenantId).ToListAsync(ct);

    public async Task<bool> AgentEnabledAsync(string agentCode, CancellationToken ct)
    {
        if (!Trusted || !TenantAiAgentRegistry.Supports(agentCode)) return false;
        var row = await db.Database.SqlQueryRaw<AgentRow>(
            @"SELECT [AgentCode], [IsEnabled], [RowVersion] FROM dbo.TenantAiAgentControls
              WHERE [TenantId] = {0} AND [AgentCode] = {1}",
            current.TenantId, agentCode).SingleOrDefaultAsync(ct);
        // No override means the previously supported feature remains enabled.
        // Provider enable, per-request consent, role and module access remain mandatory.
        return row?.IsEnabled ?? true;
    }

    public async Task<ApiResponse<IReadOnlyList<AgentPolicy>>> AgentsAsync(CancellationToken ct)
    {
        if (!await CanManageAsync(ct))
            return ApiResponse<IReadOnlyList<AgentPolicy>>.ErrorResponse("Tenant administrator required.", 403);
        var rows = await GetAgentRowsAsync(ct);
        var byCode = rows.ToDictionary(x => x.AgentCode, StringComparer.Ordinal);
        var list = TenantAiAgentRegistry.Controllable.Select(d =>
        {
            byCode.TryGetValue(d.Code, out var row);
            return new AgentPolicy(d.Code, d.Title, d.Description,
                row?.IsEnabled ?? true,
                row == null ? null : Convert.ToBase64String(row.RowVersion));
        }).ToArray();
        return ApiResponse<IReadOnlyList<AgentPolicy>>.SuccessResponse(list);
    }

    public async Task<ApiResponse<AgentPolicy>> SaveAgentAsync(
        string? code, SaveAgentRequest? request, CancellationToken ct)
    {
        if (!await CanManageAsync(ct))
            return ApiResponse<AgentPolicy>.ErrorResponse("Tenant administrator required.", 403);
        if (!TenantAiAgentRegistry.Supports(code) || request == null)
            return ApiResponse<AgentPolicy>.ErrorResponse("Unknown AI capability.", 400);
        var descriptor = TenantAiAgentRegistry.Controllable.Single(x => x.Code == code);
        var old = await db.Database.SqlQueryRaw<AgentRow>(
            @"SELECT [AgentCode], [IsEnabled], [RowVersion] FROM dbo.TenantAiAgentControls
              WHERE [TenantId] = {0} AND [AgentCode] = {1}",
            current.TenantId, code).SingleOrDefaultAsync(ct);
        byte[]? expected = null;
        if (old != null)
        {
            try { expected = Convert.FromBase64String(request.RowVersion ?? ""); }
            catch (FormatException)
            {
                return ApiResponse<AgentPolicy>.ErrorResponse("Agent settings changed. Reload.", 409);
            }
            if (!old.RowVersion.AsSpan().SequenceEqual(expected))
                return ApiResponse<AgentPolicy>.ErrorResponse("Agent settings changed. Reload.", 409);
        }
        else if (!string.IsNullOrWhiteSpace(request.RowVersion))
            return ApiResponse<AgentPolicy>.ErrorResponse("Agent settings changed. Reload.", 409);
        try
        {
            int updated;
            if (old == null)
                updated = await db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT dbo.TenantAiAgentControls
                        (TenantId, AgentCode, IsEnabled, UpdatedAtUtc, UpdatedByUserId)
                    VALUES ({current.TenantId}, {code}, {request.IsEnabled}, {DateTime.UtcNow},
                        {current.UserId})", ct);
            else
                updated = await db.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE dbo.TenantAiAgentControls SET IsEnabled = {request.IsEnabled},
                        UpdatedAtUtc = {DateTime.UtcNow}, UpdatedByUserId = {current.UserId}
                    WHERE TenantId = {current.TenantId} AND AgentCode = {code}
                        AND RowVersion = {expected}", ct);
            if (updated != 1)
                return ApiResponse<AgentPolicy>.ErrorResponse("Agent settings changed. Reload.", 409);
            var changed = await db.Database.SqlQueryRaw<AgentRow>(
                @"SELECT [AgentCode], [IsEnabled], [RowVersion] FROM dbo.TenantAiAgentControls
                  WHERE [TenantId] = {0} AND [AgentCode] = {1}",
                current.TenantId, code).SingleAsync(ct);
            return ApiResponse<AgentPolicy>.SuccessResponse(new AgentPolicy(
                descriptor.Code, descriptor.Title, descriptor.Description, changed.IsEnabled,
                Convert.ToBase64String(changed.RowVersion)));
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return ApiResponse<AgentPolicy>.ErrorResponse("Agent settings changed. Reload.", 409);
        }
    }

    public async Task<ApiResponse<UsageSummary>> UsageAsync(int days, CancellationToken ct)
    {
        if (!await CanManageAsync(ct))
            return ApiResponse<UsageSummary>.ErrorResponse("Tenant administrator required.", 403);
        if (days is < 1 or > 90)
            return ApiResponse<UsageSummary>.ErrorResponse("Choose a range of 1–90 days.", 400);

        var rows = await db.Database.SqlQueryRaw<UsageDayRow>(
            @"SELECT [UsageDateUtc], [ReservedCalls], [ObservedInputTokens], [ObservedOutputTokens],
                     [ObservedResponses] FROM dbo.AiProviderDailyUsage
              WHERE [TenantId] = {0} AND [UsageDateUtc] >=
                DATEADD(day, 1 - {1}, CONVERT(date, SYSUTCDATETIME()))",
            current.TenantId, days).ToListAsync(ct);
        var ordered = rows.OrderByDescending(x => x.UsageDateUtc).ToArray();
        var cap = configuration.GetValue<int?>("EduOsAi:DailyCallsPerTenant") ?? 20;
        return ApiResponse<UsageSummary>.SuccessResponse(new UsageSummary(
            days, ordered.Sum(x => x.ReservedCalls), cap,
            ordered.Sum(x => x.ObservedInputTokens), ordered.Sum(x => x.ObservedOutputTokens),
            ordered.Sum(x => x.ObservedResponses), ordered,
            "Reservations count all attempts; observed tokens are incomplete telemetry, not provider-billed cost."));
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