# Integration contract request — occupied-stay Hostel transfer

Status: **Awaiting Platform and Finance contracts**. Operations must not implement an unsafe in-place `HostelBedId` mutation.

## Existing canonical source of truth

`StudentHostelAllocation` is the persisted aggregate for a student's bed stay: `ClientRequestId`, `StudentId`, `StudentEnrollmentId`, `HostelBedId`, `StartDate`, `EndDate`, `MonthlyRent`, `SecurityDeposit`, `FeeHeadId`, `State`, tenant/audit fields and row version. Pre-arrival bed and room reassignment exists. Occupied-stay transfer is not covered by those methods.

## Platform-owned decision and delivery required

1. Specify a persisted predecessor/successor relationship or transfer ledger so the source stay is historically immutable and the destination stay has its own effective period. No duplicate active stay for one student on the same day.
2. Specify effective-date boundary semantics (source ends day before destination starts, unless an explicit same-day checkout/arrival model is introduced). Never rewrite historical source `HostelBedId`.
3. Persist a **tenant-scoped unique idempotency key** for transfer retries, including request-body conflict handling. Confirm optimistic row version and durable actor/time/reason audit.
4. Own shared DbContext mapping, migration, constraints and DI changes. Confirm SQL Server serializable/locking strategy to protect destination bed and room capacity across concurrent allocations.
5. Provide commit SHA, schema/API contract and rollback/migration compatibility plan. Operations will not edit Platform-owned persistence.

## Finance-owned decision and delivery required

1. Define whether same-tariff transfers carry `MonthlyRent`, `SecurityDeposit` and `FeeHeadId` forward unchanged; define behavior for cross-tariff transfers.
2. Specify effective-date proration, deposit carry/refund, adjustments, fee-head mapping and accounting posting responsibility.
3. Expose an idempotent Finance adjustment contract (same tenant/request reference), including transaction/outbox boundaries and compensation on failure.
4. Provide contract and commit SHA. Operations will not write Finance-owned monetary records.

## Operations implementation after confirmation

- Authorized options query and transfer command; canonical DTO/service/API/MVC/Razor/JS and navigation in existing Hostel Operations workspace.
- Validate tenant, enrollment, authorized campus, hostel gender restrictions, active room/bed, date overlap, room capacity and destination bed availability.
- Enforce atomic source closure/successor creation, optimistic concurrency, request idempotency and audit; reject unsafe cross-campus changes.
- Test concurrent destination claims, duplicate request/retry, stale row version, backdated/future effective dates, overlapping reservations, closed/cancelled stays, Finance failures and privacy-safe history/report exports.

**Acceptance gate:** historical source stay remains queryable; one current bed per student; no double occupancy or duplicate charge; tenant/campus isolation; successful .NET build and SQL Server integration tests.
