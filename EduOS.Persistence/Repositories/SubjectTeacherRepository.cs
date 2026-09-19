using EduOS.Core.Entities.Academic;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class InstructorAssignmentRepository : GenericRepository<InstructorAssignment>, IInstructorAssignmentRepository
    {
        public InstructorAssignmentRepository(EduOSDbContext context) : base(context) { }

        public async Task<List<InstructorAssignment>> GetByEmployeeAsync(long employeeId, long academicYearId)
        {
            return await _dbSet
                .Include(st => st.AcademicBatch)
                .Include(st => st.Subject)
                .Include(st => st.Employee)
                .Include(st => st.AcademicTerm)
                .Where(st => st.EmployeeId == employeeId && st.AcademicYearId == academicYearId)
                .ToListAsync();
        }

        public async Task<List<InstructorAssignment>> GetByBatchAsync(long academicBatchId)
        {
            return await _dbSet
                .Include(st => st.AcademicBatch)
                .Include(st => st.Subject)
                .Include(st => st.Employee)
                .Include(st => st.AcademicTerm)
                .Where(st => st.AcademicBatchId == academicBatchId)
                .ToListAsync();
        }

        public async Task<InstructorAssignment?> GetAdvisorAsync(long academicBatchId, long academicYearId)
        {
            return await _dbSet
                .Include(st => st.AcademicBatch)
                .Include(st => st.Employee)
                .FirstOrDefaultAsync(st => st.AcademicBatchId == academicBatchId && st.AcademicYearId == academicYearId && st.IsClassAdvisor && st.IsActive);
        }
    }
}
