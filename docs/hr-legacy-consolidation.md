# HR legacy consolidation boundary

## Decision

The canonical operational employee path is `EduOS.Core.Entities.Employees.Employee` together with `EmployeeAttendance`, `Attendance.LeaveApplication`, `Payroll.SalaryStructure`, and `Payroll.Payroll`.

The types under `EduOS.Core.Entities.HR` are legacy compatibility entities. New services, controllers, repositories, migrations, and self-service features must not add dependencies on `HREmployee`, `HRAttendanceLog`, `HRLeaveApplication`, `HRLeaveType`, `HRSalaryStructure`, or `HRPayroll`.

`HRDepartment`, `HRDesignation`, and `HRShift` must not be removed merely because they share the legacy namespace; they require a separate reference/data audit before consolidation.

## Current evidence

- `Employee` has the current user linkage (`UserId`), stable `PublicId`, tenant ownership, active state, teacher flag, canonical academic `Designation`/`Department` links, and optimistic concurrency (`RowVersion`).
- Employee self-service resolves the signed-in user through the canonical `Employee` model and reads canonical employee attendance and leave records.
- Payroll services use the canonical employee/payroll path.
- `EduOSDbContext` still exposes `HREmployees` and `HRAttendanceLogs`; these are retained temporarily so an existing database is not silently changed or its legacy tables dropped without a data migration.

## Safe retirement sequence

1. Inventory production rows in every legacy HR table per tenant.
2. Detect duplicate employee identities using tenant + employee code, then tenant + normalized email/phone as review-only fallbacks. Never merge tenants.
3. Produce an explicit mapping from each legacy employee ID to a canonical employee ID. Ambiguous matches require manual resolution.
4. Migrate attendance, leave, salary/payroll and any remaining foreign keys using that mapping. Preserve source IDs in migration/audit output.
5. Reconcile row counts and financial totals per tenant before switching reads.
6. Add migration-time constraints/indexes required by the canonical model and validate rollback on a production-like backup.
7. Only after code search and database-FK inspection show zero live dependencies, remove legacy DbSets/entities and generate the destructive schema migration.

## Guardrails

- Do not copy or merge HR data across tenants.
- Do not infer employee identity from name alone.
- Do not delete legacy tables in the same migration that first copies their data.
- Payroll monetary totals must reconcile before and after migration.
- Attendance and leave history dates/statuses must remain unchanged unless a documented normalization rule is applied.
- Every destructive migration must have a tested backup/rollback procedure.

This document intentionally separates the dependency audit from destructive schema work. The legacy DbSets remain until a data-preserving migration can be proven safe.