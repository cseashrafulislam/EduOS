-- Read-only validation for class-owned AcademicSections.
-- Run after the latest migration; this script never updates or deletes data.
SET NOCOUNT ON;

SELECT N'StudentEnrollments' AS TableName, COUNT_BIG(*) AS MissingSection
FROM dbo.StudentEnrollments WHERE AcademicSectionId IS NULL
UNION ALL SELECT N'SubjectOfferings', COUNT_BIG(*) FROM dbo.SubjectOfferings
WHERE AcademicSectionId IS NULL
UNION ALL SELECT N'AttendanceSessions', COUNT_BIG(*) FROM dbo.AttendanceSessions
WHERE AcademicSectionId IS NULL
UNION ALL SELECT N'ResultPublications', COUNT_BIG(*) FROM dbo.ResultPublications
WHERE AcademicBatchId IS NOT NULL AND AcademicSectionId IS NULL
UNION ALL SELECT N'FeeStructures', COUNT_BIG(*) FROM dbo.FeeStructures
WHERE AcademicBatchId IS NOT NULL AND AcademicSectionId IS NULL;

-- Every Section requires an existing campus and class (regardless of Batch).
SELECT s.TenantId, s.Id AS SectionId, s.CampusId, s.AcademicLevelId
FROM dbo.AcademicSections AS s
LEFT JOIN dbo.Campuses AS c
    ON c.TenantId = s.TenantId AND c.Id = s.CampusId
LEFT JOIN dbo.AcademicLevels AS l
    ON l.TenantId = s.TenantId AND l.Id = s.AcademicLevelId
WHERE c.Id IS NULL OR l.Id IS NULL;

-- Existing batch-based records must use a Section belonging to the same
-- campus and class as the selected cohort. A Section itself owns no BatchId.
SELECT N'StudentEnrollments' AS TableName, e.TenantId,
       e.Id AS RecordId, e.AcademicBatchId, e.AcademicSectionId
FROM dbo.StudentEnrollments AS e
JOIN dbo.AcademicSections AS s
    ON s.TenantId = e.TenantId AND s.Id = e.AcademicSectionId
JOIN dbo.AcademicBatches AS b
    ON b.TenantId = e.TenantId AND b.Id = e.AcademicBatchId
WHERE b.CampusId <> s.CampusId OR b.AcademicLevelId <> s.AcademicLevelId
UNION ALL
SELECT N'SubjectOfferings', o.TenantId, o.Id, o.AcademicBatchId, o.AcademicSectionId
FROM dbo.SubjectOfferings AS o
JOIN dbo.AcademicSections AS s
    ON s.TenantId = o.TenantId AND s.Id = o.AcademicSectionId
JOIN dbo.AcademicBatches AS b
    ON b.TenantId = o.TenantId AND b.Id = o.AcademicBatchId
WHERE b.CampusId <> s.CampusId OR b.AcademicLevelId <> s.AcademicLevelId
UNION ALL
SELECT N'AttendanceSessions', a.TenantId, a.Id, a.AcademicBatchId, a.AcademicSectionId
FROM dbo.AttendanceSessions AS a
JOIN dbo.AcademicSections AS s
    ON s.TenantId = a.TenantId AND s.Id = a.AcademicSectionId
JOIN dbo.AcademicBatches AS b
    ON b.TenantId = a.TenantId AND b.Id = a.AcademicBatchId
WHERE b.CampusId <> s.CampusId OR b.AcademicLevelId <> s.AcademicLevelId
UNION ALL
SELECT N'ResultPublications', r.TenantId, r.Id, r.AcademicBatchId, r.AcademicSectionId
FROM dbo.ResultPublications AS r
JOIN dbo.AcademicSections AS s
    ON s.TenantId = r.TenantId AND s.Id = r.AcademicSectionId
JOIN dbo.AcademicBatches AS b
    ON b.TenantId = r.TenantId AND b.Id = r.AcademicBatchId
WHERE b.CampusId <> s.CampusId OR b.AcademicLevelId <> s.AcademicLevelId
UNION ALL
SELECT N'FeeStructures', f.TenantId, f.Id, f.AcademicBatchId, f.AcademicSectionId
FROM dbo.FeeStructures AS f
JOIN dbo.AcademicSections AS s
    ON s.TenantId = f.TenantId AND s.Id = f.AcademicSectionId
JOIN dbo.AcademicBatches AS b
    ON b.TenantId = f.TenantId AND b.Id = f.AcademicBatchId
WHERE b.CampusId <> s.CampusId OR b.AcademicLevelId <> s.AcademicLevelId;
