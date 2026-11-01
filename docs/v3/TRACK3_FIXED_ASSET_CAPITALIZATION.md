# V3 Track 3 — fixed asset capitalization and accounting acceptance

## 1 November 2026 — remaining-work implementation and test candidate

These changes build on the earlier implementation below. **Deployment signoff remains
conditional on exact-commit disposable SQL, authenticated browser UAT, and the Finance
owner's independent review.** The following are implemented workflow paths, not a claim
that a real bank transfer or physical return has been externally confirmed:

- **Supplier return and Finance credit note:** `/ProcurementSupplierReturns` and
  `/api/inventory/procurement/supplier-invoices/{invoice}/returns` let an authorized
  maker select a posted supplier-invoice line, record documented return quantity and
  Automatic/ManualFinal/AutoPlusManual credit, and submit an idempotent rowversioned
  request. A distinct Principal/TenantAdmin checks receipt, source valuation, open
  period, remaining quantity, pending payments and typed BDT mapping. Approval creates
  exactly one canonical inventory issue plus balanced Finance GL debits to payable or
  explicit supplier-refund receivable, credit to inventory and any variance. Returns
  reduce source-linked AP aging and settlement capacity; the entire invoice cannot be
  unwound while a posted credit exists. Rejected requests post neither stock nor GL.
  A refund **receivable is not a cash receipt**; any external refund settlement requires
  a separate evidenced Finance/bank entry. Posted supplier-credit reversals are not yet
  supported; correction requires Finance escalation and a future audited contra path.
- **Bank-statement reconciliation evidence:** Supplier payment instruction mapping now
  reads the existing Finance bank-reconciliation match, batch and clearing approval
  records. It distinguishes externally posted payment from matching/cleared bank
  evidence; settlement never invents a bank clear or silently creates a transfer.
- **Capitalization unwind:** A posted serialized unit can be independently requested
  and reviewed for an exact contra of its original Finance journal and a compensating
  canonical inventory reversal. Active custody, depreciation, impairment, disposal,
  pending competing corrections, invalid period/version, and already-reversed movements
  fail closed. The historical capitalization remains posted, original Journal/Book
  evidence stays immutable and the book is marked inactive after atomic unwind.
  Re-enrollment needs a new asset/source—not reuse of that historic unit key.
- **Prospective depreciation policy:** Maker/checker amendment with first-of-month
  effective date, typed mode, adjusted remaining useful life, salvage floor, explicit
  versions and review reason. Approved schedules apply only prospectively; earlier
  posted journal lines and the book's original cost/salvage snapshot stay intact.
  Retroactive overlaps, pending valuation/disposal and competing amendments fail closed.
- **Validation:** The new migration adds only `ProcurementSupplierReturnCredits` and
  `AssetDepreciationPolicyAmendments` with composite tenant foreign keys, audit columns,
  unique request keys and rowversions. Focused disposable SQL regressions cover partial
  supplier return GL/stock/aging, posted asset unwind, and revised policy with preserved
  past depreciation. Run fresh/repeat SQL migrations, automated tests and bilingual
  maker/checker browser UAT before enabling for a real campus.

### Remaining manual acceptance scenarios

1. In a disposable institution, physically return a posted invoice line after partial
   payment; independently validate supplier credit, stock issue, AP reduction and refund
   receivable without treating it as a bank receipt. Reject over-return and duplicate
   review; confirm source-trace and bank-cleared evidence on a matched statement line.
2. Capitalize a serialized receipt unit, then unwind a clean unit; ensure the original
   GL remains posted, exactly one mirrored GL and stock reversal exist, and the book
   becomes inactive. Check active custody or posted depreciation blocks unwind.
3. Post the previous month's depreciation, approve a future first-of-month policy,
   confirm the historic GL stays fixed and future schedule changes. Reject same maker,
   stale version, closed period, retroactive amendment and an overlapping future policy.
4. Finance owner signs journals and bank/refund policy. Verify mobile Bangla/English
   screens, tenant/campus isolation and export formula escaping using real user roles.


## 1 November 2026 — integrated Procurement + Asset Finance implementation candidate

