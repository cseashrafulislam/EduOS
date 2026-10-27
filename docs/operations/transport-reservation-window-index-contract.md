# Transport date-window persistence contract — Issues #36, #37, #50, #51

Status: **Platform date-window indexes added; Operations integration and Finance billing policy remain separate work.**
Migration: `20261027012500_TransportReservationWindowIndexes`. Canonical tables and EF entities are unchanged.

## Verified pre-existing guarantees

- `BaseEntity.RowVersion` maps to SQL Server `rowversion` and EF concurrency for Vehicle, TransportDriver, StudentTransport, RouteVehicleAssignment and VehicleDriverAssignment.
- `IX_Vehicles_TenantId_NormalizedVehicleNumber` and `IX_TransportDrivers_TenantId_NormalizedLicenseNumber` already enforce tenant-scoped soft-delete-aware identity uniqueness. The driver filtered predicate references **LicenseNumber**, not its computed normalized column.
- Existing `IsCurrent = 1 AND IsDeleted = 0` unique indexes prevent two simultaneously current route/driver links for the same vehicle/driver, including *non-overlapping future links*. **Do not weaken these without a scheduling-policy migration and concurrency tests.**
- `IdempotencyRecords` and `IReservationCommandReceiptStore` already persist tenant+scope+request GUID, target, actor, payload hash and serialized successful response. No new receipt table is needed.
- `StudentHostelAllocation.PreviousAllocationId` and the tenant-bound predecessor FK already support occupied-transfer lineage (#45); Finance approval and Operations command remain pending.

## Additive query-plan change

Five filtered **nonunique** indexes now cover `(TenantId, resourceId, fromDate, toDate)` for:
`StudentTransports` by StudentId and VehicleId; `RouteVehicleAssignments` by VehicleId; `VehicleDriverAssignments` by VehicleId and TransportDriverId. All filters use `[IsDeleted] = 0`, a normal SQL Server column. No historical rows are modified or backfilled. The migration can be rolled back by dropping only these five indexes; allow extra storage and migration-time locking for index creation.

These indexes make tenant/resource date-range probes and SQL Server key-range locking practical; **indexes alone do not prevent overlapping intervals**. Services must continue to use serializable transactions, deterministic lock order, date-overlap rechecks, RowVersion comparisons and conflict-to-409 mapping. Do not claim database exclusion constraints exist.

## Exact dependent-service contract

For any route-vehicle change (#50), driver change (#37) or rider period mutation (#51), Operations must:
1. Require tenant/campus authorization, a nonempty ClientRequestId and expected aggregate RowVersion; never infer campus from a null legacy vehicle CampusId. New vehicles require an active authorized campus; legacy campus-null vehicles remain readable but cannot be newly assigned until explicitly reconciled.
2. Begin a short **SQL Server SERIALIZABLE** transaction; obtain resource guards in deterministic ascending (tenant, vehicle, driver, student) order. Check `IReservationCommandReceiptStore.ReplayAsync` **before** rejecting stale row versions; same actor/target/hash returns the stored original result, key reuse with changed inputs is 409.
3. Query every non-deleted intersecting period for the same resource, not just `EndDate IS NULL` or `IsCurrent`. Inclusive overlap: `existing.From <= proposed.ToOrMax AND existing.ToOrMax >= proposed.From`. Recheck vehicle capacity and rider allocations under the same transaction.
4. Save canonical entity and immutable audit, call `RecordAsync` for **successful** response, save the receipt and commit **once**. A unique-key/rowversion/deadlock conflict must not be treated as success; on deadlock retry only with a new DbContext and replay check.
5. Keep Finance-owned fares, invoices and ledgers unchanged. Paid rider extensions/shortening and occupied-stay transfers require the Finance preflight/adjustment contract.

`IReservationCommandReceiptStore` signatures:
`ReplayAsync<T>(long tenantId, long actorId, string scope, Guid requestId, long targetId, string requestHash, CancellationToken ct)`
and
`RecordAsync<T>(long tenantId, long actorId, string scope, Guid requestId, long targetId, string requestHash, ApiResponse<T> response, CancellationToken ct)`.

Use distinct operation scopes (e.g. `transport.route-vehicle-change`, `transport.upcoming-driver-change`, `transport.rider-extend-upcoming`) and SHA-256 of a canonical full request including original row versions. This migration does **not** wire receipt replay into Operations commands; exact-state retries are not equivalent to durable request-key idempotency.

## SQL Server computed-column index legality (#36/#37)

The deployed normalized vehicle and driver identity indexes use persisted computed
columns as **index keys**, not in their filtered predicates. The vehicle filter
uses the ordinary `IsDeleted` column; the driver filter uses ordinary
`LicenseNumber` and `IsDeleted` columns. An index filter referencing
`NormalizedLicenseNumber` or `NormalizedVehicleNumber` would fail SQL Server
DDL even though those computed columns are persisted.

`TransportReservationWindowIndexTests.Sql_server_normalized_identity_indexes_use_legal_base_column_filters`
now checks the **deployed SQL Server catalog**: both unique filtered indexes
exist, their normalized key columns are persisted and indexed, and neither
predicate references any computed column. The existing SQL migration smoke
already executes this test class. This is an index-regression guard, not
proof of scheduled-driver service behavior or a change to historical data.

## Verification / remaining acceptance

Platform test `TransportReservationWindowIndexTests` asserts EF model metadata and, when `EDUOS_SQL_AUTH_TEST_CONNECTION` is set, checks five SQL Server indexes after migration. Existing `SqlServerTransportIdentityIntegrationTests` covers real rowversion, normalized uniqueness, filtered current links and concurrent request-key uniqueness. Remaining: two-DbContext competing **service command** races for vehicle capacity/deactivation, overlapping route/driver scheduling, rider extension and durable request replay; Operations owns these command tests and integration. Finance owns paid-period billing and #45 transfer charges. Do not mark Issues #36/#37/#50/#51 fully closed from this persistence-only change.

## Concurrent SQL Server constraint regression (2026-10-27)

`SqlServerTransportConcurrentUniquenessTests` uses two independent tenant-scoped
`EduOSDbContext` instances per competing write against the disposable SQL Server
database. It checks that **exactly one** of two concurrent writers can insert:
(a) case/whitespace-equivalent vehicle numbers, (b) case/whitespace-equivalent
driver licenses, (c) current route links for one vehicle, and (d) current driver
links for one driver. Only SQL Server unique-index violations (2601/2627)
count as expected conflicts; unrelated database failures fail the tests.
A separate test verifies stale driver `rowversion` updates are rejected.

These tests are included in `sqlserver-migration.yml` and require
`EDUOS_SQL_AUTH_TEST_CONNECTION`. They validate deployed constraints,
**not** Operations service-level capacity/deactivation races, overlap
rechecks, cancellation-vs-extension races or HTTP 409 mapping. Those remain
separate two-command integration requirements for Issues #36/#37/#50/#51.
No new migration, duplicate receipt table, or schema backfill is introduced.

## Simultaneous canonical mutation regression (2026-10-27)

`SqlServerTransportConcurrentUniquenessTests` now also loads the **same SQL Server rowversion**
in two independent tenant-scoped contexts and starts both writes concurrently:
- Vehicle capacity reduction versus vehicle deactivation (#36).
- Upcoming route-vehicle replacement versus effective-period modification (#50).

Exactly one writer must commit; the other must receive `DbUpdateConcurrencyException`.
Readback must contain **one entire winner state**, never a merged/lost update.
These tests run under `EDUOS_SQL_AUTH_TEST_CONNECTION` in the existing SQL migration smoke workflow.
They do **not** prove that concurrent writes to **different** aggregates (rider assignment
versus capacity reduction/deactivation, or two overlapping noncurrent route links)
are serializable; those require Operations-owned service-command tests with deterministic
resource locking and HTTP 409 mapping. No migration, index change or new receipt store is needed.

## Atomic durable-receipt SQL Server regression (2026-10-27)

`SqlServerTransportIdentityIntegrationTests.Canonical_vehicle_update_and_durable_command_receipt_commit_or_rollback_together` uses the deployed SQL Server schema, the existing `ReservationCommandReceiptStore`, and one canonical `Vehicle` row. It verifies that a serializable transaction rolling back after both `SaveChanges` calls preserves neither the vehicle mutation nor its command receipt. A subsequent committed transaction persists both, and a fresh tenant-scoped DbContext can replay the exact original response; changed request hashes and actors return conflict. This is **shared persistence contract coverage**, not a replacement for Operations' command-level authorization, audit, capacity, or cross-aggregate race tests. The existing SQL migration smoke test filter already includes this test class; no migration or DI change is required.
