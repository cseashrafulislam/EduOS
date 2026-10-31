# Procurement V3 — persistent supplier invoice and canonical AP handoff

Date: 2026-10-31. Branch: `Ashraful`. Engineering code awaiting exact-commit CI and disposable SQL acceptance, representative browser/production UAT and owner go/no-go. **This is not a 100% Procurement closure certificate.**

## Ownership and workflow

1. Inventory's approved purchase order and independently posted Procurement Goods Receipt remain the authority for ordered/accepted quantities. Its posted, non-reversed canonical `InventoryMovement` is the only stock fact. `/ProcurementSupplierInvoiceMatching` offers a read-only three-way pricing/quantity preview without saving anything.
2. An authorized Accountant/TenantAdmin creates a supplier invoice draft with the supplier's original invoice number, invoice/due date, PO, posted receipt lines, quantity and supported `Automatic`, `ManualFinal`, or `AutoPlusManual` prices/reasons. The tenant+supplier+normalized invoice number and tenant+client request ID are unique, including historic records. Receipt/PO/stock value snapshots are immutable in SQL.
3. That maker submits the draft. Inside a serializable transaction the service rechecks PO approval, same-campus supplier/receipt/lot, posted stock ledger and absence of receipt reversal; **Submitted+Posted** invoices reserve cumulative accepted quantity per receipt line. The second invoice cannot bill a previously reserved unit. Drafts do not reserve quantity.
4. A different Finance reviewer (TenantAdmin/Principal/Accountant with allowed campus) independently approves or rejects. Approve requires an accounting period open for the invoice date, an active BDT Inventory Asset account, AP Liability account and, for nonzero price differences, an Expense account. One canonical `Journal` with tenant/source uniqueness and immutable matched source linkage books `Dr Inventory stock value + Dr/Cr price variance = Cr AP invoice amount`. No second GL, stock or cash ledger is created. Reject has no GL write.
5. Reversal maker requests a compensating submitted `Journal`, with mirror-image debit/credit lines, eligible open reversal period, reason and optimistic row-version. A separate reviewer can reject or post it after comparing every line and validating that original AP has not been settled through an explicitly linked payable payment. The original invoice/Journal are never edited or deleted; corrected bills require a fresh supplier invoice identity and delivery-capacity revalidation.

## API and audit surfaces

- `POST /api/procurement/supplier-invoices/preview` stays read-only; `POST /api/procurement/supplier-invoices` creates a draft.
- `POST /api/procurement/supplier-invoices/{reference}/submit` and `.../review` enforce separate maker/checker, request-key replay and versioning.
- `POST /api/procurement/supplier-invoices/{reference}/reversal` and `.../reversal/review` operate on the canonical Finance contra; `GET .../options` returns active period/account selectors.
- `GET /api/procurement/supplier-invoices` offers tenant/campus-aware state/PO filter and bounded paging; `GET .../{reference}` returns source + Journal references; `GET .../export` is bounded to 500 records with spreadsheet-formula-safe CSV.
- API enforces Inventory plan, role, rate-limit and anti-forgery for writes. No cross-tenant source identity may be followed.

## Deployment and non-destructive rollback

- Migration `20261031210000_V3ProcurementSupplierInvoices` is additive: supplier bill headers/lines, tenant composite foreign keys, uniqueness and immutable-source SQL triggers, plus a source-unique filtered index on the existing `Journals` table. Run reviewed idempotent SQL against a restored copy first. No startup auto-migration and no live SQL mutation without approved deployment.
- The migration's `Down` deliberately refuses to drop nonempty supplier invoice history. A deployed site can disable the new UI/route through release controls while preserving posted history. Review GL, AP, invoice quantities, procurement receipts and audit before an exceptional rollback or backfill.

## Required acceptance before closing Procurement

- Exact commit: build, EF no-model-drift check, all Node/C# tests and disposable SQL integration (part invoice, duplicate number/replay, over-billing hold, maker-checker, tenant isolation, balanced GL, contra and stock unchanged).
- Test high-contention concurrent submit/review, timeouts and replay; demonstrate one Journal per bill and no overbilling in the real database.
- Browser/mobile Bangla/English, keyboard dialogs, CSRF failures, campus authorization and safe CSV export.
- Reconcile supplier cash payments, bank disbursement and already-paid invoice corrections through a separately approved *source-linked* canonical Finance settlement path. This implementation records the supplier payable but **does not create or send a supplier payment**. An unrelated/manual Finance payment is not sufficient provenance to certify automatic reversal eligibility; until linked settlement is available, review payable settlements out-of-band and disable reversal operationally on paid invoices.
- Representative institute UAT, reconciliation of migrated real records, load, backup/restore, security approval and production go/no-go remain separately required.
