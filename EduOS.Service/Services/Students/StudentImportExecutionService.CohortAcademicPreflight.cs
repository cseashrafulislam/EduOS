using EduOS.Core.DTOs.Students;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Students;

public sealed partial class StudentImportExecutionService
{
    /// <summary>
    /// Cohort-wide read-only required-subject offering preflight. Queries are
    /// tenant-scoped, projected and bounded; no identity or guardian PII leaves
    /// the service. Each 100-row execution window rechecks under batch locks.
    /// </summary>
    private async Task CheckCohortRequiredOfferingsAsync(
        StudentImportCohortPreflightDto report,
        IReadOnlyList<(int FileNumber, ImportRow Row)> cohortRows,
        long tenant, CancellationToken token)
    {
        var batchIds = cohortRows.Select(x => x.Row.BatchId).Where(x => x > 0)
            .Distinct().ToArray();
        if (batchIds.Length == 0) return;
        var batches = new List<AcademicBatch>();
        foreach (var chunk in batchIds.Chunk(200))
            batches.AddRange(await _batches.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsActive &&
                    chunk.Contains(x.Id)).ToListAsync(token));
        if (batches.Count == 0) return;

        var rowsByBatch = cohortRows.ToLookup(x => x.Row.BatchId);

        void Flag(long batchId, string field, string message)
        {
            foreach (var (fileNumber, row) in rowsByBatch[batchId])
            {
                token.ThrowIfCancellationRequested();
                report.Issues.Add(new StudentImportCohortConflictDto
                {
                    FileNumber = fileNumber, RowNumber = row.RowNumber,
                    Field = field, Message = message
                });
            }
        }
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var programIds = batches.Select(x => x.AcademicProgramId).Distinct().ToArray();
        var trackIds = batches.Select(x => x.AcademicTrackId).Distinct().ToArray();
        var mediumIds = batches.Select(x => x.MediumId).Distinct().ToArray();
        var curricula = new List<AcademicCurriculum>();
        foreach (var chunk in programIds.Chunk(200))
            curricula.AddRange(await _curricula.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsActive &&
                    x.IsCurrent && chunk.Contains(x.AcademicProgramId) &&
                    trackIds.Contains(x.AcademicTrackId) && mediumIds.Contains(x.MediumId) &&
                    x.EffectiveFrom <= today && (!x.EffectiveTo.HasValue || x.EffectiveTo >= today))
                .ToListAsync(token));

        var curriculumByBatch = new Dictionary<long, long>();
        foreach (var batch in batches)
        {
            var matching = curricula.Where(x =>
                x.AcademicProgramId == batch.AcademicProgramId &&
                x.AcademicTrackId == batch.AcademicTrackId &&
                x.MediumId == batch.MediumId).Take(2).ToArray();
            if (matching.Length != 1)
                Flag(batch.Id, "Curriculum",
                    "Batch must have exactly one current effective curriculum.");
            else
                curriculumByBatch.Add(batch.Id, matching[0].Id);
        }
        if (curriculumByBatch.Count == 0) return;

        var curriculumIds = curriculumByBatch.Values.Distinct().ToArray();
        var levelIds = batches.Select(x => x.AcademicLevelId).Distinct().ToArray();
        var subjects = new List<CurriculumSubject>();
        foreach (var chunk in curriculumIds.Chunk(200))
            subjects.AddRange(await _curriculumSubjects.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsActive &&
                    !x.IsOptional && chunk.Contains(x.AcademicCurriculumId) &&
                    levelIds.Contains(x.AcademicLevelId)).ToListAsync(token));
        if (subjects.Count == 0) return;

        var subjectIds = subjects.Select(x => x.Id).ToArray();
        var offerings = new List<SubjectOffering>();
        foreach (var chunk in batchIds.Chunk(200))
            offerings.AddRange(await _offerings.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsActive &&
                    chunk.Contains(x.AcademicBatchId) &&
                    subjectIds.Contains(x.CurriculumSubjectId)).ToListAsync(token));
        var offeringIds = offerings.Select(x => x.Id).ToArray();
        var reservations = new Dictionary<long, int>();
        foreach (var chunk in offeringIds.Chunk(200))
        {
            var reserved = await _registrations.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && !x.IsDeleted &&
                    chunk.Contains(x.SubjectOfferingId) &&
                    x.State != SubjectRegistrationState.Rejected &&
                    x.State != SubjectRegistrationState.Dropped)
                .GroupBy(x => x.SubjectOfferingId)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToListAsync(token);
            foreach (var count in reserved) reservations.Add(count.Id, count.Count);
        }

        var batchCampus = batches.ToDictionary(x => x.Id, x => x.CampusId);
        var incomingByBatch = new Dictionary<long, int>();
        foreach (var (_, row) in cohortRows)
            if (batchCampus.TryGetValue(row.BatchId, out var campus) &&
                long.TryParse(row.CampusId, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed == campus)
                incomingByBatch[row.BatchId] = incomingByBatch.GetValueOrDefault(row.BatchId) + 1;

        var requiredCountByBatch = new Dictionary<long, int>();
        foreach (var batch in batches)
        {
            token.ThrowIfCancellationRequested();
            if (!curriculumByBatch.TryGetValue(batch.Id, out var curriculumId)) continue;
            var required = subjects.Where(x => x.AcademicCurriculumId == curriculumId &&
                x.AcademicLevelId == batch.AcademicLevelId).ToArray();
            requiredCountByBatch[batch.Id] = required.Length;
            if (required.Length == 0) continue;

            var invalidOffering = false;
            var capacityInsufficient = false;
            // Include all valid-campus source rows across all files; a one-file
            // preview can miss exhaustion caused by subsequent files.
            var incoming = incomingByBatch.GetValueOrDefault(batch.Id);
            foreach (var subject in required)
            {
                var matching = offerings.Where(x => x.AcademicBatchId == batch.Id &&
                    x.AcademicYearId == batch.AcademicYearId &&
                    x.CurriculumSubjectId == subject.Id).Take(2).ToArray();
                if (matching.Length != 1)
                {
                    invalidOffering = true;
                    continue;
                }
                var offering = matching[0];
                if (offering.Capacity <= 0 ||
                    reservations.GetValueOrDefault(offering.Id) + incoming > offering.Capacity)
                    capacityInsufficient = true;
            }
            if (invalidOffering)
                Flag(batch.Id, "RequiredOffering",
                    "Each mandatory curriculum subject needs exactly one active batch offering.");
            if (capacityInsufficient)
                Flag(batch.Id, "RequiredOfferingCapacity",
                    "Combined source files exceed remaining mandatory subject offering seats.");
        }

        // Each file is executed in 100-row windows. Catch an otherwise valid
        // cohort whose mandatory registration volume exceeds that atomic limit.
        foreach (var window in cohortRows.GroupBy(x =>
            (x.FileNumber, Window: Math.Max(0, (x.Row.RowNumber - 2) / 100))))
        {
            token.ThrowIfCancellationRequested();
            if (window.Sum(x => requiredCountByBatch.GetValueOrDefault(x.Row.BatchId)) <= 2000)
                continue;
            foreach (var (fileNumber, row) in window)
                report.Issues.Add(new StudentImportCohortConflictDto
                {
                    FileNumber = fileNumber, RowNumber = row.RowNumber,
                    Field = "RequiredRegistrationLimit",
                    Message = "A 100-row window exceeds the 2,000 mandatory registration limit."
                });
        }
    }
}
