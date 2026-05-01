using EduOS.Core.Entities.Exams;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IGradeRuleRepository : IGenericRepository<GradeRule>
    {
        Task<List<GradeRule>> GetByTenantAsync(int tenantId);
        Task<GradeRule?> GetByMarkAsync(decimal mark, int tenantId);
    }
}
