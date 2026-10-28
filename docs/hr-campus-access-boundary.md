# HR campus authorization and salary privacy — 2026-10-28

The HR administration service now limits Principal/HR employee directory, detail,
edit, status change, leave register/review and assignment-history reads to employees
with an effective campus assignment in a campus with an active UserCampusAccess grant
for the acting user. Unassigned employees are TenantAdmin-only until assigned.

Creating new employees is TenantAdmin-only because the create contract has no campus
ownership field. Do not widen this merely by showing a UI button; a future workflow
must use validated campus-scoped creation, user membership and assignment in one
transaction. Existing tenant-admin paths remain compatible.

No table or existing employee history is altered by this authorization change.
Before deployment, test principals/HR with multiple campuses, absent/expired grants,
future and ended assignments, direct employee UUID probing, leave ID probing,
tenant switching and payroll salary visibility. Permission changes may make
previously visible unassigned employees disappear for campus-scoped roles; an
administrator must assign them properly rather than bypassing checks.
