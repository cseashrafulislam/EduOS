using EduOS.Core.Entities.System;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories.System;

public class AuditLogRepository : GenericRepository<AuditLog>, IAuditLogRepository
{
    public AuditLogRepository(EduOSDbContext context) : base(context) { }

    public Task<List<AuditLog>> GetByUserIdAsync(long userId, long tenantId) =>
        _dbSet.AsNoTracking().Where(x => x.UserId == userId && x.TenantId == tenantId)
            .OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id).Take(100).ToListAsync();

    public Task<List<AuditLog>> GetByTableNameAsync(string tableName, long tenantId) =>
        _dbSet.AsNoTracking().Where(x => x.EntityName == tableName && x.TenantId == tenantId)
            .OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id).Take(100).ToListAsync();

    public Task<List<AuditLog>> GetByRecordIdAsync(string tableName, long recordId, long tenantId) =>
        _dbSet.AsNoTracking().Where(x => x.EntityName == tableName && x.EntityId == recordId && x.TenantId == tenantId)
            .OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id).Take(500).ToListAsync();
}
