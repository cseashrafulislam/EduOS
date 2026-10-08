# Canonical model data integrity - safe remediation

## Immediate, schema-neutral correction
- `Account.ParentAccountId` remains the canonical hierarchy FK. `ParentId` now forwards getter/setter to this same value.
- `ParentId` remains ignored by EF Core; this change adds/removes **zero** database columns, avoids breaking legacy C# callers and prevents two conflicting in-memory values.
- Regression tests cover the alias and the EF hierarchy relationship.

## Required preflight before retiring legacy fields
Run `scripts/sql/model_integrity_preflight.sql` **read-only** against an isolated copy of the live database. It checks:
1. Whether a legacy `Accounts.ParentId` column still exists and disagrees with `ParentAccountId`.
2. Legacy `Tenants.CustomDomain` values without a corresponding `TenantDomains.HostName` and ambiguous active primary domain mappings.
3. `StudentInvoices` arithmetic inconsistencies.
4. Differences between gross successful payment allocations and the invoice's paid amount (**review only**, not an automatic repair because refunds/reversals exist).

## Authoritative ownership to preserve
- **Chart of accounts:** `Account.ParentAccountId`; legacy `ParentId` is a computed C# alias, never a new database column.
- **Tenant custom domains:** `TenantDomain.HostName` is the target canonical domain registry. Keep `Tenant.CustomDomain` persisted until existing values are audited/backfilled, all lookups/writes are switched, and verified deployment/migration and rollback are available. Do not silently clear it.
- **Student payments:** accepted payment records and allocations are settlement history. `PaidAmount`/`DueAmount` are existing cached billing values, protected by an arithmetic database constraint but not sufficient to prove consistency with allocations/refunds. Review and repair those **transactionally**, with a recorded reason and idempotency; never overwrite posted financial history.
- **Tenant membership:** `TenantMembership` is canonical. Do not recreate `ApplicationUser.TenantId`. Multi-tenant resolution requires validating active membership, tenant status, request-selected tenant and tenant-specific permissions. A signed tenant claim alone is not ownership authorization.

## Pending release gates
1. Inspect the preflight result and deployed schema (including legacy tables and current migrations).
2. Generate and review additive EF migrations only after final Core builds and a migration baseline is confirmed; no `EnsureCreated`, destructive column drops, unreviewed `UPDATE`, or automatic production migration.
3. Switch custom-domain read/write paths together with compatibility tests and safe backfill.
4. Implement authoritative transactional payment/allocation/refund balance reconciliation and concurrency tests.
5. Fix Service and App compilation, then run EF snapshot validation, SQL Server integration tests, tenant-isolation tests and publish.

No deployment, data correction, or database migration has been executed by this change.
