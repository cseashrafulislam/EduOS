using EduOS.Core.Entities.Employees;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IEmployeeRepository : IGenericRepository<Employee>
    {
        Task<Employee?> GetByCodeAsync(string code);
        Task<Employee?> GetByUserIdAsync(int userId);
        Task<List<Employee>> GetTeachersAsync(int tenantId);
        Task<List<Employee>> GetByDepartmentAsync(int departmentId);
        Task<bool> IsCodeExistsAsync(string code, int tenantId, int? excludeId = null);
        Task<string> GenerateEmployeeCodeAsync(int tenantId);
    }
}
