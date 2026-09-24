using EduOS.Core.Entities.Employees;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IDesignationRepository : IGenericRepository<Designation>
    {
        Task<List<Designation>> GetActiveAsync(long tenantId);
    }
}
