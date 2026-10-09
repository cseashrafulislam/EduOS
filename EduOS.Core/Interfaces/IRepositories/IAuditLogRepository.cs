using EduOS.Core.Entities.System;

namespace EduOS.Core.Interfaces.IRepositories;

/// <summary>AuditLog.EntityName/EntityId are canonical. Tenant null is host-scoped and must require privileged authorization.</summary>
public interface IAuditLogRepository : IGenericRepository<AuditLog>
{
    Task<(List<AuditLog> Items, int TotalCount)> GetByUserIdAsync(long userId, long tenantId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<(List<AuditLog> Items, int TotalCount)> GetByEntityAsync(string entityName, long tenantId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<(List<AuditLog> Items, int TotalCount)> GetByEntityRecordAsync(string entityName, long entityId, long tenantId, int page, int pageSize, CancellationToken cancellationToken = default);
}
