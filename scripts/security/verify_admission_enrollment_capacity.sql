-- EduOS admission/enrollment capacity preflight (SQL Server, read-only).
-- Do not deploy a capacity-rule change without reviewing existing active batches.
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.AcademicBatches', N'U') IS NULL
    THROW 50000, 'dbo.AcademicBatches is missing; apply baseline migrations first.', 1;
IF OBJECT_ID(N'dbo.StudentEnrollments', N'U') IS NULL
    THROW 50001, 'dbo.StudentEnrollments is missing; apply baseline migrations first.', 1;

-- Convention: Capacity <= 0 is CLOSED for all admission/enrollment paths.
-- The result below identifies historical rows for deliberate correction by tenant operators.
SELECT b.TenantId, b.Id AS AcademicBatchId, b.Code, b.Name, b.Capacity,
       b.IsActive, b.IsDeleted
FROM dbo.AcademicBatches b
WHERE b.IsActive = 1 AND b.IsDeleted = 0 AND b.Capacity <= 0
ORDER BY b.TenantId, b.Id;

-- Capacity may have been oversubscribed before the allocation lock was introduced.
-- EnrollmentState.Active = 1 in the canonical enum.
SELECT b.TenantId, b.Id AS AcademicBatchId, b.Code, b.Capacity,
       COUNT_BIG(e.Id) AS ActiveCurrentEnrollments
FROM dbo.AcademicBatches b
JOIN dbo.StudentEnrollments e
  ON e.TenantId = b.TenantId AND e.AcademicBatchId = b.Id
 AND e.IsCurrent = 1 AND e.IsDeleted = 0 AND e.State = 1
WHERE b.IsDeleted = 0
GROUP BY b.TenantId, b.Id, b.Code, b.Capacity
HAVING COUNT_BIG(e.Id) > b.Capacity
ORDER BY b.TenantId, b.Id;

-- This query deliberately does not mutate historical capacity/enrollment.
-- Investigate any results by tenant, reconcile decisions and obtain business approval
-- before correcting capacity or closing enrollment to preserve reporting history.
