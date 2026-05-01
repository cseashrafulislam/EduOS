using EduOS.Core.Entities.System;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IAuditLogRepository : IGenericRepository<AuditLog>
    {
        Task<List<AuditLog>> GetByUserIdAsync(int userId, int tenantId);
        Task<List<AuditLog>> GetByTableNameAsync(string tableName, int tenantId);
        Task<List<AuditLog>> GetByRecordIdAsync(string tableName, int recordId, int tenantId);
    }
}
