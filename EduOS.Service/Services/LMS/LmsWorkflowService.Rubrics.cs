using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.LMS;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EduOS.Service.Services.LMS;

public sealed partial class LmsWorkflowService
{
    public async Task<ApiResponse<AssignmentRubricDto>> GetAssignmentRubricAsync(Guid assignmentReference,
        CancellationToken ct = default)
    {
        if (!Authenticated() || assignmentReference == Guid.Empty)
            return Error<AssignmentRubricDto>("Assignment unavailable.", 404);
        var assignment = await _assignments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.PublicId == assignmentReference && !x.IsDeleted, ct);
        if (assignment == null) return Error<AssignmentRubricDto>("Assignment unavailable.", 404);
        var course = await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.Id == assignment.CourseId && !x.IsDeleted, ct);
        if (course == null || !await CanViewAsync(course, ct) ||
            (!CanTeach() && !assignment.IsPublished))
            return Error<AssignmentRubricDto>("Assignment unavailable.", 404);
        if (_rubricCriteria == null) return Error<AssignmentRubricDto>("Rubric service unavailable.", 503);
        var criteria = await _rubricCriteria.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _user.TenantId && x.AssignmentId == assignment.Id && !x.IsDeleted)
            .OrderBy(x => x.DisplayOrder).Select(x => new AssignmentRubricCriterionDto
            {
                Reference = x.PublicId, Title = x.Title, DisplayOrder = x.DisplayOrder, MaxMarks = x.MaxMarks
            }).ToListAsync(ct);
        return ApiResponse<AssignmentRubricDto>.SuccessResponse(new AssignmentRubricDto
        {
            AssignmentReference = assignment.PublicId,
            RowVersion = Convert.ToBase64String(assignment.RowVersion), Criteria = criteria
        });
    }

    public async Task<ApiResponse<AssignmentRubricDto>> SaveAssignmentRubricAsync(Guid assignmentReference,
        SaveAssignmentRubricRequestDto request, CancellationToken ct = default)
    {
        if (!CanTeach()) return Error<AssignmentRubricDto>("LMS authoring permission required.", 403);
        if (_rubricCriteria == null) return Error<AssignmentRubricDto>("Rubric service unavailable.", 503);
        var criteria = request?.Criteria;
        if (assignmentReference == Guid.Empty || criteria == null || criteria.Count > 20 ||
            !TryVersion(request!.RowVersion, out _) ||
            criteria.Any(x => x == null || string.IsNullOrWhiteSpace(x.Title) || x.Title.Trim().Length > 200 ||
                x.MaxMarks <= 0 || x.MaxMarks > 100000 || decimal.Round(x.MaxMarks, 2) != x.MaxMarks) ||
            criteria.Select(x => x.Title.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != criteria.Count)
            return Error<AssignmentRubricDto>("Use up to 20 distinct criteria with valid titles and two-decimal marks.");
        var tenant = _user.TenantId;
        var assignment = await _assignments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.PublicId == assignmentReference && !x.IsDeleted, ct);
        if (assignment == null) return Error<AssignmentRubricDto>("Assignment unavailable.", 404);
        var course = await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == assignment.CourseId && !x.IsDeleted, ct);
        if (course == null || !await CanEditAsync(course, ct))
            return Error<AssignmentRubricDto>("Instructor may not change this rubric.", 403);
        if (criteria.Count > 0 && criteria.Sum(x => x.MaxMarks) != assignment.MaxMarks)
            return Error<AssignmentRubricDto>("Rubric criteria must total the assignment maximum marks.", 409);
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                if (await CourseCompletionPublicationGuard.IsLockedAsync(
                    _enrollments, tenant, assignment.CourseId, token))
                    return Error<AssignmentRubricDto>(CourseCompletionPublicationGuard.Message, 409);
                var row = await _assignments.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == assignment.Id && !x.IsDeleted, token);
                if (row == null) return Error<AssignmentRubricDto>("Assignment unavailable.", 404);
                if (!Matches(row.RowVersion, request.RowVersion))
                    return Error<AssignmentRubricDto>("Assignment changed. Reload before saving the rubric.", 409);
                if (criteria.Count > 0 && criteria.Sum(x => x.MaxMarks) != row.MaxMarks)
                    return Error<AssignmentRubricDto>("Rubric total changed. Reload assignment.", 409);
                if (await _submissions.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.AssignmentId == row.Id && !x.IsDeleted &&
                    x.State != LearningSubmissionState.Draft, token))
                    return Error<AssignmentRubricDto>("Submitted work locks the rubric. Create a new assignment.", 409);
                var now = _clock.GetUtcNow().UtcDateTime;
                var existing = await _rubricCriteria.GetQueryable().Where(x =>
                    x.TenantId == tenant && x.AssignmentId == row.Id && !x.IsDeleted).ToListAsync(token);
                foreach (var old in existing)
                {
                    old.IsDeleted = true; old.DeletedAt = now; old.DeletedBy = _user.UserId;
                    _rubricCriteria.Update(old);
                }
                row.UpdatedAt = now; row.UpdatedBy = _user.UserId;
                _assignments.Update(row);
                // Unique order indexes exclude retired rubric rows; flush retirement first
                // so adding replacement rows is safe inside this single transaction.
                await _uow.SaveChangesAsync(token);
                for (var i = 0; i < criteria.Count; i++)
                {
                    await _rubricCriteria.AddAsync(new AssignmentRubricCriterion
                    {
                        TenantId = tenant, AssignmentId = row.Id, PublicId = Guid.NewGuid(),
                        Title = criteria[i].Title.Trim(), MaxMarks = criteria[i].MaxMarks,
                        DisplayOrder = i + 1, CreatedAt = now, CreatedBy = _user.UserId
                    });
                }
                await _uow.SaveChangesAsync(token);
                var saved = await _rubricCriteria.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && x.AssignmentId == row.Id && !x.IsDeleted)
                    .OrderBy(x => x.DisplayOrder).Select(x => new AssignmentRubricCriterionDto
                    {
                        Reference = x.PublicId, Title = x.Title, MaxMarks = x.MaxMarks, DisplayOrder = x.DisplayOrder
                    }).ToListAsync(token);
                return ApiResponse<AssignmentRubricDto>.SuccessResponse(new AssignmentRubricDto
                {
                    AssignmentReference = row.PublicId,
                    RowVersion = Convert.ToBase64String(row.RowVersion), Criteria = saved
                }, "Rubric saved.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        { return Error<AssignmentRubricDto>("Rubric changed concurrently.", 409); }
        catch (DbUpdateException)
        { return Error<AssignmentRubricDto>("Rubric update conflicts with existing data.", 409); }
    }

    public async Task<ApiResponse<IReadOnlyList<AssignmentRubricScoreDto>>> GetSubmissionRubricScoresAsync(
        long submissionId, CancellationToken ct = default)
    {
        if (!Authenticated() || submissionId <= 0)
            return Error<IReadOnlyList<AssignmentRubricScoreDto>>("Submission unavailable.", 404);
        if (_rubricCriteria == null || _rubricScores == null)
            return Error<IReadOnlyList<AssignmentRubricScoreDto>>("Rubric service unavailable.", 503);
        var tenant = _user.TenantId;
        var row = await _submissions.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == submissionId && !x.IsDeleted &&
            x.State == LearningSubmissionState.Graded, ct);
        if (row == null) return Error<IReadOnlyList<AssignmentRubricScoreDto>>("Submission unavailable.", 404);
        var assignment = await _assignments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == row.AssignmentId && !x.IsDeleted, ct);
        var course = assignment == null ? null : await _courses.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == assignment.CourseId && !x.IsDeleted, ct);
        if (course == null) return Error<IReadOnlyList<AssignmentRubricScoreDto>>("Submission unavailable.", 404);
        var canTeach = await CanEditAsync(course, ct);
        if (!canTeach)
        {
            if (!_user.IsInRole("Student"))
                return Error<IReadOnlyList<AssignmentRubricScoreDto>>("Submission unavailable.", 404);
            var student = await OwnStudentIdAsync(ct);
            if (student == 0 || !await _enrollments.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.Id == row.CourseEnrollmentId && x.CourseId == course.Id &&
                x.StudentId == student && !x.IsDeleted, ct))
                return Error<IReadOnlyList<AssignmentRubricScoreDto>>("Submission unavailable.", 404);
        }
        IReadOnlyList<AssignmentRubricScoreDto> scores = await (
            from score in _rubricScores.GetQueryable().AsNoTracking()
            join criterion in _rubricCriteria.GetQueryable().AsNoTracking()
                on new { score.TenantId, CriterionId = score.AssignmentRubricCriterionId }
                equals new { criterion.TenantId, CriterionId = criterion.Id }
            where score.TenantId == tenant && score.AssignmentSubmissionId == submissionId && !score.IsDeleted &&
                criterion.AssignmentId == assignment!.Id
            orderby criterion.DisplayOrder
            select new AssignmentRubricScoreDto
            {
                CriterionReference = criterion.PublicId, AwardedMarks = score.AwardedMarks
            }).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<AssignmentRubricScoreDto>>.SuccessResponse(scores);
    }
}
