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

    public Task<(List<AuditLog> Items, int TotalCount)> GetByUserIdAsync(long userId, long tenantId, int page, int pageSize, CancellationToken cancellationToken) =>
        PageAsync(_dbSet.AsNoTracking().Where(x => x.UserId == userId && x.TenantId == tenantId)
            .OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id), page, pageSize, cancellationToken);
    public Task<(List<AuditLog> Items, int TotalCount)> GetByEntityAsync(string entityName, long tenantId, int page, int pageSize, CancellationToken cancellationToken) =>
        PageAsync(_dbSet.AsNoTracking().Where(x => x.EntityName == entityName && x.TenantId == tenantId)
            .OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id), page, pageSize, cancellationToken);
    public Task<(List<AuditLog> Items, int TotalCount)> GetByEntityRecordAsync(string entityName, long entityId, long tenantId, int page, int pageSize, CancellationToken cancellationToken) =>
        PageAsync(_dbSet.AsNoTracking().Where(x => x.EntityName == entityName && x.EntityId == entityId && x.TenantId == tenantId)
            .OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id), page, pageSize, cancellationToken);
}
