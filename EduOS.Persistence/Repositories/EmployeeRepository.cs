using EduOS.Core.Entities.Employees;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
namespace EduOS.Persistence.Repositories
{
 public class EmployeeRepository:GenericRepository<Employee>,IEmployeeRepository
 {
  public EmployeeRepository(EduOSDbContext context):base(context){}
  public async Task<Employee?> GetByCodeAsync(string code)=>await _dbSet.Include(e=>e.Designation).Include(e=>e.Department).FirstOrDefaultAsync(e=>e.EmployeeCode==code);
  public async Task<Employee?> GetByUserIdAsync(long userId)=>await _dbSet.FirstOrDefaultAsync(e=>e.UserId==userId);
  public async Task<List<Employee>> GetTeachersAsync(long tenantId)=>await _dbSet.Include(e=>e.Designation).Where(e=>e.TenantId==tenantId&&e.IsTeacher&&e.IsActive).OrderBy(e=>e.FullName).ToListAsync();
  public async Task<List<Employee>> GetByDepartmentAsync(int departmentId)=>await _dbSet.Where(e=>e.DepartmentId==departmentId&&e.IsActive).ToListAsync();
  public async Task<bool> IsCodeExistsAsync(string code,long tenantId,long? excludeId=null){var q=_dbSet.Where(e=>e.EmployeeCode==code&&e.TenantId==tenantId);if(excludeId.HasValue)q=q.Where(e=>e.Id!=excludeId.Value);return await q.AnyAsync();}
  public async Task<string> GenerateEmployeeCodeAsync(long tenantId){var last=await _dbSet.Where(e=>e.TenantId==tenantId).OrderByDescending(e=>e.Id).FirstOrDefaultAsync();var n=1;if(last!=null){var digits=new string(last.EmployeeCode.Where(char.IsDigit).ToArray());if(int.TryParse(digits,out var x))n=x+1;}return $"EMP{n:D5}";}
 }
}
