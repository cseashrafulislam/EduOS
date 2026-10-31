# V3 Track 3 — fixed asset capitalization and accounting acceptance

## Completed: read-only capitalization source preflight

The existing Asset Lifecycle workspace now offers a per-asset, tenant/campus/role-scoped
**Capitalization evidence** view and bounded CSV (25 candidate receipt lines).
Only approved posted procurement goods receipts with a still-posted canonical stock movement
and matching campus/asset-tracked item are shown; reversed movements are excluded.
Book presence and configured cost are displayed without claiming an approved GL posting.
No source-to-unit binding is inferred from matching inventory item, and no new ledger
or inventory balance is created. CSV formula cells are escaped.

This is **evidence discovery, not capitalization approval**. Historic manual assets may have
no matching goods receipt. The status deliberately remains ReviewRequired even with candidates.

## Pending owned acceptance

- [ ] Approved procurement GR line + unit/serial allocation provenance, immutable unique
  source binding, cost-basis policy/version and independent maker-checker capitalization;
  coordinate posted inventory-to-asset reclassification with canonical V1 Finance GL owner.
- [ ] Period-locked, configurable depreciation preview/approval/posting and atomic journal
  reference, idempotency, reversal, rowversion/concurrency and SQL Server integration tests.
- [ ] Audited impairment and disposal with custody-return guard, proceeds, gain/loss,
  canonical journal posting and approved correction.
- [ ] Reconciliation/aging reports and production-ready mobile bilingual UX with
  authorized bounded exports and end-to-end SQL/browser acceptance.

Existing AssetBook, depreciation run/line, impairment and disposal persistence is **not**
evidence that those workflows have been completed. Issue #56 remains open.

## Accounting integrity safeguard (subsequent Track 3 delta)

An asset with any non-deleted AssetBook now locks its source tag, campus,
category, tracked item, location, purchase date and registered purchase cost,
**even without custody history**. A prior custody assignment also retains this
lock. Name/serial metadata remains editable with rowversion; unassigned assets
without a book retain normal editability. A disposable SQL regression covers
both paths and rejects protected edits with HTTP 409. This safeguard does **not**
create an AssetBook, capitalize an acquisition, change a GL balance, or define a
valuation-correction policy.

## Track 3 source-provenance hardening (31 October 2026)

A receipt header alone is no longer sufficient for a capitalization candidate. The
preflight now requires the matching **posted canonical InventoryMovementLine** (same
tenant, receipt-linked movement, receiving location, line number, tracked item,
quantity, lot/batch and reviewed unit cost). The posted stock movement reference is
shown in the bilingual evidence table and bounded CSV for follow-up audit.
This fails closed for missing or altered stock lines, while preserving the existing
no-posting boundary. A focused disposable SQL regression covers correct evidence,
tampered unit cost, lot, location and cross-tenant IDOR. No Inventory posting,
asset book or Finance GL write path was modified.

## Independent GR approval provenance (31 October 2026)

Capitalization candidates now fail closed without recorded GR submission, distinct
maker/submitter and checker, independent review request and reason, chronological
submission/review timestamps, approved Automatic/ManualFinal/AutoPlusManual
valuation policy and a canonical posted stock movement by the checker.
SQL tampering regression covers reviewer, valuation, review request and posting actor.
This is not unique receipt-unit allocation, AssetBook capitalization or Finance posting;
no Procurement/Inventory posting implementation, stock ledger or GL was modified.

## Approved purchase-order provenance guard (1 November 2026)

The read-only capitalization candidate now also requires the canonical approved BDT
purchase order, its same-tenant receipt-linked PO line, matching campus and receiving
location campus, tracked item, approved PO quantity and original unit-price snapshot.
This prevents a valid posted stock line from masking a tampered or mismatched purchase
order source. Disposable SQL regression verifies that SQL trigger 51121 rejects changes to the
receipt's immutable PO price snapshot, then changes/restores the approved PO
line price and PO state in disposable SQL to verify candidates fail closed. Historical posted stock, AssetBook and Finance journals are unchanged.
**This remains preflight only:** no unique serialized receipt-unit binding, cost
approval, capitalization journal or depreciation posting has been implemented.
