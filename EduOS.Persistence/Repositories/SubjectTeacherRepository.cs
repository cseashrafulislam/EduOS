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

    public Task<List<InstructorAssignment>> GetByEmployeeAsync(long employeeId, DateOnly effectiveOn, CancellationToken cancellationToken) =>
        _dbSet.AsNoTracking().Where(x => x.EmployeeId == employeeId && x.IsActive
            && x.EffectiveFrom <= effectiveOn && (x.EffectiveTo == null || x.EffectiveTo >= effectiveOn))
            .OrderBy(x => x.SubjectOfferingId).ThenBy(x => x.Id).ToListAsync(cancellationToken);
    public async Task<List<InstructorAssignment>> GetByAcademicBatchAsync(long academicBatchId, DateOnly effectiveOn, CancellationToken cancellationToken)
    {
        var offerings = _context.Set<SubjectOffering>().Where(x => x.AcademicBatchId == academicBatchId).Select(x => x.Id);
        return await _dbSet.AsNoTracking().Where(x => offerings.Contains(x.SubjectOfferingId) && x.IsActive
            && x.EffectiveFrom <= effectiveOn && (x.EffectiveTo == null || x.EffectiveTo >= effectiveOn))
            .OrderByDescending(x => x.IsPrimary).ThenBy(x => x.SubjectOfferingId).ThenBy(x => x.Id).ToListAsync(cancellationToken);
    }
    public Task<InstructorAssignment?> GetPrimaryBySubjectOfferingAsync(long subjectOfferingId, DateOnly effectiveOn, CancellationToken cancellationToken) =>
        _dbSet.AsNoTracking().Where(x => x.SubjectOfferingId == subjectOfferingId && x.IsPrimary && x.IsActive
            && x.EffectiveFrom <= effectiveOn && (x.EffectiveTo == null || x.EffectiveTo >= effectiveOn))
            .OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Id).FirstOrDefaultAsync(cancellationToken);
}
