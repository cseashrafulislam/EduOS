using EduOS.Core.Entities.Academic;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

public class InstructorAssignmentRepository : GenericRepository<InstructorAssignment>, IInstructorAssignmentRepository
{
    public InstructorAssignmentRepository(EduOSDbContext context) : base(context) { }

    public async Task<List<InstructorAssignment>> GetByEmployeeAsync(long employeeId, long academicYearId)
    {
        var query =
            from assignment in _dbSet.AsNoTracking()
            join offering in _context.Set<SubjectOffering>() on assignment.SubjectOfferingId equals offering.Id
            where assignment.EmployeeId == employeeId && offering.AcademicYearId == academicYearId && assignment.IsActive
            orderby assignment.EffectiveFrom descending
            select assignment;
        return await query.ToListAsync();
    }

    public async Task<List<InstructorAssignment>> GetByBatchAsync(long academicBatchId)
    {
        var query =
            from assignment in _dbSet.AsNoTracking()
            join offering in _context.Set<SubjectOffering>() on assignment.SubjectOfferingId equals offering.Id
            where offering.AcademicBatchId == academicBatchId && assignment.IsActive
            orderby assignment.IsPrimary descending, assignment.EffectiveFrom descending
            select assignment;
        return await query.ToListAsync();
    }

    public async Task<InstructorAssignment?> GetAdvisorAsync(long academicBatchId, long academicYearId)
    {
        var query =
            from assignment in _dbSet.AsNoTracking()
            join offering in _context.Set<SubjectOffering>() on assignment.SubjectOfferingId equals offering.Id
            where offering.AcademicBatchId == academicBatchId
                && offering.AcademicYearId == academicYearId
                && assignment.IsActive && assignment.IsPrimary
            orderby assignment.EffectiveFrom descending
            select assignment;
        return await query.FirstOrDefaultAsync();
    }
}
