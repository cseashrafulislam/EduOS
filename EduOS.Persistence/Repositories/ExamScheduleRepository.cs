using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

public class ExamScheduleRepository : GenericRepository<AssessmentSchedule>, IExamScheduleRepository
{
    public ExamScheduleRepository(EduOSDbContext context) : base(context) { }

    public async Task<List<AssessmentSchedule>> GetByExamAndClassAsync(long examId, long classId)
    {
        var query =
            from schedule in _dbSet.AsNoTracking()
            join subject in _context.Set<AssessmentSubject>() on schedule.AssessmentSubjectId equals subject.Id
            join offering in _context.Set<SubjectOffering>() on subject.SubjectOfferingId equals offering.Id
            join batch in _context.Set<AcademicBatch>() on offering.AcademicBatchId equals batch.Id
            where subject.AssessmentId == examId && batch.AcademicLevelId == classId
            orderby schedule.AssessmentDate, schedule.StartTime
            select schedule;
        return await query.ToListAsync();
    }

    public Task<List<AssessmentSchedule>> GetByDateAsync(DateTime date, long tenantId)
    {
        var target = DateOnly.FromDateTime(date);
        return _dbSet.AsNoTracking().Where(x => x.TenantId == tenantId && x.AssessmentDate == target)
            .OrderBy(x => x.StartTime).ToListAsync();
    }
}
