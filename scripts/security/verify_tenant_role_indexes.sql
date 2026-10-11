-- Read-only preflight for SQL Server. Run before deploying tenant role editing.
-- This query does not change schema/data; review with DBA and compare to EF model.
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.AspNetRoles', N'U') IS NULL
    THROW 50000, 'dbo.AspNetRoles is missing; deploy baseline schema first.', 1;
IF COL_LENGTH('dbo.AspNetRoles','TenantId') IS NULL
    THROW 50001, 'AspNetRoles.TenantId is missing; apply reviewed additive migration first.', 1;
IF COL_LENGTH('dbo.AspNetRoles','NormalizedName') IS NULL
    THROW 50002, 'AspNetRoles.NormalizedName is missing.', 1;

SELECT i.name AS IndexName, i.is_unique AS IsUnique, i.filter_definition AS FilterDefinition,
       STUFF((SELECT ', ' + COL_NAME(ic.object_id,ic.column_id)
              FROM sys.index_columns ic
              WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
                AND ic.is_included_column = 0
              ORDER BY ic.key_ordinal FOR XML PATH(''), TYPE).value('.','nvarchar(max)'),1,2,'') AS KeyColumns
FROM sys.indexes i
WHERE i.object_id = OBJECT_ID(N'dbo.AspNetRoles') AND i.is_hypothetical = 0
ORDER BY i.name;

-- This result MUST be empty before creating the per-tenant uniqueness index.
SELECT TenantId, NormalizedName, COUNT_BIG(*) AS DuplicateCount
FROM dbo.AspNetRoles
WHERE NormalizedName IS NOT NULL
GROUP BY TenantId, NormalizedName
HAVING COUNT_BIG(*) > 1;

-- RoleNameIndex on NormalizedName alone MUST NOT remain unique,
-- otherwise two different institutions cannot both use 'Teacher'.
-- Expected composite UNIQUE index:
--   (TenantId, NormalizedName) WHERE NormalizedName IS NOT NULL
-- See EduOSDbContext.ConfigureIdentity for the EF model of record.
