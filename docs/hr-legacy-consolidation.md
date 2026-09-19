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

## Code dependency inventory

The current service tree shows the active operational implementations under `Services/Portals/EmployeeSelfServiceService.cs` and `Services/HR/HrPayrollService.cs`. Employee self-service is already on the canonical employee, attendance, leave and payroll path. `HrPayrollService` must remain part of every legacy audit because its HR-facing name does not by itself prove that it depends on legacy `HR.*` entities.

The persistence layer still contains legacy DbSets and therefore remains an intentional compatibility boundary. Before removing any legacy entity, audit all of these categories against the branch head:

1. `EduOS.Persistence` DbSets, model configuration, migrations and snapshots.
2. `EduOS.Service` constructor/repository generic types and direct `HR.*` entity references.
3. `EduOS.App` controllers, request/response models and dependency-injection registrations.
4. `EduOS.Tests` seed helpers and assertions that may still instantiate legacy entities.
5. Database foreign keys and row counts in deployed databases; source-code absence alone is not proof that a table can be dropped.

A code reference is classified as **operational** when it participates in a live controller/service workflow, **migration-only** when it exists only to preserve/transform deployed data, and **compatibility-only** when it keeps an existing table/model readable while no new writes are introduced. Only migration-only or compatibility-only references are acceptable for the retirement candidates listed above.

## Safe retirement sequence

1. Inventory production rows in every legacy HR table per tenant.
2. Detect duplicate employee identities using tenant + employee code, then tenant + normalized email/phone as review-only fallbacks. Never merge tenants.
3. Produce an explicit mapping from each legacy employee ID to a canonical employee ID. Ambiguous matches require manual resolution.
4. Migrate attendance, leave, salary/payroll and any remaining foreign keys using that mapping. Preserve source IDs in migration/audit output.
5. Reconcile row counts and financial totals per tenant before switching reads.
6. Add migration-time constraints/indexes required by the canonical model and validate rollback on a production-like backup.
7. Only after code search and database-FK inspection show zero live dependencies, remove legacy DbSets/entities and generate the destructive schema migration.

## Required reconciliation output

Before destructive retirement, produce a per-tenant report containing legacy employee count, mapped/unmapped/ambiguous employee counts, attendance count, leave count, payroll row count, payroll gross/net totals, and every remaining FK that targets a legacy table. The migration is blocked when any ambiguous employee, unexplained row-count difference, monetary mismatch, or live operational FK remains.

## Guardrails

- Do not copy or merge HR data across tenants.
- Do not infer employee identity from name alone.
- Do not delete legacy tables in the same migration that first copies their data.
- Payroll monetary totals must reconcile before and after migration.
- Attendance and leave history dates/statuses must remain unchanged unless a documented normalization rule is applied.
- Every destructive migration must have a tested backup/rollback procedure.
- Do not create a new feature on a retirement-candidate `HR.*` entity even if doing so appears cheaper than extending the canonical model.

This document intentionally separates the dependency audit from destructive schema work. The legacy DbSets remain until a data-preserving migration can be proven safe.