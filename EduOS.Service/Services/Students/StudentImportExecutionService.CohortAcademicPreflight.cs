using EduOS.Core.DTOs.Students;
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
            .Distinct().ToArray(); // bounded to 200 by the caller
        if (batchIds.Length == 0) return;
        var batches = await _batches.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsActive &&
                batchIds.Contains(x.Id))
            .Select(x => new { x.Id, x.CampusId, x.AcademicYearId,
                x.AcademicProgramId, x.AcademicTrackId, x.MediumId, x.AcademicLevelId })
            .ToListAsync(token);
        if (batches.Count == 0) return;

        void Flag(long batchId, string field, string message)
        {
            foreach (var (fileNumber, row) in cohortRows)
            {
                token.ThrowIfCancellationRequested();
                if (row.BatchId != batchId) continue;
                report.Issues.Add(new StudentImportCohortConflictDto
                {
                    FileNumber = fileNumber, RowNumber = row.RowNumber,
                    Field = field, Message = message
                });
            }
        }
        void FlagAll(string field, string message)
        {
            foreach (var batch in batches) Flag(batch.Id, field, message);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var programIds = batches.Select(x => x.AcademicProgramId).Distinct().ToArray();
        var trackIds = batches.Select(x => x.AcademicTrackId).Distinct().ToArray();
        var mediumIds = batches.Select(x => x.MediumId).Distinct().ToArray();
        var curricula = await _curricula.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsActive &&
                x.IsCurrent && programIds.Contains(x.AcademicProgramId) &&
                trackIds.Contains(x.AcademicTrackId) && mediumIds.Contains(x.MediumId) &&
                x.EffectiveFrom <= today && (!x.EffectiveTo.HasValue || x.EffectiveTo >= today))
            .Select(x => new { x.Id, x.AcademicProgramId, x.AcademicTrackId, x.MediumId })
            .Take(2001).ToListAsync(token);
        if (curricula.Count > 2000)
        {
            FlagAll("Curriculum", "Current curriculum lookup exceeds the preflight limit.");
            return;
        }

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
        var subjects = await _curriculumSubjects.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsActive &&
                !x.IsOptional && curriculumIds.Contains(x.AcademicCurriculumId) &&
                levelIds.Contains(x.AcademicLevelId))
            .Select(x => new { x.Id, x.AcademicCurriculumId, x.AcademicLevelId })
            .Take(2001).ToListAsync(token);
        if (subjects.Count > 2000)
        {
            FlagAll("RequiredOffering", "Required curriculum subject count exceeds the preflight limit.");
            return;
        }
        if (subjects.Count == 0) return;

        var subjectIds = subjects.Select(x => x.Id).ToArray();
        var offerings = await _offerings.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsActive &&
                batchIds.Contains(x.AcademicBatchId) &&
                subjectIds.Contains(x.CurriculumSubjectId))
            .Select(x => new { x.Id, x.AcademicBatchId, x.AcademicYearId,
                x.CurriculumSubjectId, x.Capacity })
            .Take(4001).ToListAsync(token);
        if (offerings.Count > 4000)
        {
            FlagAll("RequiredOffering", "Required offering count exceeds the preflight limit.");
            return;
        }
        var offeringIds = offerings.Select(x => x.Id).ToArray();
        var reservations = await _registrations.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted &&
                offeringIds.Contains(x.SubjectOfferingId) &&
                x.State != SubjectRegistrationState.Rejected &&
                x.State != SubjectRegistrationState.Dropped)
            .GroupBy(x => x.SubjectOfferingId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, token);

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
            var incoming = cohortRows.Count(x => x.Row.BatchId == batch.Id &&
                long.TryParse(x.Row.CampusId, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var campus) &&
                campus == batch.CampusId);
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
