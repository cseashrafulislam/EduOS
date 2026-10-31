# V3 Procurement — goods receipts and inventory posting (2026-10-31)

## Delivered integration slice

`Approved Purchase Order → partial delivered quantities → maker-created Draft → maker Submitted → independent checker Approved or Rejected → one Posted InventoryMovement`.

- `ProcurementGoodsReceipts` / `ProcurementGoodsReceiptLines` preserve tenant/campus/store, approved PO price and quantity, maker, checker, evidence, nonce, rowversion, costing policy, and canonical stock-movement reference.
- Only an approved, non-cancelled purchase order with an approved supplier, live tracked items, and active authorized **campus-specific** store may be received. A generic cross-campus/null-campus store does not qualify.
- Positive partial quantities (up to six decimal places) are bounded against PO quantities cumulatively across **Submitted + Posted** deliveries. Submitted receipts hold remaining PO capacity. Rejected receipts release that capacity; a draft does not reserve it. Maker and checker checks are re-evaluated under serializable SQL transactions.
- Every mutation has independent nonce and optimistic rowversion. Create and review bind their replay keys to semantic SHA-256 fingerprints; conflicting replays receive HTTP 409. A checker cannot accept a receipt they created or submitted.
- Three checker costing policies: `Automatic` copies the approved PO unit price; `ManualFinal` requires a final positive unit cost per line; `AutoPlusManual` applies a signed correction per line. Changed valuations require 10–800 character line evidence. Each checker decision also requires evidence.
- A **trusted InventoryMovementService pathway**, unavailable to generic movement creation, posts one canonical `InventoryMovement` of type `Receipt` with `SourceType=ProcurementGoodsReceipt`, `SourceId=GoodsReceipt.Id` and full item/lot quantities/costs. SQL unique filtered provenance index enforces at most one movement per source; stock is not modified before posting.
- Migration `20261031190000_V3ProcurementGoodsReceipts` adds composite tenant foreign keys, SQL checks, immutable source-snapshot triggers, source-link index and guarded down migration. There is no destructive V1/V2 schema alteration.
- `/ProcurementGoodsReceipts` offers purchase-order lookup, campus store choice, draft submission, independent review, valuation, bilingual headings, paginated audit view and CSV export. API is protected by module entitlement, role policy, tenant/location filters, anti-forgery and rate limits.
- `SqlServerV3ProcurementPurchaseOrderTests.SQL_goods_receipt_partial_maker_checker_cost_modes_replay_and_stock_ledger` covers stock immutability preapproval, idempotent create/review, partial receipt totals, checker separation, cost modes, rejection/replacement, supplier PO cancellation lock and tenant isolation on disposable SQL Server.

## Deliberate accounting boundary

This is **quantity acceptance and Inventory valuation only**. Purchase-order approval is a commitment, not an invoice. Goods receipt does **not** book Accounts Payable, create Finance journal entries, authorize supplier payment or assert supplier invoice three-way matching. Supplier invoice capture and `PO ↔ receipt ↔ invoice` matching remain the next Procurement/Finance integration slice; the approved Inventory posting is its immutable stock evidence.

## Operator acceptance

1. Approve a supplier and PO through the existing requisition/PO maker-checker flow.
2. Inventory maker opens `/ProcurementGoodsReceipts`, searches the approved PO, selects the **same-campus** store, adds a partial delivered quantity and a detailed delivery reason.
3. Create the draft; confirm stock ledger did not change. Submit the draft and confirm PO remaining quantity is reserved for that submission.
4. Log in as a different authorized checker; accept with Automatic or a documented manual costing mode. Confirm one Posted movement and the same quantity/cost in the Inventory ledger.
5. Attempt a replay, overreceipt, cancellation after posting, self-approval, mismatched campus and cross-tenant read: they must not mutate stock.
6. Reject a separate pending receipt and confirm remaining PO capacity is released and no movement exists for that rejected receipt.

Automation: Node contract tests locally; .NET compile, full migrations, EF model drift and SQL acceptance through GitHub `CI` / `SQL Server Migration Smoke` after pushing the exact commit. Evidence belongs to the exact commit SHA, not an earlier baseline.
