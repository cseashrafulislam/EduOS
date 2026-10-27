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
1. Require tenant/campus authorization, a nonempty ClientRequestId and expected aggregate RowVersion; never infer campus from a null legacy vehicle CampusId. New vehicles require an active authorized campus; legacy campus-null routes/vehicles must not be newly assigned until explicitly reconciled. **Known unresolved Operations gap:** `TransportService.AssignAsync` currently permits null `CampusId` to bypass its campus comparison for existing route–vehicle links; do not treat this policy as enforced on the rider-write path.
2. Begin a short **SQL Server SERIALIZABLE** transaction; obtain resource guards in deterministic ascending (tenant, vehicle, driver, student) order. Check `IReservationCommandReceiptStore.ReplayAsync` **before** rejecting stale row versions; same actor/target/hash returns the stored original result, key reuse with changed inputs is 409.
3. Query every non-deleted intersecting period for the same resource, not just `EndDate IS NULL` or `IsCurrent`. Inclusive overlap: `existing.From <= proposed.ToOrMax AND existing.ToOrMax >= proposed.From`. Recheck vehicle capacity and rider allocations under the same transaction.
4. Save canonical entity and immutable audit, call `RecordAsync` for **successful** response, save the receipt and commit **once**. A unique-key/rowversion/deadlock conflict must not be treated as success; on deadlock retry only with a new DbContext and replay check.
5. Keep Finance-owned fares, invoices and ledgers unchanged. Paid rider extensions/shortening and occupied-stay transfers require the Finance preflight/adjustment contract.

`IReservationCommandReceiptStore` signatures:
`ReplayAsync<T>(long tenantId, long actorId, string scope, Guid requestId, long targetId, string requestHash, CancellationToken ct)`
and
`RecordAsync<T>(long tenantId, long actorId, string scope, Guid requestId, long targetId, string requestHash, ApiResponse<T> response, CancellationToken ct)`.

Use distinct operation scopes (e.g. `transport.route-vehicle-change`, `transport.upcoming-driver-change`, `transport.rider-extend-upcoming`) and SHA-256 of a canonical full request including original row versions. **Current HEAD implementation:** `TransportService.RouteVehicleCreate.cs`, `TransportService.UpcomingRouteVehicleChange.cs`, and `TransportService.UpcomingDriverChange.cs` already call `ReplayAsync` and `RecordAsync` inside serializable transactions. Preserve that wiring; do **not** add another receipt store or duplicate replay logic. Other commands must be verified individually rather than assumed complete.

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

Platform test `TransportReservationWindowIndexTests` asserts EF model metadata and, when `EDUOS_SQL_AUTH_TEST_CONNECTION` is set, checks five SQL Server indexes after migration. Existing `SqlServerTransportIdentityIntegrationTests` covers real rowversion, normalized uniqueness, filtered current links and concurrent request-key uniqueness. Existing SQL Server tests now include competing vehicle/driver identity writes, same-row rowversion conflicts, rider booking versus vehicle capacity reduction, overlapping future route–vehicle creation, upcoming route–vehicle replacement versus rider booking, and competing upcoming driver changes with durable replay. **Still unverified:** all cross-aggregate deactivation/assignment races, the full finite-interval scheduling policy, and every rider cancellation/extension/transfer combination. Operations owns service-command coverage and HTTP 409 mapping. Finance owns paid-period billing and #45 transfer charges. Do not mark Issues #36/#37/#50/#51 fully closed from this persistence-only change.

## Latest verified command integration (2026-10-27)

At `Ashraful` HEAD `6e19b7dbf2e6fb80bdbc25381953a1fe17cd9ca2`, Operations already uses the shared durable receipt store in route–vehicle creation, upcoming route–vehicle change and upcoming driver change; SQL Server two-DbContext tests cover competing future driver changes (one winner, one HTTP 409 and winning request replay) and upcoming route–vehicle replacement racing a rider booking. [CI](https://github.com/cseashrafulislam/EduOS/actions/runs/40448313081) and [SQL Server Migration Smoke](https://github.com/cseashrafulislam/EduOS/actions/runs/40448313075) both passed on that commit. These checks do **not** prove full production readiness.

**Campus-scope gap (#36):** `TransportService.GetRoutesAsync` and `GetVehiclesAsync` still expose null-campus legacy records to non-admin campus-scoped operators through an `allowed.Any()` fallback. `TransportService.AssignAsync` checks mismatch only for nonnull route/vehicle campuses, permitting new rider reservations through pre-existing historical links if campus is null. Operations must require explicit active, matching campus for new reservations and hide unassigned records from ordinary campus-scoped lists. Preserve TenantAdmin-only historical reconciliation; no automatic backfill or Platform migration is justified. [Issue #36](https://github.com/cseashrafulislam/EduOS/issues/36).

**Scheduling contract (#37/#50):** the deployed unique filtered `IsCurrent` indexes prohibit multiple simultaneously-current rows even if their effective periods do not overlap. Do not change those indexes or claim general date-range exclusion without an agreed scheduling policy, additive migration and concurrency regressions.

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

## Tenant and soft-delete identity lifecycle regression (2026-10-27)

`SqlServerTransportConcurrentUniquenessTests.Normalized_transport_identity_keys_are_tenant_scoped_and_reusable_after_soft_delete`
tests the **deployed SQL Server** filtered normalized vehicle-number and driver-license
indexes across two tenant contexts. Both tenants may independently register the same
case/whitespace-equivalent identifiers; after soft-deleting an original vehicle and
driver, the original tenant may register replacements with the same normalized keys.
The deleted records remain stored, and one active identity per tenant remains.
This complements the existing same-tenant concurrent duplicate rejection tests.
It does **not** authorize assignment of legacy campus-null vehicles: Operations must
require explicit active-campus reconciliation before any new assignment. No schema
migration, backfill, identity merge or change to the existing computed-column
filtered predicates is needed.
