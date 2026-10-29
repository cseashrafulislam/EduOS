using System.Data;
using System.Globalization;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EduOS.Service.Services.SaaS;

/// <summary>
/// Active student/employee limits from the effective tenant subscription.
/// A zero plan limit is unlimited. A missing effective plan does not invent a
/// quota; the existing subscription access gate owns that authorization.
/// Every limited write must call this inside its database transaction. The
/// SQL transaction-scoped application lock serializes different write paths
/// (admission/import/readmission/reactivation) without locking unrelated tenants.
/// </summary>
public static class SubscriptionActiveCapacityPolicy
{
    public static Task<bool> CanAddStudentsAsync(EduOSDbContext db, long tenantId,
        int additional, CancellationToken ct = default) => CheckAsync(db, tenantId,
            additional, "Student", ct);

    public static Task<bool> CanAddEmployeesAsync(EduOSDbContext db, long tenantId,
        int additional, CancellationToken ct = default) => CheckAsync(db, tenantId,
            additional, "Employee", ct);

    private static async Task<bool> CheckAsync(EduOSDbContext db, long tenantId,
        int additional, string resource, CancellationToken ct)
    {
        if (tenantId <= 0 || db.CurrentTenantId != tenantId || additional <= 0)
            return false;

        var now = DateTime.UtcNow;
        var planId = await db.TenantSubscriptions.AsNoTracking()
            .Where(x => x.TenantId == tenantId && !x.IsDeleted &&
                x.StartsAt <= now && x.EndsAt > now &&
                (x.State == SubscriptionState.Trial || x.State == SubscriptionState.Active ||
                 x.State == SubscriptionState.Grace))
            .OrderByDescending(x => x.StartsAt).ThenByDescending(x => x.Id)
            .Select(x => (long?)x.SubscriptionPlanId).FirstOrDefaultAsync(ct);
        if (!planId.HasValue) return true;

        var max = await db.SubscriptionPlans.AsNoTracking()
            .Where(x => x.Id == planId.Value && !x.IsDeleted)
            .Select(x => resource == "Student" ? (int?)x.MaxStudents : x.MaxEmployees)
            .FirstOrDefaultAsync(ct);
        if (max is null or <= 0) return true;

        if (!await AcquireTransactionLockAsync(db,
                "EduOS.Quota.Active" + resource + "." + tenantId.ToString(CultureInfo.InvariantCulture), ct))
            return false;

        var used = resource == "Student"
            ? await db.Students.AsNoTracking().CountAsync(x =>
                x.TenantId == tenantId && !x.IsDeleted && x.StatusCode == "Active", ct)
            : await db.Employees.AsNoTracking().CountAsync(x =>
                x.TenantId == tenantId && !x.IsDeleted && x.State == EmployeeState.Active, ct);
        return used <= max.Value && additional <= max.Value - used;
    }

    private static async Task<bool> AcquireTransactionLockAsync(EduOSDbContext db,
        string resource, CancellationToken ct)
    {
        // EF InMemory is only used for deterministic policy tests. The real SQL
        // path must have a transaction; a preflight without the lock is unsafe.
        if (!db.Database.IsRelational()) return true;
        if (!db.Database.IsSqlServer())
            throw new InvalidOperationException("Tenant quota reservation requires SQL Server.");
        var transaction = db.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("Tenant quota reservation requires a database transaction.");
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandType = CommandType.Text;
        command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock " +
            "@Resource = @resource, @LockMode = 'Exclusive', " +
            "@LockOwner = 'Transaction', @LockTimeout = 10000; SELECT @result;";
        var argument = command.CreateParameter();
        argument.ParameterName = "@resource";
        argument.Value = resource;
        command.Parameters.Add(argument);
        var outcome = await command.ExecuteScalarAsync(ct);
        return outcome != null && Convert.ToInt32(outcome, CultureInfo.InvariantCulture) >= 0;
    }
}
