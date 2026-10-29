using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.LMS;

public sealed partial class LmsWorkflowService
{
    public async Task<ApiResponse<PagedResult<LmsLearnerCourseProgressDto>>> GetMyCourseProgressAsync(
        int page, int pageSize, CancellationToken ct = default, bool includeArchived = false)
    {
        if (!Authenticated() || !_user.IsInRole("Student"))
            return Error<PagedResult<LmsLearnerCourseProgressDto>>("Student permission required.", 403);
        if (page < 1 || pageSize is < 1 or > 25 || ((long)page - 1) * pageSize > int.MaxValue)
            return Error<PagedResult<LmsLearnerCourseProgressDto>>("Invalid pagination.");
        if (_completionQuizzes == null || _completionAttempts == null)
            return Error<PagedResult<LmsLearnerCourseProgressDto>>("LMS progress repositories unavailable.", 503);

        var studentId = await OwnStudentIdAsync(ct);
        if (studentId == 0)
            return ApiResponse<PagedResult<LmsLearnerCourseProgressDto>>.SuccessResponse(
                EmptyPage<LmsLearnerCourseProgressDto>(page, pageSize));
        var tenant = _user.TenantId;
        var historicalIds = includeArchived ? HistoricalCourseIdsQuery(studentId) : null;
        var query = from enrollment in _enrollments.GetQueryable().AsNoTracking()
                    join course in _courses.GetQueryable().AsNoTracking() on enrollment.CourseId equals course.Id
                    where enrollment.TenantId == tenant && course.TenantId == tenant &&
                          enrollment.StudentId == studentId && !enrollment.IsDeleted &&
                          !course.IsDeleted &&
                          (enrollment.State == CourseEnrollmentState.Active ||
                           enrollment.State == CourseEnrollmentState.Completed)
                    select new { Enrollment = enrollment, Course = course };
        if (historicalIds == null)
            query = query.Where(x => x.Course.IsActive);
        else
            query = query.Where(x => x.Course.IsActive ||
                (x.Enrollment.State == CourseEnrollmentState.Completed &&
                 historicalIds.Contains(x.Course.Id)));
        var total = await query.CountAsync(ct);
        var pageRows = await query.OrderByDescending(x => x.Enrollment.EnrolledAt)
            .ThenByDescending(x => x.Enrollment.Id).Skip((int)(((long)page - 1) * pageSize))
            .Take(pageSize).Select(x => new
            {
                EnrollmentId = x.Enrollment.Id, x.Enrollment.CourseId, x.Enrollment.State,
                x.Enrollment.EnrolledAt, x.Enrollment.CompletedAt,
                CourseReference = x.Course.PublicId, CourseCode = x.Course.Code,
                CourseTitle = x.Course.Title, IsArchived = !x.Course.IsActive
            }).ToListAsync(ct);
        if (pageRows.Count == 0)
            return ApiResponse<PagedResult<LmsLearnerCourseProgressDto>>.SuccessResponse(
                new PagedResult<LmsLearnerCourseProgressDto>
                { Page = page, PageSize = pageSize, TotalCount = total, Items = [] });

        var courseIds = pageRows.Select(x => x.CourseId).Distinct().ToArray();
        var enrollmentIds = pageRows.Select(x => x.EnrollmentId).ToArray();
        var requiredLessons = await _lessons.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && courseIds.Contains(x.CourseId) &&
                        x.IsPublished && !x.IsDeleted)
            .GroupBy(x => x.CourseId).Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var completedLessons = await (
            from progress in _progress.GetQueryable().AsNoTracking()
            join lesson in _lessons.GetQueryable().AsNoTracking() on progress.LessonId equals lesson.Id
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on progress.CourseEnrollmentId equals enrollment.Id
            where progress.TenantId == tenant && lesson.TenantId == tenant &&
                  enrollment.TenantId == tenant && !enrollment.IsDeleted &&
                  enrollment.StudentId == studentId && enrollment.CourseId == lesson.CourseId &&
                  enrollmentIds.Contains(progress.CourseEnrollmentId) &&
                  courseIds.Contains(lesson.CourseId) && lesson.IsPublished && !lesson.IsDeleted &&
                  !progress.IsDeleted && progress.IsCompleted && progress.ProgressPercent >= 100m
            select new { progress.CourseEnrollmentId, progress.LessonId })
            .Distinct().GroupBy(x => x.CourseEnrollmentId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var requiredAssignments = await _assignments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && courseIds.Contains(x.CourseId) &&
                        x.IsPublished && !x.IsDeleted)
            .GroupBy(x => x.CourseId).Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var gradedAssignments = await (
            from submission in _submissions.GetQueryable().AsNoTracking()
            join assignment in _assignments.GetQueryable().AsNoTracking()
                on submission.AssignmentId equals assignment.Id
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on submission.CourseEnrollmentId equals enrollment.Id
            where submission.TenantId == tenant && assignment.TenantId == tenant &&
                  enrollment.TenantId == tenant && !enrollment.IsDeleted &&
                  enrollment.StudentId == studentId && enrollment.CourseId == assignment.CourseId &&
                  enrollmentIds.Contains(submission.CourseEnrollmentId) &&
                  courseIds.Contains(assignment.CourseId) && assignment.IsPublished &&
                  !assignment.IsDeleted && !submission.IsDeleted &&
                  submission.State == LearningSubmissionState.Graded && submission.Marks.HasValue
            select new { submission.CourseEnrollmentId, submission.AssignmentId })
            .Distinct().GroupBy(x => x.CourseEnrollmentId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var requiredQuizzes = await _completionQuizzes.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && courseIds.Contains(x.CourseId) &&
                        x.IsPublished && !x.IsDeleted)
            .GroupBy(x => x.CourseId).Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var passedQuizzes = await (
            from attempt in _completionAttempts.GetQueryable().AsNoTracking()
            join quiz in _completionQuizzes.GetQueryable().AsNoTracking() on attempt.QuizId equals quiz.Id
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on attempt.CourseEnrollmentId equals enrollment.Id
            where attempt.TenantId == tenant && quiz.TenantId == tenant &&
                  enrollment.TenantId == tenant && !enrollment.IsDeleted &&
                  enrollment.StudentId == studentId && enrollment.CourseId == quiz.CourseId &&
                  enrollmentIds.Contains(attempt.CourseEnrollmentId) &&
                  courseIds.Contains(quiz.CourseId) && quiz.IsPublished && !quiz.IsDeleted &&
                  !attempt.IsDeleted && attempt.State == QuizAttemptState.Graded &&
                  attempt.ObtainedMarks.HasValue && attempt.ObtainedMarks.Value >= quiz.PassMarks
            select new { attempt.CourseEnrollmentId, attempt.QuizId })
            .Distinct().GroupBy(x => x.CourseEnrollmentId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);

