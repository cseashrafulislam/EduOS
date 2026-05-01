using System.Linq.Expressions;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IGenericRepository<T> where T : class
    {
        // Get
        Task<T?> GetByIdAsync(int id);
        Task<T?> GetByIdAsync(int id, params Expression<Func<T, object>>[] includes);
        Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate);
        Task<List<T>> GetAllAsync();
        Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate);
        Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate, params Expression<Func<T, object>>[] includes);

        // Existence & Count
        Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate);
        Task<int> CountAsync();
        Task<int> CountAsync(Expression<Func<T, bool>> predicate);

        // Add
        Task AddAsync(T entity);
        Task AddRangeAsync(IEnumerable<T> entities);

        // Update
        void Update(T entity);
        void UpdateRange(IEnumerable<T> entities);

        // Delete
        void Delete(T entity);
        void DeleteRange(IEnumerable<T> entities);
        Task<bool> DeleteByIdAsync(int id);

        // Queryable for advanced queries
        IQueryable<T> GetQueryable();
        IQueryable<T> GetQueryable(Expression<Func<T, bool>> predicate);

        // Pagination
        Task<(List<T> Items, int TotalCount)> GetPagedAsync(
            int page, 
            int pageSize, 
            Expression<Func<T, bool>>? predicate = null,
            Expression<Func<T, object>>? orderBy = null,
            bool descending = false,
            params Expression<Func<T, object>>[] includes);
    }
}
