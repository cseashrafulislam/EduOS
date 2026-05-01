using EduOS.Core.Entities.Employees;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class EmployeeRepository : GenericRepository<Employee>, IEmployeeRepository
    {
        public EmployeeRepository(EduOSDbContext context) : base(context) { }

        public async Task<Employee?> GetByCodeAsync(string code)
        {
            return await _dbSet
                .Include(e => e.Designation)
                .Include(e => e.Department)
                .FirstOrDefaultAsync(e => e.EmployeeCode == code);
        }

        public async Task<Employee?> GetByUserIdAsync(int userId)
        {
            return await _dbSet
                .FirstOrDefaultAsync(e => e.UserId == userId);
        }

        public async Task<List<Employee>> GetTeachersAsync(int tenantId)
        {
            return await _dbSet
                .Include(e => e.Designation)
                .Where(e => e.TenantId == tenantId && e.IsTeacher && e.IsActive)
                .OrderBy(e => e.FullName)
                .ToListAsync();
        }

        public async Task<List<Employee>> GetByDepartmentAsync(int departmentId)
        {
            return await _dbSet
                .Where(e => e.DepartmentId == departmentId && e.IsActive)
                .ToListAsync();
        }

        public async Task<bool> IsCodeExistsAsync(string code, int tenantId, int? excludeId = null)
        {
            var query = _dbSet.Where(e => 
                e.EmployeeCode == code && e.TenantId == tenantId);
            if (excludeId.HasValue)
                query = query.Where(e => e.Id != excludeId.Value);
            return await query.AnyAsync();
        }

        public async Task<string> GenerateEmployeeCodeAsync(int tenantId)
        {
            var lastEmployee = await _dbSet
                .Where(e => e.TenantId == tenantId)
                .OrderByDescending(e => e.Id)
                .FirstOrDefaultAsync();

            int nextNumber = 1;
            if (lastEmployee != null && !string.IsNullOrEmpty(lastEmployee.EmployeeCode))
            {
                var numericPart = new string(lastEmployee.EmployeeCode.Where(char.IsDigit).ToArray());
                if (int.TryParse(numericPart, out int lastNumber))
                    nextNumber = lastNumber + 1;
            }

            return $"EMP{nextNumber:D5}";
        }
    }
}
