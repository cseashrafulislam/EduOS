# V3 Inventory & Store — final engineering acceptance batch (2026-10-31)

## Scope, owner, and source of truth

This is a V3 Inventory & Store **engineering/QA** handoff. It preserves V1/V2 development freezes, the existing `InventoryMovement` / `InventoryMovementLine` as the sole posted stock ledger, and independent Procurement and Fixed Asset ownership. No new stock-balance table, Finance payable or inventory movement provenance bypass is introduced. Institutional production release acceptance is separately gated.

## Physical count report / bilingual mobile

- A StoreKeeper/InventoryManager/Principal/TenantAdmin or authorized Accountant may request `GET /InventoryMovements/PrintPhysicalCount?reference={uuid}` through the existing `INVENTORY`-entitled controller. The action always obtains a **fresh** `GetPhysicalCountAsync` read under tenant and campus authorization; foreign references are not printed. Invalid empty UUIDs are rejected. Responses are private, no-store and `nosniff`.
- The A4 landscape count sheet includes immutable source/reference, store, count state, UTC snapshot and decisions, maker/checker, per-item/lot book/reserved/measured/suggested/posted numbers, costing and signed reason/lineage. Unlike units are **not totalled**. Razor encodes user-provided lot/reason. A blind Draft prints the usable handwritten-count sheet with its book/valuation/variance fields **redacted**, not a leaked snapshot.
- Existing Inventory UI now offers a count-sheet Print action and accessible single-header information icon, with Bangla/English labels and responsive controls. Existing authorized CSV remains separate; print does not rely on client-supplied cached count values.
- A pinned Chromium workflow (`inventory-physical-browser.yml`) runs a 390px English maker/blind-count flow and Bangla checker/ManualFinal flow, with intercepted **mock API** responses and anti-forgery assertions. This verifies real DOM/JS behavior, not a live-institute authorization substitute. Server-side ownership and blind print data are verified independently on disposable SQL Server.

## Three-store ledger reconciliation and concurrent stock integrity

SQL tests in `SqlServerV3InventoryMultiStoreAcceptanceTests.cs` deliberately use the existing movement service on a disposable SQL Server database:

1. Opening receipt of 10 units in Store A; reserve four; reject a seven-unit transfer that would consume another hold; post six from A→B and two from B→C; verify A4/B4/C2, active reservation4 and total on-hand10 (one item/lot base unit, not summed unlike UOMs).
2. Independently approved physical count of three at Store B posts one unit shortage through canonical `PhysicalStockCount` source; verify A4/B3/C2 and exactly nine remaining across locations, no revision of the other store's reserved balance, and idempotent checker replay.
3. Competing seven-unit transfers from Store A to two different destination stores in distinct SQL contexts must produce **one** success and **one** HTTP 409. Validate only one posted movement, A3 and a total of seven in the destinations, not fourteen; no double-spend or negative stock.
4. The count print is checked with an authenticated draft and with a separately submitted count. A foreign-tenant requested print gets HTTP 404; a blind draft has no book quantity, value, reservation or variance in the service projection.

The suite deliberately uses transactions, rowversions and ownership checks already implemented by Inventory. No new financial journals, balances, migrations, or unsafe synthetic stock adjustments are added.

## Engineering acceptance evidence and issue governance

The code change itself must be validated on its **exact pushed SHA** by:

- `CI`: Release build/publish, all .NET and JavaScript contracts, auth regressions, EF snapshot and idempotent SQL script.
- `SQL Server Migration Smoke`: fresh/repeat migrations, the existing V1/V2 and V3 Inventory SQL acceptance subset including the new three-store tests, backup/restore and Finance regression safety.
- `Inventory Physical Count Browser Acceptance`: real headless Chromium execution at 390px for English and Bangla with a mocked API surface. Its result does not prove deployment-level UAT or server-authenticated Chromium.

Only **after all three are verified** should historical Inventory issues [#109](https://github.com/cseashrafulislam/EduOS/issues/109) and [#112](https://github.com/cseashrafulislam/EduOS/issues/112) receive updated exact-commit evidence and closure of their *engineering-code* checklist. If any issue's acceptance explicitly requires real pilot, measured p95/p99, restore on actual data, or sign-off, mark those as pending operational gates and do **not** claim production certification. These external gates are not new V3 feature-development tickets.
