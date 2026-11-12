-- Execute after 20261113010000_AcademicSectionFoundation.
-- Safe to rerun. Each batch is a committed transaction; no old AcademicBatchId is changed.
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Table sysname, @Rows int, @Total bigint;
DECLARE @Tables TABLE (TableName sysname NOT NULL PRIMARY KEY);
INSERT @Tables VALUES
(N'StudentEnrollments'),(N'SubjectOfferings'),(N'AttendanceSessions'),
(N'ResultPublications'),(N'FeeStructures');
WHILE EXISTS (SELECT 1 FROM @Tables)
BEGIN
    SELECT TOP (1) @Table = TableName FROM @Tables ORDER BY TableName;
    SET @Total = 0;
    WHILE 1 = 1
    BEGIN
        SET @Rows = 0;
        DECLARE @Sql nvarchar(max) = N'
BEGIN TRANSACTION;
UPDATE TOP (500) t
   SET AcademicSectionId = s.Id
  FROM dbo.' + QUOTENAME(@Table) + N' AS t
  JOIN dbo.AcademicSections AS s
    ON s.TenantId = t.TenantId
   AND s.AcademicBatchId = t.AcademicBatchId
   AND s.IsLegacyDefault = 1
 WHERE t.AcademicSectionId IS NULL;
SET @Updated = @@ROWCOUNT;
COMMIT TRANSACTION;';
        EXEC sys.sp_executesql @Sql, N'@Updated int OUTPUT', @Updated = @Rows OUTPUT;
        SET @Total += @Rows;
        IF @Rows = 0 BREAK;
    END;
    PRINT CONCAT(@Table, N': backfilled ', @Total, N' rows');
    DELETE FROM @Tables WHERE TableName = @Table;
END;
-- Verify before switching readers/writers. Existing legacy rows without batch scope
-- are excluded; don't guess a SectionId when AcademicBatchId is NULL.
SELECT N'StudentEnrollments' AS TableName, COUNT_BIG(*) AS Unmapped
FROM dbo.StudentEnrollments WHERE AcademicBatchId IS NOT NULL AND AcademicSectionId IS NULL
UNION ALL SELECT N'SubjectOfferings', COUNT_BIG(*) FROM dbo.SubjectOfferings
WHERE AcademicBatchId IS NOT NULL AND AcademicSectionId IS NULL
UNION ALL SELECT N'AttendanceSessions', COUNT_BIG(*) FROM dbo.AttendanceSessions
WHERE AcademicBatchId IS NOT NULL AND AcademicSectionId IS NULL
UNION ALL SELECT N'ResultPublications', COUNT_BIG(*) FROM dbo.ResultPublications
WHERE AcademicBatchId IS NOT NULL AND AcademicSectionId IS NULL
UNION ALL SELECT N'FeeStructures', COUNT_BIG(*) FROM dbo.FeeStructures
WHERE AcademicBatchId IS NOT NULL AND AcademicSectionId IS NULL;
