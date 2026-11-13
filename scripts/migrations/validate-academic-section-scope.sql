-- Read-only validation after AcademicSections migrations.
-- Never infer or overwrite SectionId from BatchId; only a real teaching section
-- selected by the business workflow should be used for new records.
SET NOCOUNT ON;

SELECT N'StudentEnrollments' AS TableName, COUNT_BIG(*) AS MissingSection
FROM dbo.StudentEnrollments WHERE AcademicBatchId IS NOT NULL AND AcademicSectionId IS NULL
UNION ALL SELECT N'SubjectOfferings', COUNT_BIG(*) FROM dbo.SubjectOfferings
WHERE AcademicBatchId IS NOT NULL AND AcademicSectionId IS NULL
UNION ALL SELECT N'AttendanceSessions', COUNT_BIG(*) FROM dbo.AttendanceSessions
WHERE AcademicBatchId IS NOT NULL AND AcademicSectionId IS NULL
UNION ALL SELECT N'ResultPublications', COUNT_BIG(*) FROM dbo.ResultPublications
WHERE AcademicBatchId IS NOT NULL AND AcademicSectionId IS NULL
UNION ALL SELECT N'FeeStructures', COUNT_BIG(*) FROM dbo.FeeStructures
WHERE AcademicBatchId IS NOT NULL AND AcademicSectionId IS NULL;

-- Capacity is never silently corrected. A batch must cover current student
-- count and each of its independent section capacities.
SELECT s.TenantId, s.Id AS SectionId, s.AcademicBatchId, s.Capacity AS SectionCapacity,
       b.Capacity AS BatchCapacity
FROM dbo.AcademicSections AS s
JOIN dbo.AcademicBatches AS b ON b.TenantId = s.TenantId AND b.Id = s.AcademicBatchId
WHERE s.IsDeleted = 0 AND (b.IsDeleted = 1 OR s.Capacity > b.Capacity);

-- A non-null SectionId must belong to the same Tenant and Batch.
SELECT N'StudentEnrollments' AS TableName, e.TenantId, e.Id AS RecordId,
       e.AcademicBatchId, e.AcademicSectionId
FROM dbo.StudentEnrollments AS e
JOIN dbo.AcademicSections AS s ON s.TenantId = e.TenantId AND s.Id = e.AcademicSectionId
WHERE e.AcademicSectionId IS NOT NULL AND e.AcademicBatchId <> s.AcademicBatchId
UNION ALL SELECT N'SubjectOfferings', o.TenantId, o.Id, o.AcademicBatchId, o.AcademicSectionId
FROM dbo.SubjectOfferings AS o
JOIN dbo.AcademicSections AS s ON s.TenantId = o.TenantId AND s.Id = o.AcademicSectionId
WHERE o.AcademicSectionId IS NOT NULL AND o.AcademicBatchId <> s.AcademicBatchId
UNION ALL SELECT N'AttendanceSessions', a.TenantId, a.Id, a.AcademicBatchId, a.AcademicSectionId
FROM dbo.AttendanceSessions AS a
JOIN dbo.AcademicSections AS s ON s.TenantId = a.TenantId AND s.Id = a.AcademicSectionId
WHERE a.AcademicSectionId IS NOT NULL AND a.AcademicBatchId <> s.AcademicBatchId
UNION ALL SELECT N'ResultPublications', r.TenantId, r.Id, r.AcademicBatchId, r.AcademicSectionId
FROM dbo.ResultPublications AS r
JOIN dbo.AcademicSections AS s ON s.TenantId = r.TenantId AND s.Id = r.AcademicSectionId
WHERE r.AcademicSectionId IS NOT NULL AND r.AcademicBatchId <> s.AcademicBatchId
UNION ALL SELECT N'FeeStructures', f.TenantId, f.Id, f.AcademicBatchId, f.AcademicSectionId
FROM dbo.FeeStructures AS f
JOIN dbo.AcademicSections AS s ON s.TenantId = f.TenantId AND s.Id = f.AcademicSectionId
WHERE f.AcademicSectionId IS NOT NULL AND f.AcademicBatchId <> s.AcademicBatchId;
