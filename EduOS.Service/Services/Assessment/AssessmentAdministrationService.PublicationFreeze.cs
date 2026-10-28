using EduOS.Core.Entities.Assessment;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    // Historical publication is terminal evidence even when visibility was withdrawn.
    // Scheme edits are frozen across the assessment; marks/review are scoped to a batch.
    private IQueryable<ResultPublication> FinalizedPublications(long assessmentId) =>
        _publications.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.AssessmentId == assessmentId && !x.IsDeleted &&
            (x.State == ResultPublicationState.Published ||
             x.State == ResultPublicationState.Withdrawn));

    private Task<bool> HasFinalizedPublicationAsync(long assessmentId, long batchId,
        CancellationToken ct) =>
        FinalizedPublications(assessmentId).AnyAsync(x => x.AcademicBatchId == batchId, ct);

    private Task<bool> HasAnyFinalizedPublicationAsync(long assessmentId, CancellationToken ct) =>
        FinalizedPublications(assessmentId).AnyAsync(ct);
}
