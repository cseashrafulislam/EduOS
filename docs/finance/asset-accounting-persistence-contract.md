# Institutional asset accounting — shared persistence contract (#56)

**Schema:** additive EF migration `20261027213000_AssetAccountingPersistence`; canonical `Asset`, `AssetAssignment`, `Journal`, `JournalLine` and `Account` are not renamed or rewritten. There is **no historical backfill**: books must be explicitly approved for existing assets before depreciation. This migration adds no monetary postings and is not a complete Finance workflow.

## Canonical aggregate contract

- `AssetBook`: tenant+asset unique active book, mandatory campus, in-service date, explicit method, useful-life months, cost basis, salvage, policy version and six optional tenant-bound chart-of-accounts mappings. No guessed defaults/rates. Finance must validate method, accounts, ownership and campus before activating a book.
- `AssetDepreciationRun`: campus+accounting period, period end, unique tenant request GUID, approval/posting state and optional canonical Journal/reversal references. `AssetDepreciationLine` links run and book; a unique tenant+book+period-end active index prevents double depreciation across runs. Posted lines and runs must be immutable; reversal must be a new Finance-approved journal/record, not an edit.
- `AssetImpairment`: tenant-scoped request key, book, effective date, amount, approval and journal/reversal provenance. `AssetDisposal`: unique tenant+book active event, proceeds, carrying value, gain/loss, approval and journal/reversal provenance.
- All new entities inherit tenant query filtering, immutable audit provenance and SQL Server RowVersion from `BaseTenantEntity`. Every canonical parent FK includes TenantId; nullable approval actor FK is global ApplicationUser. Unique filters reference only ordinary `IsDeleted`, not computed columns. SQL constraints reject negative costs, invalid salvage/life, and negative impairment/disposal proceeds.

## Required Finance/Inventory service contract (not yet implemented)

Finance owns book approval, period close, depreciation calculation/preview, approval, posting, impairment, disposal and approved reversal. Inventory owns the existing Asset Lifecycle UI and custody returns. Under one short SQL Server SERIALIZABLE transaction, check tenant/campus and accounting-period state, Asset/Book RowVersion, unreturned `AssetAssignment` rows, journal mappings, and existing tenant+request/asset+period uniqueness. Acquire locks in deterministic tenant/asset/period order. Create canonical `Journal` and `JournalLine` via Finance posting service and record run/line/disposal linkage atomically; never post through a second ledger. For cross-service transactions use existing outbox and reconciliation, never claim exactly-once without a durable receipt. Repeat the same request must replay the original outcome; changed payload must return 409. Never mutate a posted journal or disposal; use an approved reversal and preserve history.

**Status:** persistence and SQL schema regression only; Finance's service/API/Razor/report workflows and two-DbContext posting races remain outstanding. No new CRUD screens for individual lines or audit records are intended.
