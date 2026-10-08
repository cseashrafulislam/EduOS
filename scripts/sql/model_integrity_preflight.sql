-- EduOS model ownership preflight - READ ONLY.
-- Run manually against a backed-up, isolated SQL Server copy after the canonical schema exists.
-- Never automatically change historical accounting, domain, or payment records.

SET NOCOUNT ON;

PRINT '1. Legacy account parent values that conflict with canonical ParentAccountId';
IF OBJECT_ID(N'dbo.Accounts', N'U') IS NULL
    PRINT 'SKIP: dbo.Accounts does not exist.';
ELSE IF COL_LENGTH(N'dbo.Accounts', N'ParentId') IS NULL
    PRINT 'OK: legacy Accounts.ParentId column is absent.';
ELSE
    EXEC sys.sp_executesql N'
        SELECT a.TenantId, a.Id AS AccountId, a.Code, a.ParentAccountId, a.ParentId AS LegacyParentId
        FROM dbo.Accounts AS a
        WHERE a.ParentId IS NOT NULL
          AND (a.ParentAccountId IS NULL OR a.ParentAccountId <> a.ParentId)
        ORDER BY a.TenantId, a.Id;';

PRINT '2. Tenant legacy custom domains without a matching canonical TenantDomain record';
IF OBJECT_ID(N'dbo.Tenants', N'U') IS NULL OR OBJECT_ID(N'dbo.TenantDomains', N'U') IS NULL
    PRINT 'SKIP: dbo.Tenants or dbo.TenantDomains does not exist.';
ELSE
BEGIN
    SELECT t.Id AS TenantId, t.Code, t.CustomDomain AS LegacyCustomDomain
    FROM dbo.Tenants AS t
    WHERE t.CustomDomain IS NOT NULL
      AND LTRIM(RTRIM(t.CustomDomain)) <> N''
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.TenantDomains AS d
          WHERE d.TenantId = t.Id AND d.IsDeleted = 0
            AND LOWER(LTRIM(RTRIM(d.HostName))) = LOWER(LTRIM(RTRIM(t.CustomDomain)))
      )
    ORDER BY t.Id;

    SELECT d.TenantId, COUNT_BIG(*) AS ActivePrimaryDomainCount
    FROM dbo.TenantDomains AS d
    WHERE d.IsDeleted = 0 AND d.IsPrimary = 1 AND d.IsActive = 1
    GROUP BY d.TenantId
    HAVING COUNT_BIG(*) > 1;
END;

PRINT '3. Student invoice arithmetic violations (irrespective of payment allocation or refund status)';
IF OBJECT_ID(N'dbo.StudentInvoices', N'U') IS NULL
    PRINT 'SKIP: dbo.StudentInvoices does not exist.';
ELSE
    SELECT i.TenantId, i.Id AS InvoiceId, i.InvoiceNumber, i.TotalAmount, i.PaidAmount, i.DueAmount
    FROM dbo.StudentInvoices AS i
    WHERE i.PaidAmount < 0 OR i.DueAmount < 0 OR i.PaidAmount > i.TotalAmount
       OR i.DueAmount <> i.TotalAmount - i.PaidAmount
    ORDER BY i.TenantId, i.Id;

PRINT '4. Successful payment allocations versus invoice cached paid amount (REVIEW ONLY; refunds/credits require reconciliation)';
IF OBJECT_ID(N'dbo.StudentInvoices', N'U') IS NULL
    OR OBJECT_ID(N'dbo.PaymentAllocations', N'U') IS NULL
    OR OBJECT_ID(N'dbo.StudentPayments', N'U') IS NULL
    PRINT 'SKIP: invoice, payment, or allocation tables do not exist.';
ELSE
    SELECT i.TenantId, i.Id AS InvoiceId, i.InvoiceNumber, i.PaidAmount,
           COALESCE(SUM(CASE WHEN p.State = 3 THEN a.Amount ELSE 0 END), 0) AS GrossSuccessfulAllocated,
           COUNT_BIG(a.Id) AS AllocationRowCount
    FROM dbo.StudentInvoices AS i
    LEFT JOIN dbo.PaymentAllocations AS a
        ON a.TenantId = i.TenantId AND a.StudentInvoiceId = i.Id AND a.IsDeleted = 0
    LEFT JOIN dbo.StudentPayments AS p
        ON p.Id = a.StudentPaymentId AND p.TenantId = i.TenantId AND p.IsDeleted = 0
    GROUP BY i.TenantId, i.Id, i.InvoiceNumber, i.PaidAmount
    HAVING i.PaidAmount <> COALESCE(SUM(CASE WHEN p.State = 3 THEN a.Amount ELSE 0 END), 0)
    ORDER BY i.TenantId, i.Id;

-- Above differences are NOT automatically errors: successful payments may have refunds,
-- reversals, disputes, or other amendments. Reconcile each case before changes.
