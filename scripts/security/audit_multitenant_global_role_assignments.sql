-- SQL Server 2019+. Read-only pre-deployment audit.
-- Default role grants are global in legacy data. They must NOT be copied
-- to every institution when a user is a member of more than one tenant.
-- MembershipStatus.Active = 2; TenantState.Active = 2; PendingVerification = 1;
-- OnboardingStage.Completed = 11 (verified in EduOS.Core/Enums/FinalDomainEnums.cs).
WITH ActiveMemberships AS
(
    SELECT m.UserId, m.TenantId, m.IsOwner
    FROM dbo.TenantMemberships m
    INNER JOIN dbo.Tenants t ON t.Id = m.TenantId
    WHERE m.IsDeleted = 0 AND m.Status = 2 AND t.IsDeleted = 0
      AND (t.State = 2 OR (t.State = 1 AND t.OnboardingStage <> 11))
),
MembershipTotals AS
(
    SELECT UserId, COUNT(DISTINCT TenantId) AS ActiveInstitutionCount
    FROM ActiveMemberships
    GROUP BY UserId
)
SELECT u.Id AS UserId, u.Email, totals.ActiveInstitutionCount,
       r.Id AS GlobalRoleId, r.Name AS GlobalRoleName,
       m.TenantId, t.Name AS InstitutionName, m.IsOwner
FROM MembershipTotals totals
INNER JOIN dbo.AspNetUsers u ON u.Id = totals.UserId
INNER JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
INNER JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
INNER JOIN ActiveMemberships m ON m.UserId = u.Id
INNER JOIN dbo.Tenants t ON t.Id = m.TenantId
WHERE totals.ActiveInstitutionCount > 1 AND r.TenantId IS NULL
  AND r.IsSystemRole = 0 AND r.IsDeleted = 0 AND r.IsActive = 1
ORDER BY u.Id, t.Name, r.Name;
-- Before rollout: review every result and assign only explicitly approved,
-- correctly tenant-scoped roles. Never infer multi-institution privileges
-- solely from a global AspNetUserRoles row.
