# EduOS V3 Inventory — partial reserved issue acceptance contract

Owner directive 2026-10-30: the existing hourly Inventory track is **V3 only**. Historical V2 LMS/Library/Hostel/Transport remains preserved and unfinished acceptance is not relabeled V3. V1 Academic/Student/Finance/HR is a maintenance-only engineering baseline.

## Canonical stock contract

The sole posted stock source remains `InventoryMovement` / `InventoryMovementLine`. A manual Issue draft may be reserved under serializable stock checks. Partial issue fulfilment is **not** an edit to a posted movement: it creates a separately numbered, posted `Issue` with `SourceType = ReservationIssueFulfillment`, `SourceId` pointing to the original reserved movement and immutable original line numbers stored on the child lines. The parent retains original requested `Quantity` and tracks `FulfilledQuantity` as a control amount. Active held quantity is requested minus fulfilled, per item, lot and location. Parent becomes `Fulfilled` only when all lines are consumed. Release cancels any remaining hold without reversing already posted child issues.

The API `POST /api/inventory/movements/{reference}/fulfill-issue` requires Inventory entitlement, TenantAdmin/Principal/InventoryManager role, a fresh rowversion, a unique command ID and positive six-decimal line quantities. Every source ID, campus location, tenant and line is revalidated inside a serializable transaction. Retrying the same committed command with identical lines returns its posted evidence; a reused ID with changed content or a reversed child fails closed. Parent partial fulfilment prevents the old full-quantity Post action. Posted child corrections use the existing approved compensating reversal, **not** a mutation of posted lines, and reversal never silently recreates a hold.

The active reservation register and bounded CSV show requested, fulfilled and still-held quantities in the item's base unit; unrelated units are not aggregated. Instructor/LMS and Procurement order approvals are not part of this module. Transfer dispatch partial fulfilment and expiry policy remain separate pending acceptance; do not misrepresent this as the full reservation lifecycle.

## Migration, rollback and release gates

`20261030201000_InventoryReservedIssuePartialFulfillment` adds a non-null `decimal(18,6)` fulfilled-control column with default zero, a database constraint `0 <= FulfilledQuantity <= Quantity`, and a tenant-leading source-reference index. Existing rows retain their original stock quantities. The Down migration refuses to remove this control column if any consumption has occurred: a deliberate data-retention/reconciliation decision is required before rollback, not an unsafe blind drop.

Release acceptance requires exact-SHA Release build/publish, EF model consistency, fresh/repeat disposable SQL migration, SQL race/IDOR/stock invariant tests, JS contract tests, browser bilingual/mobile flows and later institutional UAT. A passing build is not proof of migration or pilot acceptance. No production migration, live data operation or external integration is authorized by this implementation.

## Remaining reservation epic

- Explicit approved UTC expiry policy and immutable expiration audit, including reserve/expire/post races.
- Partial transfer dispatch/receipt policy, source lineage and remainder treatment.
- Dedicated campus-IDOR and concurrent release/post/reversal acceptance.
- Deployed browser/mobile, measured p95/p99 and institutional UAT.
