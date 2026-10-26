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

Separately, an *upcoming Transport extension with a nonzero MonthlyFare* remains
blocked even with no posted Finance activity: extending a chargeable billing period
requires a Finance-owned schedule/proration contract. A zero-fare extension may
proceed only after the posting guard and all seat/overlap checks pass. Paid shortening
is blocked; zero-fare shortening is still subject to the posting guard.

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
- #50, #51, #37: durable request-key handling now uses the canonical `IdempotencyRecord`
  table. Its unique `(TenantId, Scope, RequestId)` index prevents duplicate receipt
  insertion; added optional `TargetId`, `CompletedByUserId`, `CompletedAt` and `ResultJson`
  preserve actor, canonical target and original response. Successful outcomes are
  stored in the *same serializable transaction* as the reservation mutation. A retry
  with identical actor, target, key and payload returns the original response; key
  reuse with different details returns 409. The UI retains the key for a retry but
  generates a new one when the payload changes. `ClientRequestId` is mandatory on
  these commands. No Finance monetary entries are created by this mechanism.
  SQL Server integration coverage for simultaneous schedule conflicts and deadlock
  retry remains open; a collided concurrent attempt may currently return 409 and
  needs a retry using the original key after the competing transaction commits.
- #36: legacy campus-null vehicle policy and capacity/deactivation race tests require
  a separate reviewed rollout. Existing campus-null records are retained.

## Receipt migration and deployment

`ReservationCommandReceipts` adds four **nullable** columns to the existing receipt
table, without modifying old receipts. Deploy the schema migration before the API/UI
code that writes the new columns; old receipts lacking actor/target/result are treated
as conflicts rather than a fabricated success. After deployment, verify same-key
replay, conflicting-key rejection, cross-tenant boundaries and rollback of both the
reservation and receipt under a failed transaction. Rolling back the migration after
new receipts exist removes their response/audit fields: retain a database backup
and never silently downgrade a system actively using this workflow.
