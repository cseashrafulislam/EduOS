using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

public class MarkEntryRepository : GenericRepository<StudentAssessmentMark>, IStudentAssessmentMarkRepository
{
    public MarkEntryRepository(EduOSDbContext context) : base(context) { }

    public async Task<List<StudentAssessmentMark>> GetByExamAndStudentAsync(long examId, long studentId)
    {
        var query =
            from mark in _dbSet.AsNoTracking()
            join assessmentSubject in _context.Set<AssessmentSubject>() on mark.AssessmentSubjectId equals assessmentSubject.Id
            join registration in _context.Set<StudentSubjectRegistration>() on mark.StudentSubjectRegistrationId equals registration.Id
            join enrollment in _context.Set<StudentEnrollment>() on registration.StudentEnrollmentId equals enrollment.Id
            where assessmentSubject.AssessmentId == examId && enrollment.StudentId == studentId
            select mark;
        return await query.ToListAsync();
    }

    public async Task<List<StudentAssessmentMark>> GetByExamAndSubjectAsync(long examId, long subjectId, long classId)
    {
        var query =
            from mark in _dbSet.AsNoTracking()
            join assessmentSubject in _context.Set<AssessmentSubject>() on mark.AssessmentSubjectId equals assessmentSubject.Id
            join offering in _context.Set<SubjectOffering>() on assessmentSubject.SubjectOfferingId equals offering.Id
            join curriculumSubject in _context.Set<CurriculumSubject>() on offering.CurriculumSubjectId equals curriculumSubject.Id
            join registration in _context.Set<StudentSubjectRegistration>() on mark.StudentSubjectRegistrationId equals registration.Id
            join enrollment in _context.Set<StudentEnrollment>() on registration.StudentEnrollmentId equals enrollment.Id
            where assessmentSubject.AssessmentId == examId && curriculumSubject.SubjectId == subjectId && enrollment.AcademicLevelId == classId
            orderby enrollment.RollNo
            select mark;
        return await query.ToListAsync();
    }

    public async Task<StudentAssessmentMark?> GetExistingAsync(long examId, long studentId, long subjectId)
    {
        var query =
            from mark in _dbSet
            join assessmentSubject in _context.Set<AssessmentSubject>() on mark.AssessmentSubjectId equals assessmentSubject.Id
            join offering in _context.Set<SubjectOffering>() on assessmentSubject.SubjectOfferingId equals offering.Id
            join curriculumSubject in _context.Set<CurriculumSubject>() on offering.CurriculumSubjectId equals curriculumSubject.Id
            join registration in _context.Set<StudentSubjectRegistration>() on mark.StudentSubjectRegistrationId equals registration.Id
            join enrollment in _context.Set<StudentEnrollment>() on registration.StudentEnrollmentId equals enrollment.Id
            where assessmentSubject.AssessmentId == examId && enrollment.StudentId == studentId && curriculumSubject.SubjectId == subjectId
            select mark;
        return await query.FirstOrDefaultAsync();
    }

    public async Task<bool> IsAllMarksEnteredAsync(long examId, long classId)
    {
        var expected =
            from assessmentSubject in _context.Set<AssessmentSubject>()
            join offering in _context.Set<SubjectOffering>() on assessmentSubject.SubjectOfferingId equals offering.Id
            join registration in _context.Set<StudentSubjectRegistration>() on offering.Id equals registration.SubjectOfferingId
            join enrollment in _context.Set<StudentEnrollment>() on registration.StudentEnrollmentId equals enrollment.Id
            where assessmentSubject.AssessmentId == examId && enrollment.AcademicLevelId == classId
                && registration.State == SubjectRegistrationState.Approved
            select new { assessmentSubject.Id, RegistrationId = registration.Id };

        if (!await expected.AnyAsync()) return false;

        // Count-only comparison incorrectly reports complete when a duplicate or unrelated
        // mark replaces a missing required registration. Check each expected pairing instead.
        return !await expected.AnyAsync(item => !_dbSet.Any(mark =>
            mark.AssessmentSubjectId == item.Id &&
            mark.StudentSubjectRegistrationId == item.RegistrationId));
    }

    public async Task<List<StudentAssessmentMark>> GetByAssessmentAndEnrollmentAsync(long assessmentId, long studentEnrollmentId, CancellationToken cancellationToken)
    {
        var ids = _context.Set<StudentSubjectRegistration>().Where(x => x.StudentEnrollmentId == studentEnrollmentId).Select(x => x.Id);
        var subjects = _context.Set<AssessmentSubject>().Where(x => x.AssessmentId == assessmentId).Select(x => x.Id);
        return await _dbSet.AsNoTracking().Where(x => ids.Contains(x.StudentSubjectRegistrationId)
            && subjects.Contains(x.AssessmentSubjectId)).OrderBy(x => x.AssessmentSubjectId).ThenBy(x => x.Id).ToListAsync(cancellationToken);
    }
    public Task<(List<StudentAssessmentMark> Items, int TotalCount)> GetByAssessmentSubjectAsync(long assessmentSubjectId, int page, int pageSize, CancellationToken cancellationToken) =>
        PageAsync(_dbSet.AsNoTracking().Where(x => x.AssessmentSubjectId == assessmentSubjectId)
            .OrderBy(x => x.StudentSubjectRegistrationId).ThenBy(x => x.Id), page, pageSize, cancellationToken);
    public Task<StudentAssessmentMark?> GetExistingAsync(long assessmentSubjectId, long studentSubjectRegistrationId, CancellationToken cancellationToken) =>
        _dbSet.FirstOrDefaultAsync(x => x.AssessmentSubjectId == assessmentSubjectId
            && x.StudentSubjectRegistrationId == studentSubjectRegistrationId, cancellationToken);
    public async Task<bool> AreRequiredMarksEnteredAsync(long assessmentId, long academicBatchId, CancellationToken cancellationToken)
    {
        var required = from subject in _context.Set<AssessmentSubject>()
                       join offering in _context.Set<SubjectOffering>() on subject.SubjectOfferingId equals offering.Id
                       join registration in _context.Set<StudentSubjectRegistration>() on offering.Id equals registration.SubjectOfferingId
                       join enrollment in _context.Set<StudentEnrollment>() on registration.StudentEnrollmentId equals enrollment.Id
                       where subject.AssessmentId == assessmentId && offering.AcademicBatchId == academicBatchId
                           && enrollment.AcademicBatchId == academicBatchId && registration.State == SubjectRegistrationState.Approved
                       select new { SubjectId = subject.Id, RegistrationId = registration.Id };
        if (!await required.AnyAsync(cancellationToken)) return false;
        return !await required.AnyAsync(x => !_dbSet.Any(mark => mark.AssessmentSubjectId == x.SubjectId
            && mark.StudentSubjectRegistrationId == x.RegistrationId), cancellationToken);
    }
}
