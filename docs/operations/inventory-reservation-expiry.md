# Inventory reservation expiry — manual, auditable and fail-closed

This is an explicitly scheduled per-reservation deadline, not an automatic release policy. Only a TenantAdmin, Principal or InventoryManager may set a deadline once on an active reserved Issue/Transfer, and only those roles may explicitly expire a due reservation with a reason. There is deliberately **no timer, worker, scheduled SQL job or implicit release on reads**. Unprocessed due holds continue to protect stock; posting/partial fulfilment is rejected after the deadline until an authorized expiry or release.

A serializable expiry transition changes the parent state to `Expired`, records UTC time, actor and reason, and releases only the unfulfilled hold. Already posted issue/transfer dispatch children and their destination receipt evidence are untouched. The canonical posted stock ledger remains authoritative. Expiry retries with the same reason are idempotent; conflicting reasons or stale rowversions fail closed. SQL Server check constraints, a trigger and EF change guards protect the one-time deadline and final expiry evidence from mutation.

The line-level reservation register supports an explicit history toggle. The CSV is freshly authorized and capped at 250 rows; released quantity is shown separately from fulfilled and actively reserved quantity, without adding quantities across units.

**Deployment:** additive migration `20261030223000_InventoryReservationExpiryEvidence`. Reverting it after any reservation has a deadline is deliberately blocked until an approved retention/reconciliation procedure exists. Existing V1/V2 schemas and Procurement source contracts are unchanged.

**Acceptance still required:** exact-SHA Release/EF/SQL concurrency and campus-IDOR tests, browser/mobile localization, migration repeat/rollback rehearsal and real-institute UAT. Automatic expiry remains disabled until the owner approves an explicit institutional policy.