This section describes the **new code awaiting exact-commit disposable SQL and user acceptance**;
the historic preflight-only sections below document the preceding baseline. Do not mark #55/#56
closed solely because this code builds.

- `/AssetAccounting` and `/api/inventory/asset-accounting/*` require both `INVENTORY`
  and `FINANCE` module entitlements. A campus-authorized Accountant/TenantAdmin submits;
  a **different** Principal/TenantAdmin verifies source, version, accounts and open period.
- Capitalization identifies a numbered unit of an independently approved goods-receipt
  line. A filtered SQL unique constraint prevents a second live claim on that unit or asset.
  The reviewer atomically posts the canonical **Inventory Issue**, one **AssetBook**, and
  an exactly balanced existing **Finance Journal**. The command stores source unit, book,
  movement and journal identifiers. Automatic, manual-final and auto-with-manual-correction
  cost policies require a reason, period, BDT account mapping and useful-life version;
  manual valuation differences receive explicit gain/loss GL lines.
- Subsequent monthly straight-line depreciation, signed manual impairment and custody-cleared
  disposal have individual submitted/posted/rejected states, stable client keys, optimistic
  versions, independent GL reviewers and current book-value checks. Impairment credits the
  original fixed-asset cost GL directly, while the book retains original cost and an
  explicit impairment row. Disposal reverses the remaining fixed-asset cost net of recorded
  impairments and accumulated depreciation, with explicit proceeds *clearing* (not a bank
  transaction), and gain/loss accounts. Asset/book are made inactive only on posted disposal.
- Depreciation/impairment/disposal corrections submit an **exact mirror** of the posted
  original journal for a separate reviewer; correction requires an open period, no later
  posted book events and no competing command. Original source evidence is retained.
  Capitalization itself is intentionally **not** reversed by a generic stock action:
  a unit-level unwind with custody and subsequent journal reconciliation needs its own
  strictly controlled acceptance path.
- Supplier settlement is separate: only an external bank-confirmation reference can move
  an independently approved payment journal to `Posted` in the existing Finance GL;
  a separately approved contra compensates without changing the recorded bank reference.
  Net payable aging and enterprise supplier trace account for valid posted contras. EduOS
  does not initiate a real bank transfer or reconcile the bank statement automatically.

### Manual tenant/campus UAT and go/no-go (still required)

1. Configure seven distinct typed BDT posting accounts, bank/payable accounts, open
   periods, campus access, two real users and module entitlements. Test 403 for denied
   maker/reviewer and 409 for stale versions and same maker/checker.
2. Receive an approved PO's asset-tracked serialized units; confirm both posted stock line
   and receipt details are the same item, location, campus, lot and reviewed price. Submit
   unit 1 twice with the same idempotency key; reject reuse with changed inputs, competing
   second asset/unit and incomplete evidence. Approve once; verify exactly one stock issue,
   one active book and one balanced posted capitalization GL journal.
3. Close the accounting period, exhaust stock, revoke source evidence or change GL mapping
   before approval. Every scenario must roll back **all three** postings, not leave a book
   without a journal/stock issue.
4. Request automatic month-end depreciation, then a documented manual impairment and
   disposal with proceeds clearing. Check current value, debits/credits, no active custody,
   no double-posting, no cross-tenant access and no posted future events. Approve an exact
   disposal contra; confirm the original posted journal remains immutable and the asset
   becomes available again. Check rejected review and superseded-key behavior.
5. Bank-confirm a supplier payment with external evidence, inspect net AP aging/source
   trace, reverse by independent full-value contra, and verify reopen of the original
   payable only after every paid settlement is compensated.
6. Run `.NET Release`, JavaScript contracts, disposable SQL fresh/repeat migrations,
   tenant isolation, backup-restore and authenticated bilingual/mobile browser flows.
   Review accounting journals with the institution's Finance owner before production.

**Not yet certified:** real bank-statement matching, supplier returns/credit-note source
flows, capitalization unwind after a posted book, depreciation policy changes after initial
booking, translated contextual Help Drawer and live-campus/finance-owner UAT. These require
separate acceptance and must not be described as completed merely by CI passing.

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
