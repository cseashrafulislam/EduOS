using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

public class ResultRepository : GenericRepository<StudentResultSummary>, IStudentResultSummaryRepository
{
    public ResultRepository(EduOSDbContext context) : base(context) { }

    public async Task<StudentResultSummary?> GetByExamAndStudentAsync(long examId, long studentId)
    {
        var query =
            from result in _dbSet.AsNoTracking()
            join enrollment in _context.Set<StudentEnrollment>() on result.StudentEnrollmentId equals enrollment.Id
            where result.AssessmentId == examId && enrollment.StudentId == studentId
            orderby result.PublicationVersionNo descending
            select result;
        return await query.FirstOrDefaultAsync();
    }

    public async Task<List<StudentResultSummary>> GetByExamAndClassAsync(long examId, long classId)
    {
        var query =
            from result in _dbSet.AsNoTracking()
            join enrollment in _context.Set<StudentEnrollment>() on result.StudentEnrollmentId equals enrollment.Id
            where result.AssessmentId == examId && enrollment.AcademicLevelId == classId
            orderby result.MeritPosition ?? int.MaxValue, enrollment.RollNo
            select result;
        return await query.ToListAsync();
    }

    public async Task<List<StudentResultSummary>> GetTopRankersAsync(long examId, long classId, int top = 10)
    {
        var take = Math.Clamp(top, 1, 100);
        var query =
            from result in _dbSet.AsNoTracking()
            join enrollment in _context.Set<StudentEnrollment>() on result.StudentEnrollmentId equals enrollment.Id
            where result.AssessmentId == examId && enrollment.AcademicLevelId == classId
                && result.IsPassed && !result.IsWithheld && result.MeritPosition != null
            orderby result.MeritPosition
            select result;
        return await query.Take(take).ToListAsync();
    }

    public Task<StudentResultSummary?> GetByPublicationAndEnrollmentAsync(long resultPublicationId, long studentEnrollmentId, CancellationToken cancellationToken) =>
        _dbSet.AsNoTracking().FirstOrDefaultAsync(x => x.ResultPublicationId == resultPublicationId
            && x.StudentEnrollmentId == studentEnrollmentId, cancellationToken);
    public Task<(List<StudentResultSummary> Items, int TotalCount)> GetByPublicationAsync(long resultPublicationId, int page, int pageSize, CancellationToken cancellationToken) =>
        PageAsync(_dbSet.AsNoTracking().Where(x => x.ResultPublicationId == resultPublicationId)
            .OrderBy(x => x.MeritPosition ?? int.MaxValue).ThenBy(x => x.Id), page, pageSize, cancellationToken);
    public Task<List<StudentResultSummary>> GetTopRankersAsync(long resultPublicationId, int top, CancellationToken cancellationToken) =>
        _dbSet.AsNoTracking().Where(x => x.ResultPublicationId == resultPublicationId
            && x.IsPassed && !x.IsWithheld && x.MeritPosition != null)
            .OrderBy(x => x.MeritPosition).ThenBy(x => x.Id).Take(Math.Clamp(top, 1, 100)).ToListAsync(cancellationToken);
}
