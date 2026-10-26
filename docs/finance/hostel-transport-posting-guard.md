# Hostel and Transport reservation posting guard

## Current policy

Financially consequential mutations to an existing canonical `StudentHostelAllocation` or
`StudentTransport` reservation must not alter dates, allocation, or rider period after
Finance has posted an invoice or payment. These mutations return HTTP 409, with no
reservation or Finance write. This includes the no-charge operations: a zero rent/fare
on the reservation is **not** evidence that no externally posted charge exists.

`IReservationFinancePolicy` checks the current tenant's enrollment invoices and student
payments inside the reservation command's serializable transaction. It treats an invoice
with a journal, paid amount, or paid/partially-paid/refunded state and a payment with
a journal or successful/refunded state as posted activity. Draft/unposted invoices do not
block. Unknown tenant, student or enrollment identifiers fail closed.

**Important limitation:** The existing Finance schema does not link a posted invoice or
payment to a specific Hostel/Transport reservation. Until a trustworthy link is
introduced, the guard intentionally blocks any posted enrollment invoice or successful
student payment, even if it belongs to another fee category. Neither invoice descriptions
nor matching amounts are safe substitutes for an authoritative reference.

## Adjustment policy and prohibited bypasses

There is currently **no approved reservation adjustment authorization** that binds a
Finance decision to a specific reservation, before/after values, payment reconciliation,
approver, and immutable operation key. Consequently **all mutations with posted financial
activity remain blocked**, including for administrators. A flag from the browser, a role,
an arbitrary approval ID, or a no-charge flag must not bypass the guard.

To enable a posted reservation change in a separate reviewed implementation:

1. Finance defines the canonical charge period (inclusive Hostel/Transport EndDate),
   proration/deposit/refund rules, and a reservation-specific authoritative link to
   posted charges and payments.
2. Finance owns an approval/adjustment record containing tenant, reservation type/ID,
   exact before/after state hash, posted invoice/payment linkage, approving identity,
   immutable request key and completed/reversed monetary journal references.
3. The Operations command validates and consumes that *approved and completed* Finance
   adjustment **atomically** with the reservation mutation, using tenant/campus checks,
   rowversion, one-time use and serializable transaction. Reject stale or reused approval
   records. Finance owns posting/reversal writes; Operations never does.
4. Integration tests cover conflicting period updates, retries, approvals for a different
   reservation or campus, mixed paid/unpaid invoices, ledger reconciliation and rollback.

## Database rollout: normalized transport identities

Migration `TransportNormalizedIdentityUniqueness` adds persisted normalized identity columns
and tenant-scoped unique filtered indexes for active Vehicle numbers and Driver licenses.
The legacy exact Vehicle-number unique index becomes nonunique, so a soft-deleted number
can be reused. New writes still use the normal application validation.

Before applying it to production, back up the database and check for per-tenant active
duplicates after trimming and uppercasing vehicle numbers and license numbers. The
migration throws SQL error `51036` or `51037` if duplicates exist; **do not** arbitrarily
delete or merge rows. Resolve each case with the owner and audit it, then rerun the
migration. It is atomic: failure must not leave the new indexes partially deployed.

Rollback drops normalized indexes/columns and restores the old unique Vehicle-number
index. **If a previously soft-deleted vehicle number has been reused, the rollback's old
unique index cannot be restored until the duplicate is reconciled.** Roll back the
application first and use a reviewed data reconciliation plan rather than forcing the
schema downgrade.

## Open follow-ups

- #45, #47, #48: occupied Hostel transfer, accurate finite-stay billing and an approved
  Finance adjustment workflow are not enabled by this guard.
- #50, #51, #37: durable request-key idempotency and additional SQL Server concurrency
  tests are still required for changes to route/vehicle, driver, and rider schedules.
- #36: legacy campus-null vehicle policy and capacity/deactivation race tests require
  a separate reviewed rollout. Existing campus-null records are retained.