        var rows = pageRows.Select(x =>
        {
            var lessons = requiredLessons.GetValueOrDefault(x.CourseId);
            var completed = Math.Min(lessons, completedLessons.GetValueOrDefault(x.EnrollmentId));
            return new LmsLearnerCourseProgressDto
            {
                CourseEnrollmentId = x.EnrollmentId, CourseReference = x.CourseReference,
                CourseCode = x.CourseCode, CourseTitle = x.CourseTitle, IsArchived = x.IsArchived, State = x.State,
                EnrolledAt = x.EnrolledAt, CompletedAt = x.CompletedAt,
                RequiredLessons = lessons, CompletedLessons = completed,
                RequiredAssignments = requiredAssignments.GetValueOrDefault(x.CourseId),
                GradedAssignments = gradedAssignments.GetValueOrDefault(x.EnrollmentId),
                RequiredQuizzes = requiredQuizzes.GetValueOrDefault(x.CourseId),
                PassedQuizzes = passedQuizzes.GetValueOrDefault(x.EnrollmentId),
                LessonCompletionPercent = lessons == 0 ? 0m :
                    Math.Round(100m * completed / lessons, 2, MidpointRounding.AwayFromZero)
            };
        }).ToList();
        return ApiResponse<PagedResult<LmsLearnerCourseProgressDto>>.SuccessResponse(
            new PagedResult<LmsLearnerCourseProgressDto>
            { Page = page, PageSize = pageSize, TotalCount = total, Items = rows });
    }

    public async Task<ApiResponse<PagedResult<LessonProgressDto>>> GetMyLessonProgressAsync(
        Guid courseReference, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Authenticated() || !_user.IsInRole("Student"))
            return Error<PagedResult<LessonProgressDto>>("Student permission required.", 403);
        if (courseReference == Guid.Empty || page < 1 || pageSize is < 1 or > 100 ||
            ((long)page - 1) * pageSize > int.MaxValue)
            return Error<PagedResult<LessonProgressDto>>("Invalid course or pagination.");
        var studentId = await OwnStudentIdAsync(ct);
        if (studentId == 0) return Error<PagedResult<LessonProgressDto>>("Course not found.", 404);
        var tenant = _user.TenantId;
        var course = await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.PublicId == courseReference && !x.IsDeleted, ct);
        if (course == null || !await CanViewAsync(course, ct))
            return Error<PagedResult<LessonProgressDto>>("Course not found.", 404);
        var eligibleEnrollments = _enrollments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.CourseId == course.Id &&
                        x.StudentId == studentId && !x.IsDeleted &&
                        (x.State == CourseEnrollmentState.Active || x.State == CourseEnrollmentState.Completed));
        if (!course.IsActive)
        {
            // A second, later enrollment without a valid original academic link must not
            // replace the historically authorized completed enrollment's progress.
            var historicalEnrollmentIds = HistoricalCourseEnrollmentIdsQuery(studentId);
            if (historicalEnrollmentIds == null)
                return Error<PagedResult<LessonProgressDto>>("Course not found.", 404);
            eligibleEnrollments = eligibleEnrollments.Where(x =>
                x.State == CourseEnrollmentState.Completed && historicalEnrollmentIds.Contains(x.Id));
        }
        var enrollment = await eligibleEnrollments
            .OrderByDescending(x => x.EnrolledAt).ThenByDescending(x => x.Id)
            .Select(x => x.Id).FirstOrDefaultAsync(ct);
        if (enrollment == 0) return Error<PagedResult<LessonProgressDto>>("Course not found.", 404);
        var lessons = _lessons.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.CourseId == course.Id && x.IsPublished && !x.IsDeleted);
        var total = await lessons.CountAsync(ct);
        var lessonRows = await lessons.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id)
            .Skip((int)(((long)page - 1) * pageSize)).Take(pageSize)
            .Select(x => new { x.Id, x.PublicId, x.Title }).ToListAsync(ct);
        var lessonIds = lessonRows.Select(x => x.Id).ToArray();
        var saved = await _progress.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.CourseEnrollmentId == enrollment &&
            lessonIds.Contains(x.LessonId) && !x.IsDeleted)
            .OrderByDescending(x => x.Id).ToListAsync(ct);
        var progress = saved.GroupBy(x => x.LessonId).ToDictionary(x => x.Key, x => x.First());
        var rows = lessonRows.Select(x =>
        {
            progress.TryGetValue(x.Id, out var record);
            return new LessonProgressDto
            {
                Id = record?.Id ?? 0, CourseEnrollmentId = enrollment,
                LessonReference = x.PublicId, LessonTitle = x.Title,
                IsCompleted = record?.IsCompleted ?? false,
                CompletedAt = record?.CompletedAt,
                ProgressPercent = record?.ProgressPercent ?? 0m
            };
        }).ToList();
        return ApiResponse<PagedResult<LessonProgressDto>>.SuccessResponse(
            new PagedResult<LessonProgressDto>
            { Page = page, PageSize = pageSize, TotalCount = total, Items = rows });
    }
}
