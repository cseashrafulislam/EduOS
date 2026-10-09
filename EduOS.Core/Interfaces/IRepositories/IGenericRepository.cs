using System.Linq.Expressions;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IGenericRepository<T> where T : class
    {
        Task<T?> GetByIdAsync(long id);
        Task<T?> GetByIdAsync(long id, params Expression<Func<T, object>>[] includes);
        Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate);
        Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, params Expression<Func<T, object>>[] includes);
        [Obsolete("Unbounded query. Use a paged, projected SQL query for large data sets.")]
        Task<List<T>> GetAllAsync();
        [Obsolete("Unbounded query. Use a paged, projected SQL query for large data sets.")]
        Task<List<T>> GetAllAsync(params Expression<Func<T, object>>[] includes);
        [Obsolete("Unbounded query. Use a paged, projected SQL query for large data sets.")]
        Task<List<T>> GetAllAsync(Expression<Func<T, object>> orderBy, bool descending = false, params Expression<Func<T, object>>[] includes);
        [Obsolete("Unbounded query. Use a paged, projected SQL query for large data sets.")]
        Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate);
        [Obsolete("Unbounded query. Use a paged, projected SQL query for large data sets.")]
        Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate, params Expression<Func<T, object>>[] includes);
        Task<bool> AnyAsync();
        Task<bool> AnyAsync(Expression<Func<T, bool>> predicate);
        Task<int> CountAsync();
        Task<int> CountAsync(Expression<Func<T, bool>> predicate);
        Task AddAsync(T entity);
        Task AddRangeAsync(IEnumerable<T> entities);
        void Update(T entity);
        void UpdateRange(IEnumerable<T> entities);
        [Obsolete("Hard deletes of tenant business data require explicit workflow and authorization; use controlled archival/reversal.")]
        void Delete(T entity);
        [Obsolete("Bulk hard deletes require a domain-specific authorized retention workflow.")]
        void DeleteRange(IEnumerable<T> entities);
        /// <summary>Composable database query only; enforce tenant, permission and projection before execution.</summary>
        IQueryable<T> GetQueryable();
        /// <summary>Filtered database query, not an authorization boundary. Never materialize an unbounded result.</summary>
        IQueryable<T> GetQueryable(Expression<Func<T, bool>> predicate);
        Task<(List<T> Items, int TotalCount)> GetPagedAsync(int page, int pageSize, Expression<Func<T, bool>>? predicate = null, Expression<Func<T, object>>? orderBy = null, bool descending = false, params Expression<Func<T, object>>[] includes);
        IUnitOfWork UnitOfWork { get; }
    }
}
