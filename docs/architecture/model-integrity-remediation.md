# Canonical model data integrity - safe remediation

## Current active model
- `Account.ParentAccountId` is the only active C# hierarchy FK. The former `Account.ParentId` alias no longer exists in the current entity, service or DTO contracts.
- `TenantDomain.HostName` is the active domain registry; the current `Tenant` C# entity no longer exposes `CustomDomain`. Do not assume deployed historical database columns or values have already been reconciled.
- These observations describe the current code, not a completed production-database migration.

## Required preflight before retiring legacy fields
Run `scripts/sql/model_integrity_preflight.sql` **read-only** against an isolated copy of the live database. It checks:
1. Whether a legacy `Accounts.ParentId` column still exists and disagrees with `ParentAccountId`.
2. Legacy `Tenants.CustomDomain` values without a corresponding `TenantDomains.HostName` and ambiguous active primary domain mappings.
3. `StudentInvoices` arithmetic inconsistencies.
4. Differences between gross successful payment allocations and the invoice's paid amount (**review only**, not an automatic repair because refunds/reversals exist).

## Authoritative ownership to preserve
- **Chart of accounts:** `Account.ParentAccountId` is canonical. A historical physical `Accounts.ParentId` column, if present, requires read-only preflight before any schema cleanup.
- **Tenant custom domains:** `TenantDomain.HostName` is the canonical domain registry. Historical `Tenants.CustomDomain` database values, if present, require audit/backfill and a reviewed deployment/rollback before any column removal. Do not silently clear them.
- **Student payments:** accepted payment records and allocations are settlement history. `PaidAmount`/`DueAmount` are existing cached billing values, protected by an arithmetic database constraint but not sufficient to prove consistency with allocations/refunds. Review and repair those **transactionally**, with a recorded reason and idempotency; never overwrite posted financial history.
- **Tenant membership:** `TenantMembership` is canonical. Do not recreate `ApplicationUser.TenantId`. Multi-tenant resolution requires validating active membership, tenant status, request-selected tenant and tenant-specific permissions. A signed tenant claim alone is not ownership authorization.

## Pending release gates
1. Inspect the preflight result and deployed schema (including legacy tables and current migrations).
2. Generate and review additive EF migrations only after final Core builds and a migration baseline is confirmed; no `EnsureCreated`, destructive column drops, unreviewed `UPDATE`, or automatic production migration.
3. Reconcile any historical custom-domain values into the canonical registry, validate resolution and rollback, then review the physical schema cleanup separately.
4. Implement authoritative transactional payment/allocation/refund balance reconciliation and concurrency tests.
5. Fix Service and App compilation, then run EF snapshot validation, SQL Server integration tests, tenant-isolation tests and publish.

No deployment, data correction, or database migration has been executed by this change.
