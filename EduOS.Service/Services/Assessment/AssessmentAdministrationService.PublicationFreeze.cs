using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    // A withdrawn publication remains historical evidence; it never reopens ordinary
    // marking. All write paths use this tenant/batch-scoped, SQL-indexed predicate.
    private Task<bool> HasFinalizedPublicationAsync(long assessmentId, long batchId,
        CancellationToken ct) =>
        _publications.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == _user.TenantId && x.AssessmentId == assessmentId &&
            x.AcademicBatchId == batchId && !x.IsDeleted &&
            (x.State == ResultPublicationState.Published ||
             x.State == ResultPublicationState.Withdrawn), ct);
}
