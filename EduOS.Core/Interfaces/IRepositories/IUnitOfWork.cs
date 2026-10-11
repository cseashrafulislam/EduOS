using System.Data;

namespace EduOS.Core.Interfaces.IRepositories;

/// <summary>
/// Persistence-neutral unit of work. Transactions and provider retries are coordinated by
/// Persistence, not exposed as EF Core execution strategies to the application contracts.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute a short, atomic business operation. The implementation owns transaction
    /// creation, commit/rollback and any provider-specific retry execution strategy.
    /// The delegate must not perform external side effects and must tolerate replay
    /// using tenant-scoped idempotency keys and database uniqueness constraints.
    /// An exception must roll back the entire transaction.
    /// </summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Use a stricter isolation level only when predicate-based integrity checks
    /// must serialize concurrent writers (e.g. overlapping academic timetables).
    /// Provider retry, commit and rollback still belong to Persistence.
    /// </summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken = default);
}
