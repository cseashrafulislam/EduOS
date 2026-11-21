using System.Data;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AI;

/// <summary>
/// Cross-instance SQL Server daily call ceiling. A single short transactional SQL batch
/// owns the counter; no app-server cache, MAX()+1, provider call inside the lock,
/// or in-memory value can authorize a reservation.
/// </summary>
public sealed record AiQuotaReservation(DateOnly UsageDateUtc);

public sealed class AiProviderDailyQuotaService(EduOSDbContext db)
{
    private const string ReserveSql = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Today date = CONVERT(date, SYSUTCDATETIME());
DECLARE @Granted int = 0;
DECLARE @LockResult int;
DECLARE @Resource nvarchar(255) = CONCAT(N'EduOS:AI:', CONVERT(nvarchar(20), @TenantId),
    N':', CONVERT(char(8), @Today, 112));
BEGIN TRANSACTION;
EXEC @LockResult = sys.sp_getapplock
    @Resource = @Resource,
    @LockMode = N'Exclusive',
    @LockOwner = N'Transaction',
    @LockTimeout = 3000;
IF @LockResult < 0
BEGIN
    ROLLBACK TRANSACTION;
    THROW 51092, 'AI daily quota lock unavailable.', 1;
END;
IF EXISTS (SELECT 1 FROM dbo.AiProviderDailyUsage WHERE TenantId = @TenantId AND UsageDateUtc = @Today)
BEGIN
    UPDATE dbo.AiProviderDailyUsage
        SET ReservedCalls = ReservedCalls + 1, LastReservedAtUtc = SYSUTCDATETIME()
    WHERE TenantId = @TenantId AND UsageDateUtc = @Today AND ReservedCalls < @Limit;
    SET @Granted = @@ROWCOUNT;
END
ELSE
BEGIN
    INSERT dbo.AiProviderDailyUsage (TenantId, UsageDateUtc, ReservedCalls, LastReservedAtUtc)
    VALUES (@TenantId, @Today, 1, SYSUTCDATETIME());
    SET @Granted = 1;
END;
COMMIT TRANSACTION;
SELECT @Granted, @Today;";

    public async Task<bool> TryReserveAsync(long tenantId, int dailyLimit, CancellationToken ct) =>
        await ReserveAsync(tenantId, dailyLimit, ct) != null;

    /// <summary>Reserve a provider attempt atomically and return its SQL Server UTC day.</summary>
    public async Task<AiQuotaReservation?> ReserveAsync(long tenantId, int dailyLimit, CancellationToken ct)
    {
        if (tenantId <= 0 || dailyLimit < 1 || dailyLimit > 100 ||
            db.CurrentTenantId != tenantId) return null;

        var connection = db.Database.GetDbConnection();
        var mustClose = connection.State != ConnectionState.Open;
        if (mustClose) await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = ReserveSql;
            command.CommandType = CommandType.Text;
            var tenant = command.CreateParameter();
            tenant.ParameterName = "@TenantId"; tenant.DbType = DbType.Int64; tenant.Value = tenantId;
            command.Parameters.Add(tenant);
            var cap = command.CreateParameter();
            cap.ParameterName = "@Limit"; cap.DbType = DbType.Int32; cap.Value = dailyLimit;
            command.Parameters.Add(cap);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct) || reader.GetInt32(0) != 1) return null;
            return new AiQuotaReservation(DateOnly.FromDateTime(reader.GetDateTime(1)));
        }
        finally
        {
            if (mustClose) await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// A single atomic increment per observed successful response. No retries are
    /// attempted if the SQL outcome is unknown. Provider billing remains authoritative.
    /// </summary>
    public async Task RecordObservedUsageAsync(long tenantId, AiQuotaReservation reservation,
        long inputTokens, long outputTokens, CancellationToken ct)
    {
        if (tenantId <= 0 || db.CurrentTenantId != tenantId || reservation == null ||
            inputTokens < 0 || outputTokens < 0 ||
            inputTokens > 100000000L || outputTokens > 100000000L)
            throw new InvalidOperationException("Invalid AI observation.");
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE dbo.AiProviderDailyUsage SET
                ObservedInputTokens = ObservedInputTokens + {inputTokens},
                ObservedOutputTokens = ObservedOutputTokens + {outputTokens},
                ObservedResponses = ObservedResponses + 1
            WHERE TenantId = {tenantId} AND UsageDateUtc = {reservation.UsageDateUtc}", ct);
        if (affected != 1)
            throw new InvalidOperationException("AI quota reservation not found for usage observation.");
    }
}
