# V3 Procurement — supplier invoice matching preflight (2026-10-31)

## Scope and accounting boundary

After the independently approved `ProcurementGoodsReceipt` is posted into the one canonical `InventoryMovement` ledger, an Accountant, Principal or TenantAdmin can open `/ProcurementSupplierInvoiceMatching`, search an **approved** campus Purchase Order and select its posted delivery lines. The protected endpoint is `POST /api/procurement/supplier-invoices/preview`. It returns a **read-only proposal**, not a registered supplier invoice, cumulative invoice-capacity reservation, payable, GL journal or payment clearance.

- Source validation is tenant/campus/authorized-store scoped. A guessed foreign receipt UUID cannot be previewed. Each selected receipt must be `Posted`, point to a real posted movement with `SourceType=ProcurementGoodsReceipt` and its exact source ID, store and complete line-level quantities, lots and unit costs; reversed stock is refused.
- Proposed invoice quantities are positive decimal(18,6), cannot exceed their selected receipt-line quantity and cannot repeat the same receipt line within one proposal. This does **not** promise prevention of cross-invoice overbilling: that requires durable invoice/line rows and a serializable cumulative match on the later posting transaction.
- `Automatic` proposes approved PO unit prices; `ManualFinal` takes a non-negative final price; `AutoPlusManual` accepts a signed correction. Four-decimal unit price, cent-rounded per-line BDT amount and substantive 10–800 character reasons for deviations from PO price are enforced server-side. Price variance against the **actual posted stock valuation** is reported explicitly for independent Finance review.
- Bilingual English/Bangla, responsive PO and posted-receipt pickers, accessible help toggle, CSRF, module entitlement, role, rate limiting, no-store and safe text-only DOM rendering.
- No changes to Finance V1 journals or Inventory V3 stock ledgers, no new schema or migration and no supplier invoice creation side effect.

## Next owned Procurement/Finance slice — explicitly still pending

1. Tenant/campus-owned SupplierInvoice and SupplierInvoiceLine persisted evidence with unique supplier invoice number, immutable supplier/PO/receipt-line snapshots, document dates, safe attachments and auditor history.
2. Serializable quantity exhaustion across *all* submitted/matched/posted invoices; duplicate/replay and cross-tenant/campus/role/reversal checks; maker-checker matching rejection/rework and independent Finance approval.
3. Configured account/campus/period validation, approved payable journal linked to the matched supplier invoice using the **existing** `Journal` / `JournalLine` and accounting reversal/payment contracts; price variance posting/reconciliation, no parallel GL or silent cash disbursement.
4. Exact-SHA SQL/browser acceptance, period-close, reversal, supplier credit-note, AP aging, backup/restore, then separately institutional UAT and measured p95/p99.

This preflight is a safe foundation for the next persistence/posting work; do not call Procurement or V3 complete because the preview passes.
