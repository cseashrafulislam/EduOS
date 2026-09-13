using EduOS.Core.Entities.Employees;
namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IEmployeeRepository : IGenericRepository<Employee>
    {
        Task<Employee?> GetByCodeAsync(string code);
        Task<Employee?> GetByUserIdAsync(long userId);
        Task<List<Employee>> GetTeachersAsync(long tenantId);
        Task<List<Employee>> GetByDepartmentAsync(int departmentId);
        Task<bool> IsCodeExistsAsync(string code, long tenantId, long? excludeId = null);
        Task<string> GenerateEmployeeCodeAsync(long tenantId);
    }
}
