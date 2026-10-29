using EduOS.Core.Entities.Assessment;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AssessmentAdministration;

// All counts are SQL aggregates over a bounded set of publication IDs; no per-student materialization.
public sealed partial class AssessmentAdministrationService
{
    private sealed record PublicationEvidence(int TotalCount, int ActiveCount, int ValidCount, int UniqueCount,
        bool CoursesReconciled)
    {
        public bool IsReconciled => AssessmentPublicationSnapshotIntegrity.IsReconciled(
            TotalCount, ActiveCount, ValidCount, UniqueCount) && CoursesReconciled;
    }

    private async Task<Dictionary<long, PublicationEvidence>> GetPublicationEvidenceAsync(
        long[] publicationIds, long campusId, long academicYearId, CancellationToken ct)
    {
        if (publicationIds.Length == 0) return new Dictionary<long, PublicationEvidence>();
        var tenant = _user.TenantId;
        // Reconciliation must see soft-deleted historical snapshots as well as active rows.
        // IgnoreQueryFilters is limited to this tenant-scoped evidence count; callers
        // are authorized examination controllers with a validated campus scope.
        var all = await _summaries.GetQueryable().AsNoTracking().IgnoreQueryFilters()
            .Where(x => x.TenantId == tenant && publicationIds.Contains(x.ResultPublicationId))
            .GroupBy(x => x.ResultPublicationId)
            .Select(g => new { Id = g.Key, Total = g.Count(), Active = g.Count(x => !x.IsDeleted) })
            .ToListAsync(ct);

        var valid = from row in _summaries.GetQueryable().AsNoTracking()
            join pub in _publications.GetQueryable().AsNoTracking()
                on new { row.TenantId, Id = row.ResultPublicationId }
                equals new { pub.TenantId, pub.Id }
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on new { row.TenantId, Id = row.StudentEnrollmentId }
                equals new { enrollment.TenantId, enrollment.Id }
            join student in _students.GetQueryable().AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.StudentId }
                equals new { student.TenantId, student.Id }
            where row.TenantId == tenant && publicationIds.Contains(row.ResultPublicationId) &&
                !row.IsDeleted && !pub.IsDeleted && !enrollment.IsDeleted && !student.IsDeleted &&
                row.AssessmentId == pub.AssessmentId &&
                row.PublicationVersionNo == pub.VersionNo &&
                enrollment.AcademicBatchId == pub.AcademicBatchId &&
                enrollment.CampusId == campusId && enrollment.AcademicYearId == academicYearId &&
                row.TotalMarks >= 0m && row.ObtainedMarks >= 0m &&
                row.ObtainedMarks <= row.TotalMarks &&
                (!row.Percentage.HasValue || row.Percentage >= 0m && row.Percentage <= 100m) &&
                (!row.GPA.HasValue || row.GPA >= 0m) &&
                (!row.CGPA.HasValue || row.CGPA >= 0m)
            select new { row.ResultPublicationId, row.StudentEnrollmentId };

        var validCounts = await valid.GroupBy(x => x.ResultPublicationId)
            .Select(g => new { Id = g.Key, Count = g.Count() }).ToListAsync(ct);
        var uniqueCounts = await valid.Distinct().GroupBy(x => x.ResultPublicationId)
            .Select(g => new { Id = g.Key, Count = g.Count() }).ToListAsync(ct);
        var v = validCounts.ToDictionary(x => x.Id, x => x.Count);
        var u = uniqueCounts.ToDictionary(x => x.Id, x => x.Count);
        var courseProof = await ReconcileCourseSnapshotPublicationsAsync(publicationIds, ct);
        return all.ToDictionary(x => x.Id, x => new PublicationEvidence(
            x.Total, x.Active, v.GetValueOrDefault(x.Id), u.GetValueOrDefault(x.Id),
            courseProof.GetValueOrDefault(x.Id, true)));
    }
}
