# Procurement V3 — persistent supplier invoice and canonical AP handoff

Date: 2026-10-31. Branch: `Ashraful`. The supplier-invoice/AP **engineering slice passed both exact-commit checks** at [`ea8c2a9`](https://github.com/cseashrafulislam/EduOS/commit/ea8c2a9a2953f7757f4e8f8d31003fd5d6822c6b): [full CI](https://github.com/cseashrafulislam/EduOS/actions/runs/41203617372) **SUCCESS** (build, Node/.NET tests, EF model-drift, SQL script and publish) and [disposable SQL Server migration/acceptance](https://github.com/cseashrafulislam/EduOS/actions/runs/41203617295) **SUCCESS** (fresh/repeated migrations, source-link tests, financial evidence and backup/restore). Representative browser/production UAT and owner go/no-go remain separate. **This is not a 100% Procurement closure certificate.**

## Ownership and workflow

1. Inventory's approved purchase order and independently posted Procurement Goods Receipt remain the authority for ordered/accepted quantities. Its posted, non-reversed canonical `InventoryMovement` is the only stock fact. `/ProcurementSupplierInvoiceMatching` offers a read-only three-way pricing/quantity preview without saving anything.
2. An authorized Accountant/TenantAdmin creates a supplier invoice draft with the supplier's original invoice number, invoice/due date, PO, posted receipt lines, quantity and supported `Automatic`, `ManualFinal`, or `AutoPlusManual` prices/reasons. The tenant+supplier+normalized invoice number and tenant+client request ID are unique, including historic records. Receipt/PO/stock value snapshots are immutable in SQL.
3. That maker submits the draft. Inside a serializable transaction the service rechecks PO approval, same-campus supplier/receipt/lot, posted stock ledger and absence of receipt reversal; **Submitted+Posted** invoices reserve cumulative accepted quantity per receipt line. The second invoice cannot bill a previously reserved unit. Drafts do not reserve quantity. Conversely, Inventory's canonical stock reversal now rejects the goods receipt while an active submitted or posted supplier invoice claims any of its lines.
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

## Approved-but-unposted settlement approval revocation (2026-10-31)

A separate Finance reviewer can rescind an **Approved**, still **unposted**
settlement instruction through the existing `/settlements/{instruction}/review`
endpoint (`approve=false`), using its current rowversion and a documented
reason. The canonical Journal transitions to `Rejected`, releasing its reserved
payable amount without posting GL or executing any bank transfer. Original
approval actor/time are retained, and the rescinding reviewer and reason are
recorded in `UpdatedBy/UpdatedAt` and the bounded Journal description.
Maker self-revocation and stale repeated decisions fail closed. The existing
Enterprise trace recognizes rejected instructions and excludes their amounts
from reserved exposure; a new source-linked instruction can be submitted.
This is NOT settlement payment execution or reconciled net payable acceptance.

## Maker withdrawal of submitted settlement instruction (2026-10-31)

An original instruction maker may withdraw their own **Submitted**, not-yet-approved
and unposted supplier settlement instruction using
`POST /api/procurement/supplier-invoices/{invoice}/settlements/{instruction}/withdraw`.
The action requires Finance/Inventory entitlements, Accountant/TenantAdmin maker
access, matching tenant/campus/source, a rowversion, a nonempty idempotency UUID,
and a documented 10–400-character reason. Serializable transaction and
canonical Journal state/line validation protect competing review and withdrawal.
Exact retries return the same rejected instruction; a changed reason or key
cannot replay the withdrawal. The original instruction remains in the Journal
audit trail with actor, timestamp, reason and request key; the payable reservation
is released because rejected instructions do not count as active commitments.
No AP, stock, GL cash or bank transfer is posted. Approved instructions can
only be rescinded by the independent checker via the existing review contract.
SQL regression covers non-maker denial, exact retry, mismatched replay,
stale checker approval, released reservation and unchanged posted payable/stock.
The bilingual responsive workspace exposes a maker-only withdrawal dialog.
This is **not** payment settlement or a reconciled unpaid balance.
