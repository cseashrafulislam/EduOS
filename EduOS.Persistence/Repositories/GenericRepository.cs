using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace EduOS.Persistence.Repositories
{
    public class GenericRepository<T> : IGenericRepository<T> where T : class
    {
        protected readonly EduOSDbContext _context;
        protected readonly DbSet<T> _dbSet;

        public IUnitOfWork UnitOfWork => _context;

        public GenericRepository(EduOSDbContext context)
        {
            _context = context;
            _dbSet = context.Set<T>();
        }

        public virtual async Task<T?> GetByIdAsync(long id)
        {
            // FindAsync can return an already-tracked entity without applying global tenant/soft-delete filters.
            return await _dbSet.FirstOrDefaultAsync(x => EF.Property<long>(x, "Id") == id);
        }

        public virtual async Task<T?> GetByIdAsync(
            long id,
            params Expression<Func<T, object>>[] includes)
        {
            IQueryable<T> query = _dbSet;

            foreach (var include in includes)
            {
                query = query.Include(include);
            }

            return await query.FirstOrDefaultAsync(e =>
                EF.Property<long>(e, "Id") == id);
        }

        public virtual async Task<T?> FirstOrDefaultAsync(
            Expression<Func<T, bool>> predicate)
        {
            return await _dbSet.FirstOrDefaultAsync(predicate);
        }

        public virtual async Task<T?> FirstOrDefaultAsync(
            Expression<Func<T, bool>> predicate,
            params Expression<Func<T, object>>[] includes)
        {
            IQueryable<T> query = _dbSet;

            foreach (var include in includes)
            {
                query = query.Include(include);
            }

            return await query.FirstOrDefaultAsync(predicate);
        }

        public virtual async Task<List<T>> GetAllAsync()
        {
            return await _dbSet.ToListAsync();
        }

        public virtual async Task<List<T>> GetAllAsync(
            params Expression<Func<T, object>>[] includes)
        {
            IQueryable<T> query = _dbSet;

            foreach (var include in includes)
            {
                query = query.Include(include);
            }

            return await query.ToListAsync();
        }

        public virtual async Task<List<T>> GetAllAsync(
            Expression<Func<T, object>> orderBy,
            bool descending = false,
            params Expression<Func<T, object>>[] includes)
        {
            IQueryable<T> query = _dbSet;

            foreach (var include in includes)
            {
                query = query.Include(include);
            }

            query = descending
                ? query.OrderByDescending(orderBy)
                : query.OrderBy(orderBy);

            return await query.ToListAsync();
        }

        public virtual async Task<List<T>> FindAsync(
            Expression<Func<T, bool>> predicate)
        {
            return await _dbSet.Where(predicate).ToListAsync();
        }

        public virtual async Task<List<T>> FindAsync(
            Expression<Func<T, bool>> predicate,
            params Expression<Func<T, object>>[] includes)
        {
            IQueryable<T> query = _dbSet;

            foreach (var include in includes)
            {
                query = query.Include(include);
            }

            return await query.Where(predicate).ToListAsync();
        }

        public virtual async Task<T?> FindFirstOrDefaultAsync(
            Expression<Func<T, bool>> predicate)
        {
            return await _dbSet.FirstOrDefaultAsync(predicate);
        }

        public virtual async Task<T?> FindFirstOrDefaultAsync(
            Expression<Func<T, bool>> predicate,
            params Expression<Func<T, object>>[] includes)
        {
            IQueryable<T> query = _dbSet;

            foreach (var include in includes)
            {
                query = query.Include(include);
            }

            return await query.FirstOrDefaultAsync(predicate);
        }

        public virtual async Task<bool> AnyAsync()
        {
            return await _dbSet.AnyAsync();
        }

        public virtual async Task<bool> AnyAsync(
            Expression<Func<T, bool>> predicate)
        {
            return await _dbSet.AnyAsync(predicate);
        }

        public virtual async Task<bool> ExistsAsync(
            Expression<Func<T, bool>> predicate)
        {
            return await _dbSet.AnyAsync(predicate);
        }

        public virtual async Task<int> CountAsync()
        {
            return await _dbSet.CountAsync();
        }

        public virtual async Task<int> CountAsync(
            Expression<Func<T, bool>> predicate)
        {
            return await _dbSet.CountAsync(predicate);
        }

        public virtual async Task AddAsync(T entity)
        {
            await _dbSet.AddAsync(entity);
        }

        public virtual async Task AddRangeAsync(IEnumerable<T> entities)
        {
            await _dbSet.AddRangeAsync(entities);
        }

        public virtual void Update(T entity)
        {
            _dbSet.Update(entity);
        }

        public virtual void UpdateRange(IEnumerable<T> entities)
        {
            _dbSet.UpdateRange(entities);
        }

        public virtual void Delete(T entity)
        {
            _dbSet.Remove(entity);
        }

        public virtual void DeleteRange(IEnumerable<T> entities)
        {
            _dbSet.RemoveRange(entities);
        }

        public virtual async Task<bool> DeleteByIdAsync(long id)
        {
            var entity = await GetByIdAsync(id);

            if (entity == null)
            {
                return false;
            }

            Delete(entity);
            return true;
        }

        public virtual IQueryable<T> GetQueryable()
        {
            return _dbSet.AsQueryable();
        }

        public virtual IQueryable<T> GetQueryable(
            Expression<Func<T, bool>> predicate)
        {
            return _dbSet.Where(predicate);
        }

        public virtual async Task<(List<T> Items, int TotalCount)> GetPagedAsync(
            int page,
            int pageSize,
            Expression<Func<T, bool>>? predicate = null,
            Expression<Func<T, object>>? orderBy = null,
            bool descending = false,
            params Expression<Func<T, object>>[] includes)
        {
            if (page <= 0)
            {
                page = 1;
            }

            pageSize = Math.Clamp(pageSize, 1, 200);
            var offset = ((long)Math.Max(1, page) - 1) * pageSize;

            IQueryable<T> query = _dbSet;

            foreach (var include in includes)
            {
                query = query.Include(include);
            }

            if (predicate != null)
            {
                query = query.Where(predicate);
            }

            var totalCount = await query.CountAsync();
            if (offset > int.MaxValue) return (new List<T>(), totalCount);

            query = orderBy == null
                ? query.OrderBy(x => EF.Property<long>(x, "Id"))
                : descending
                    ? query.OrderByDescending(orderBy).ThenBy(x => EF.Property<long>(x, "Id"))
                    : query.OrderBy(orderBy).ThenBy(x => EF.Property<long>(x, "Id"));

            var items = await query
                .Skip((int)offset)
                .Take(pageSize)
                .ToListAsync();

            return (items, totalCount);
        }
    }
}