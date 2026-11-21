using System.Data;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AI;

/// <summary>
/// Cross-instance SQL Server daily call ceiling. A single short transactional SQL batch
/// owns the counter; no app-server cache, MAX()+1, provider call inside the lock,
/// or in-memory value can authorize a reservation.
/// </summary>
public sealed class AiProviderDailyQuotaService(EduOSDbContext db)
{
    private const string ReserveSql = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Today date = CONVERT(date, SYSUTCDATETIME());
DECLARE @Granted int = 0;
DECLARE @LockResult int;
BEGIN TRANSACTION;
EXEC @LockResult = sys.sp_getapplock
    @Resource = CONCAT(N'EduOS:AI:', CONVERT(nvarchar(20), @TenantId), N':', CONVERT(char(8), @Today, 112)),
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
SELECT @Granted;";

    public async Task<bool> TryReserveAsync(long tenantId, int dailyLimit, CancellationToken ct)
    {
        if (tenantId <= 0 || dailyLimit < 1 || dailyLimit > 100 ||
            db.CurrentTenantId != tenantId) return false;

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
            var result = await command.ExecuteScalarAsync(ct);
            return result is int granted && granted == 1;
        }
        finally
        {
            if (mustClose) await db.Database.CloseConnectionAsync();
        }
    }
}
