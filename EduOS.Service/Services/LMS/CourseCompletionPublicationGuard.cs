using EduOS.Core.Entities.LMS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IRepositories;
using Microsoft.EntityFrameworkCore;
namespace EduOS.Service.Services.LMS;
internal static class CourseCompletionPublicationGuard
{
    internal const string Message = "Course content is locked after an enrollment is completed. Create a new course version.";
    internal static Task<bool> IsLockedAsync(IGenericRepository<CourseEnrollment> enrollments, long tenantId,
        long courseId, CancellationToken ct) => enrollments.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenantId && x.CourseId == courseId && !x.IsDeleted &&
            x.State == CourseEnrollmentState.Completed, ct);
}
