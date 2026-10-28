# Employee identity and payroll privacy — 2026-10-28

New HR user-account links require both an active tenant membership and a verified
global `ApplicationUser.PersonId` equal to `Employee.PersonId`. Supplying only a
`UserId` without a matching `PersonId` is rejected.

Employee self-service (profile, leave, attendance) and user payslips now fail
closed for mismatched, inactive, deleted or unlinked platform users. No existing
global Person, employee or user identifier is overwritten or auto-merged.

**Deployment preflight**: before V2 activation, audit older employees with
`UserId IS NOT NULL` where the linked application's PersonId is null or differs.
Correct through verified owner-consented identity linking and audited
administration; do NOT migrate by matching name/email/phone or automatically
change the employee's PersonId.

This is a privacy/security behavior change, not a schema migration. Tenant
administrators can still access authorized institution payroll history.
