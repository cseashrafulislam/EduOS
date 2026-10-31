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
