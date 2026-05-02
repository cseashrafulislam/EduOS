using EduOS.Core.Entities.System;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories.System
{
    public class AuditLogRepository : GenericRepository<AuditLog>, IAuditLogRepository
    {
        public AuditLogRepository(EduOSDbContext context) : base(context) { }

        public async Task<List<AuditLog>> GetByUserIdAsync(long userId, long tenantId)
        {
            return await _dbSet
                .Where(a => a.UserId == userId && a.TenantId == tenantId)
                .OrderByDescending(a => a.CreatedAt)
                .Take(100)
                .ToListAsync();
        }

        public async Task<List<AuditLog>> GetByTableNameAsync(string tableName, long tenantId)
        {
            return await _dbSet
                .Where(a => a.TableName == tableName && a.TenantId == tenantId)
                .OrderByDescending(a => a.CreatedAt)
                .Take(100)
                .ToListAsync();
        }

        public async Task<List<AuditLog>> GetByRecordIdAsync(string tableName, int recordId, long tenantId)
        {
            return await _dbSet
                .Where(a => a.TableName == tableName
                         && a.RecordId == recordId
                         && a.TenantId == tenantId)
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();
        }
    }
}
