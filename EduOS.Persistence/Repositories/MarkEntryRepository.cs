using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

public class MarkEntryRepository : GenericRepository<StudentAssessmentMark>, IMarkEntryRepository
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
}
