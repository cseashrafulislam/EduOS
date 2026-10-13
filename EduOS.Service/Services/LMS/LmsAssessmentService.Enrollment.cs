using EduOS.Core.Common;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
namespace EduOS.Service.Services.LMS;
public sealed partial class LmsAssessmentService
{
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
            !x.IsDeleted && (x.State == CourseEnrollmentState.Active ||
            x.State == CourseEnrollmentState.Completed)).OrderByDescending(x => x.Id)
            .Select(x => x.Id).FirstOrDefaultAsync(ct);
        return enrollmentId == 0 ? Fail<long>("Course enrollment required.", 403) :
            ApiResponse<long>.SuccessResponse(enrollmentId);
    }
}
