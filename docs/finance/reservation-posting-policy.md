# Finance reservation posting policy — Hostel and Transport

Scope: institutional Finance only; no Platform SaaS billing. Issues #45, #47, #48, #51.

## Current enforceable rule

- Existing canonical Hostel/Transport reservations remain the source of truth.
- Operations must reject any financially effective date, term, bed/room transfer, cancellation or extension after a posted invoice/payment unless an **approved, tenant-scoped, idempotent Finance adjustment** has been persisted and verified inside the write transaction. The approval contract does not exist yet; therefore these mutations stay blocked.
- Even without posted activity, **chargeable** reservations (nonzero rent, fare or deposit, or linked fee head) stay blocked for financially effective amendments until Finance establishes billing period and proration policy. An issued but unposted invoice also blocks until reconciled.
- A Finance preflight is a read-only exposure snapshot, not approval, a booking lock, or permission to change a reservation. Operations must revalidate against current ledger state in its own transaction. Unrelated student payments are conservatively treated as potential reservation activity because no canonical reservation-to-invoice/payment FK exists.
- No implicit daily proration, retroactive billing, deposit settlement or earned-rent recognition is authorized. Do not edit posted invoices, payment allocations or journals; use future Finance-owned contra/adjustment entries with balancing and audit.

## Decisions awaiting an approved Finance policy

- #47: start-date correction may shift billable period and due date; block after posting, and block charged changes without a billing policy.
- #48: inclusive EndDate controls occupancy only; it is **not yet** an approved last billable day or automatic deposit refund date. A 90-day occupancy limit does not imply a 90-day billing rule.
- #51: paid Transport extension/shortening must not silently regenerate charges; block until an approved Finance-owned adjustment/recurring billing contract exists.
- #45: occupied Hostel transfers require predecessor/successor allocation provenance, effective-date proration policy, and deposit carry-forward/settlement decisions. Operations cannot write GL.

## Platform integration needed before enabling charged/posted amendments

1. Canonical reservation source linkage on invoice lines / financial adjustments (tenant, module, reservation, effective period), with indexes and unique constraints preventing double billing.
2. Durable tenant-scoped idempotency record keyed by operation, reservation and request key, with canonical payload hash, stored outcome, rowversion and audit actor.
3. Finance adjustment approval aggregate (maker/checker, state transitions, accounting period, signed decimal(18,2) amounts, currency, reason, immutable posting/reversal journal provenance), plus DI and EF migration owned by Platform.
4. One transactionally coordinated authorization/commit protocol (or outbox/saga with explicit pending state) between Finance approval and Operations reservation mutation. No time-of-check/time-of-use gap.
5. SQL Server integration tests for cross-campus/tenant, posted and unpaid-issued invoices, replay, mismatched key, concurrency, double-entry balance and reversal.

Finance preflight: `GET /api/finance/adjustments/reservations/preflight?kind=hostel&id=<canonical-id>` (or `transport`). TenantAdmin + FINANCE permission required. The response always has `financialMutationApproved=false`.

## Finance preflight transport campus integrity

The read-only Transport preflight now requires a tenant-owned, non-deleted route and vehicle and checks each explicit campus against the reservation enrollment campus. Legacy institution-wide route/vehicle campus NULL is accepted, but mismatched explicit campuses, missing links and a mismatched optional route-vehicle assignment fail closed with HTTP 409. The response exposes canonical route/vehicle IDs for Finance reconciliation. This check does not authorize or post a reservation change; Operations remains responsible for enforcing the same invariant inside its write transaction. No schema/DI change is required.

## Issued invoice boundary enforced by the Finance policy gate

The shared `IReservationFinancePolicy.HasPostedActivityAsync` gate now conservatively returns `true` for an **issued, unposted** enrollment invoice as well as posted invoice/payment activity. This is intentional: an issued invoice is a billing commitment even if no journal or payment has been recorded. Hostel pre-arrival rescheduling and Transport upcoming extension already call this Finance-owned gate inside their serializable reservation transactions; consequently a zero-rent/zero-fare reservation cannot bypass issued-invoice reconciliation. A cancelled invoice with no posting reference does not block, while any journal-linked invoice continues to block regardless of state. The API preflight already reports `HasUnpostedIssuedInvoice` and `RequiresBillingPolicy`; no approval or proration is implied. The approval/adjustment aggregate, source linkage and Finance/Operations atomic coordination remain Platform integration dependencies.

## Finance reservation review worklist

`GET /api/finance/adjustments/reservations/worklist?kind=hostel|transport&campusId=&startFrom=&startTo=&onlyReviewRequired=true&page=1&pageSize=25` is a TenantAdmin/FINANCE read-only queue. It uses canonical reservation/enrollment records and filters financially exposed rows in SQL **before** bounded pagination. Page exposure flags are batched by enrollment/student, not guessed from reservation descriptions. The campus column reflects enrollment campus; the per-reservation preflight must validate linked Hostel beds or Transport routes/vehicles. Selecting **Inspect preflight** opens the existing read-only validation, not an approval action. Issued invoices, posted invoices/payments and charged terms are review triggers. There is no Finance adjustment approval or mutation API until Platform delivers the persistence/coordination contract.
