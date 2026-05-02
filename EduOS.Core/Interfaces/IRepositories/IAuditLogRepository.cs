using EduOS.Core.Entities.System;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IAuditLogRepository : IGenericRepository<AuditLog>
    {
        Task<List<AuditLog>> GetByUserIdAsync(long userId, long tenantId);
        Task<List<AuditLog>> GetByTableNameAsync(string tableName, long tenantId);
        Task<List<AuditLog>> GetByRecordIdAsync(string tableName, int recordId, long tenantId);
    }
}
