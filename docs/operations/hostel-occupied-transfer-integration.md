# Occupied-stay Hostel transfer — shared persistence and integration contract

Status: **Platform lineage schema defined; Operations command/UI and Finance policy remain pending.**
Canonical aggregate: `StudentHostelAllocation` (no parallel allocation table).

## Delivered Platform schema (migration `20261027003000_HostelOccupiedTransferLineage`)

- Nullable `StudentHostelAllocations.PreviousAllocationId bigint` references the **same tenant's** prior allocation via composite FK `(TenantId, PreviousAllocationId) -> (TenantId, Id)` with restricted deletion.
- Filtered unique index `(TenantId, PreviousAllocationId) WHERE PreviousAllocationId IS NOT NULL` guarantees **at most one successor** for each predecessor, even if the successor is soft-deleted. The filter uses a regular column, not a computed expression.
- Check constraint rejects a self-reference. `BaseEntity.RowVersion` remains the existing SQL Server `rowversion` concurrency token. `ClientRequestId` retains its existing tenant-unique constraint.
- Migration is **additive**: all legacy allocations receive `NULL` predecessor; do not infer historical transfers, merge rows or rewrite their original bed IDs. Rollback drops only this new relationship and its index/check, but is unsafe after new transfers have been recorded unless those relationships have been exported and explicitly reconciled.

## Exact Operations command contract (not implemented by Platform)

An authorized tenant/campus-scoped occupied-transfer command must take:
`sourceAllocationId`, `destinationBedId`, `effectiveFrom` (DateOnly),
`sourceRowVersion` (base64), `destinationBedRowVersion` (base64),
`clientRequestId` (nonempty GUID), and a bounded, auditable reason.
The response returns the original source ID, newly created successor ID and both updated row versions.

**Atomic transaction** (SQL Server SERIALIZABLE; all queries explicitly tenant-filtered):
1. Use `IReservationCommandReceiptStore.ReplayAsync` with scope `hostel.occupied-transfer`, target ID = source allocation ID, actor ID and SHA-256 hash of the **canonical complete request**, including original row versions. Identical successful retries return the stored response; key reuse with different target/actor/body is 409. Never create a duplicate receipt store or reuse an unrelated operation scope.
2. Verify tenant membership, source student's current enrollment/campus access, source occupied/active status, source row version, destination active hostel/room/bed and campus permission. Preserve gender and physical room capacity restrictions. Reject source and destination equal.
3. Verify `effectiveFrom > source.StartDate` and `effectiveFrom <= source.EndDate` when a finite end date exists. For inclusive `DateOnly` periods, source `EndDate = effectiveFrom.AddDays(-1)`; successor `StartDate = effectiveFrom`, successor `EndDate = original source.EndDate`. Reject transfers on the first day (no zero-length predecessor), and guard DateOnly.MinValue.
4. Lock canonical source/student/enrollment and both bed/room capacity guards in a **deterministic ascending ID order**, then re-evaluate all overlapping allocations (including historical/future ranges, not just `EndDate IS NULL`). A single open-ended-bed unique index is not sufficient for arbitrary date ranges. Validate no duplicate student occupancy or over-capacity bed/room intervals at commit.
5. Update the predecessor's **end date and state only**; never change its `HostelBedId`, original rent, deposit or fee-head history. Create one successor `StudentHostelAllocation` with `PreviousAllocationId = source.Id`, new bed, original student/enrollment, dates and finance-approved tariff values. Keep tenant IDs identical. Do not mutate existing ledger/invoice/payment rows.
6. Save source and successor, record an immutable audit with actor/time/reason and a **successful** `IReservationCommandReceiptStore.RecordAsync` result in the **same transaction**. Commit once. Translate rowversion/unique/FK conflicts to 409, not silent retries. Deadlocks (1205) may be retried only from a fresh DbContext after replay checks; never retry partial mutations.

**Ownership:** Operations owns DTO/service/API/Razor/JS/menu/report integration. Platform owns DbContext, migrations, global DI and receipt store. Finance owns tariffs, proration, deposit carry/refund, posted adjustments and any billing preflight. Until Finance confirms no-charge and paid transfer policy, **do not enable occupied transfers in production**.

## Other shared integration issues

- **#36:** Existing SQL rowversion covers Vehicle, RouteVehicleAssignment and StudentTransport. Normalized vehicle number uniqueness is tenant-scoped for non-deleted rows. Legacy `Vehicle.CampusId = NULL` must remain readable; new vehicle creation must require an authorized active campus. Concurrent capacity reductions and rider reservations need Operations transaction/integration tests.
- **#37:** Existing SQL-compliant driver index uses `LicenseNumber IS NOT NULL AND IsDeleted = 0` as filter and indexes the persisted computed `NormalizedLicenseNumber`. Driver PII remains TenantAdmin-only until campus authorization policy is defined. Scheduled driver links cannot be modeled as multiple `IsCurrent = 1` rows for the same driver/vehicle.
- **#50:** Existing durable `IdempotencyRecord` and `IReservationCommandReceiptStore` support scope `transport.route-vehicle-change`; Operations must integrate it atomically. Filtered `IsCurrent` uniqueness does not protect all date-overlap cases; SERIALIZABLE locking/recheck remains mandatory.
- **#51:** Reuse existing receipt scopes for rider extension/shortening with canonical payload hash, original response replay and tenant-bound target ID. Paid extension/shortening requires Finance-owned billing policy and authorized adjustment path; Platform must not write Finance ledgers.

## Acceptance / remaining tests

Platform: EF model metadata validates composite same-tenant FK, unique predecessor and self-reference check. The SQL Server migration smoke now executes `HostelOccupiedTransferLineageContractTests` against the migrated database, including trusted composite FK column order, filtered unique predecessor index, legal noncomputed filter, and the existing EF model contract. Operations: simultaneous destination claims, stale source/destination versions, cross-tenant/campus, overlapping finite reservations, exact retries, changed-payload replay, and same-day/boundary behavior. Finance: invoice/deposit/proration/paid-period integration. Do not claim operational completeness until these pass.
