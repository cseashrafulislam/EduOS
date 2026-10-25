using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
namespace EduOS.Service.Services.LMS;
public sealed partial class LmsAssessmentService
{
    // Read-only access status; writes independently recheck course and enrollment state.
    public async Task<ApiResponse<QuizAttemptEligibilityDto>> GetMyQuizAttemptEligibilityAsync(
        Guid courseReference, CancellationToken ct = default)
    {
        if (!Learner) return Fail<QuizAttemptEligibilityDto>("Student permission required.", 403);
        if (courseReference == Guid.Empty) return Fail<QuizAttemptEligibilityDto>("Course reference required.");
        var course = await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.PublicId == courseReference && !x.IsDeleted, ct);
        var studentId = await StudentIdAsync(ct);
        if (course == null || studentId == 0)
            return Fail<QuizAttemptEligibilityDto>("Course enrollment required.", 403);
        var enrollments = await _enrollments.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.CourseId == course.Id && x.StudentId == studentId &&
            !x.IsDeleted && (x.State == CourseEnrollmentState.Active ||
                             x.State == CourseEnrollmentState.Completed))
            .OrderByDescending(x => x.Id).ToListAsync(ct);
        var active = enrollments.FirstOrDefault(x => x.State == CourseEnrollmentState.Active);
        if (course.IsActive && active != null)
            return ApiResponse<QuizAttemptEligibilityDto>.SuccessResponse(new QuizAttemptEligibilityDto
            {
                CourseReference = courseReference, CanAttempt = true,
                CourseEnrollmentId = active.Id, Reason = "Active course enrollment."
            });
        var completed = enrollments.Where(x => x.State == CourseEnrollmentState.Completed).ToList();
        if (completed.Count == 0)
            return Fail<QuizAttemptEligibilityDto>("Course enrollment required.", 403);
        if (!course.IsActive)
        {
            var allowedIds = await HistoricalEnrollmentIdsAsync(course.Id, studentId, ct);
            if (!completed.Any(x => allowedIds.Contains(x.Id)))
                return Fail<QuizAttemptEligibilityDto>("Course enrollment required.", 403);
        }
        return ApiResponse<QuizAttemptEligibilityDto>.SuccessResponse(new QuizAttemptEligibilityDto
        {
            CourseReference = courseReference, CanAttempt = false, CourseEnrollmentId = 0,
            Reason = course.IsActive
                ? "Course completed. New quiz attempts are unavailable."
                : "Archived course. New quiz attempts are unavailable."
        });
    }

    public async Task<ApiResponse<long>> GetMyCourseEnrollmentIdAsync(Guid courseReference, CancellationToken ct = default)
    {
        if (!Learner) return Fail<long>("Student permission required.", 403);
        var courseId = await _courses.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.PublicId == courseReference && x.IsActive && !x.IsDeleted)
            .Select(x => x.Id).FirstOrDefaultAsync(ct);
        var studentId = await StudentIdAsync(ct);
        if (courseId == 0 || studentId == 0) return Fail<long>("Course enrollment required.", 403);
        var enrollmentId = await _enrollments.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.CourseId == courseId && x.StudentId == studentId &&
            !x.IsDeleted && x.State == CourseEnrollmentState.Active).OrderByDescending(x => x.Id)
            .Select(x => x.Id).FirstOrDefaultAsync(ct);
        return enrollmentId == 0 ? Fail<long>("Course enrollment required.", 403) :
            ApiResponse<long>.SuccessResponse(enrollmentId);
    }
}
