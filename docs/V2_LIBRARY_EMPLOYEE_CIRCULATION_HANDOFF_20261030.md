# EduOS V2 Library — employee borrowing completion checkpoint

**Branch:** `Ashraful` · **Implementation date:** 2026-10-30 · **Status:** engineering implementation, not institution production/UAT approval.

## Delivered behavior

- The existing physical-copy `BookIssues` ledger now records **exactly one** borrower: a student **or** an employee. Prior student loans retain their existing IDs and foreign keys. No second loan ledger or editable duplicate balance exists.
- An authorized TenantAdmin, Principal or Librarian searches active campus-assigned employees (paged by server), selects an available campus-eligible copy and issues it with a unique client request ID. Replays of identical requests are idempotent; changed terms or already-issued copies fail. Principal/Librarian branch grants are checked on write and replay.
- The student's FIFO reservation queue retains priority. Direct employee issues cannot jump an eligible pending student reservation. Employee identity, active employment, effective-dated campus assignment and branch eligibility are revalidated during the serializable issue transaction.
- Existing return and renewal actions support both borrower types. The staff circulation register searches employee name/code, reflects employee history and exports formula-safe, tenant-filtered CSV in English or Bengali. A logged-in employee can view/export **only their own** paginated borrowing history using the existing Library self-service panel.
- Employee borrowing does **not** silently generate a student invoice or claim a student fee liability. Existing student overdue/charge and V1 Finance workflows remain separate and intact.

## Migration and safe deployment

`20261030230000_V2LibraryEmployeeCirculation` follows the V3 inventory reservation-expiry and procurement-supplier migrations and changes `BookIssues.StudentId` from required to nullable, adds nullable `EmployeeId`, a composite `(TenantId,EmployeeId)` FK, tenant/employee/status index and a check constraint requiring exactly one borrower. It retains all historical rows. The downward migration refuses to run if any employee loan exists, preventing loss of borrower identity.

Apply only after checking `__EFMigrationsHistory` on the target SQL Server and creating a restorable full database backup. Ensure this migration is **after** the preceding V3 migrations. Deploy schema and application together; older application builds assume non-null `StudentId` and must not serve circulation endpoints in a mixed-version rollout. For rollback with employee history, use a reviewed forward fix, not schema downgrade.

## Acceptance and operational boundaries

1. Build the Release solution, run Library .NET and JavaScript tests, check EF model snapshot drift and run SQL Server migration acceptance on an anonymized restored database.
2. Test active/inactive employee, changed/revoked campus assignment, principal branch isolation, cross-tenant ID guesses, duplicate/replay/conflicting requests, student reservation priority, concurrent issuance, employee renewals/returns, BN/EN UI, large pagination and CSV formula escaping.
3. Reconcile employee loans separately from **student-only** overdue assessments/invoices. Employee fine policy and paid-charge refund/credit-note orchestration are **not** silently enabled: any future employee charging needs an explicitly approved payroll/receivable policy, ledger mapping and audit/reversal rules. Payments already allocated to Library student invoices remain corrected only through approved Finance credit-note/refund workflows, never by deleting posted journals or receipts.
4. Complete institutional security review, backup/restore drill, browser/mobile UAT, concurrency/load validation and signed release approval before declaring 100% production ready.

**Do not reopen or overwrite V1 Finance, student data or SaaS design solely to support this physical Library circulation change.**
