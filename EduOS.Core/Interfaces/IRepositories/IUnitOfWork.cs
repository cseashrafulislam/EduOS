using Microsoft.EntityFrameworkCore.Storage;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IUnitOfWork : IDisposable
    {
        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
        Task BeginTransactionAsync();
        Task CommitTransactionAsync();
        Task RollbackTransactionAsync();
        IExecutionStrategy CreateExecutionStrategy();
    }
}
