# SaaS V1 capacity and security handoff

Scope: EduOS `Ashraful` branch only. HR/Payroll remains part of V1. No changes
to V2 feature ownership or to payment/tax/refund policies are implied here.

## Four implemented workstreams

1. **Active students:** The effective subscription's `MaxStudents` is enforced
   when an admission converts to a student, a whole student-import batch runs,
   a student is readmitted, or a non-active student is reactivated. A limit of
   zero means unlimited. A denied allocation returns a conflict and must not
   commit any student or partial import batch. A tenant-specific SQL Server
   transaction application lock serializes these otherwise independent paths.
2. **Active employees:** `MaxEmployees` is enforced for HR creation and
   suspended-employee reactivation. Existing active employees can be edited,
   suspended or exited without reserving an additional seat. HR remains V1.
3. **Private file storage:** Tenant document and message uploads already
   enforce `MaxStorageMb`. Anonymous public-admission uploads now require a
   verified applicant plus the tenant resolved from the published portal, and
   check the same quota under a serializable database transaction before
   recording a `FileAsset`. On rejection the newly uploaded private blob is
   deleted. **Payment deposit slips are explicitly exempt from blocking** so
   a tenant with full storage can still submit renewal/upgrade evidence. Those
   files are private, limited to 5 MiB and one pending payment per invoice,
   and are included in storage usage.
4. **Visibility and regression:** Usage History exposes live active students,
   employees, member seats and committed storage separately from dated usage
   snapshots. Unit, JavaScript contract and real SQL Server concurrency tests
   cover limits and transaction behavior.

## Operational behavior

- Quota counts are tenant-scoped and ignore logically deleted records.
- No current effective plan means no *invented* quota. Existing subscription
  access gates still decide whether an action is authorized.
- Zero plan limit is unlimited; positive limits are hard caps on new active
  capacity. A reduction below current usage does not automatically delete or
  suspend existing members, students, employees or files.
- A capacity conflict is a `409`; the administrator should review the plan,
  free capacity where appropriate, or upgrade.
- The UI's daily snapshots are history; live counters reflect the current
  database state and are **not financial billing meters**.

## Validation before release

1. Run GitHub CI on `Ashraful` (`dotnet` compile/test, JavaScript contracts).
2. Run SQL Server migration smoke + `SqlServerStudentImportConcurrencyIntegrationTests`.
3. Perform a staging end-to-end test with two tenants and positive student,
   employee and file limits, including a simultaneous create/import conflict.
4. Check public applicant ownership, re-upload replay, blob cleanup, and a
   valid unpaid invoice with full storage (deposit slip still permitted).
5. Verify effective subscription dates, plan-change effective dates, manual
   payment verification and canceled auto-renewal in the deployment environment.

Not certified here: real payment-provider callbacks, statutory invoices/taxes,
refund execution, production load/penetration testing and full recovery drills.
Those require approved provider contracts, deployment credentials and
institution-approved policy fixtures; do not silently simulate them as live.
