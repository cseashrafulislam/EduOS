# V2 Library — loss/damage liability and institutional Finance posting

## Scope and ownership

`Library/Conditions` records a physical lost/damaged copy incident and closes its outstanding loan without forging a return or creating a receivable. `Library/Liabilities` is a **separate institution-administrator-only assessment and Finance workflow**. The library charge is owned by `LibraryLiability`; it is **not** a `StudentFine` and does not mutate tuition fine rules or existing posted invoices.

1. At direct issue or reservation fulfillment, `BookIssue.ReplacementPriceSnapshot` captures the then-current catalogue replacement price. Existing historical loans are left `NULL` (unknown)—never automatically backfilled from current prices.
2. A copy must still be Lost or Damaged with a matching closed incident. One active enrollment in the same physical branch campus is required. Lost charges must exactly equal the issue-time snapshot; damaged charges may be less than or equal to it, both positive two-decimal BDT. An administrator records the reason with a unique request ID.
3. Another institution administrator reviews using the assessment rowversion. Waive requires a reason and does not create an invoice. Approve requires an open current accounting period and a dedicated, active `LIB-` fee head with distinct BDT receivable asset/income accounts. The fee head may not be used by tuition structures/installment plans.
4. Approval atomically creates one regular `StudentInvoice` and `StudentInvoiceLine`, one posted double-entry `Journal` (debit receivable, credit income), and links the invoice to the immutable library assessment. Standard Finance student payments/allocations update `PaidAmount`/`DueAmount`, which the Library register reads live. No parallel library payment balance is stored.
5. The invoice/journal/source links are immutable after posting. Finance's authorized adjustment/refund policy applies; do not delete the invoice, edit its journal or reset a posted liability. A recovered copy is not proof of an automatic financial reversal.

## Integrity and privacy

- Tenant-composite FKs to the loan, enrollment, campus and invoice; unique `(TenantId, BookIssueId)`, `(TenantId, ClientRequestId)`, `(TenantId, PublicId)`, and one-to-one posted invoice linkage.
- SQL CHECK ensures positive monetary amounts and reviewed states match invoice linkage. Writes use serializable transactions, SQL uniqueness, maker-checker checks, and `rowversion` concurrency.
- The new API, list, CSV export and browser page all require module `LIBRARY`, authenticated `TenantAdmin`, and the current request's tenant. The administration CSV is paged, protected from spreadsheet formulas and never publicly cached.
- `LibraryLiability` and the existing `StudentInvoice`, `Journal`, `JournalLine`, `PaymentAllocation` and audit records are the authoritative financial evidence. Historic posted entries are returned even if a source copy/book was later soft-deleted.

## Deployment and operational gate

1. Back up/rehearse on a restored database. Apply additive migration `20261030144408_V2LibraryLiabilityPosting` before deploying the new API/UI. It adds a nullable issue-time price snapshot and the new liability table/indexes; there is no speculative data rewrite. Confirm the V3 migration `20261030201000_InventoryReservedIssuePartialFulfillment` remains in sequence.
2. Configure a dedicated `LIB-` fee head and income/receivable accounts, with an open accounting period, for each applicable tenant. Use catalogue replacement prices **before issuing** copies. Pre-existing lost/damaged incidents with no issue-time price need explicit audited historical evidence and a separately designed remediation process; this module deliberately does not guess.
3. Test a lost and damaged loan, duplicate submissions, conflicting reviewer updates, same-actor rejection, cross-tenant ID probing, waiver, unpaid and paid invoice statements, as well as Finance period-close and compensation/reversal procedures. SQL tests use `EDUOS_SQL_AUTH_TEST_CONNECTION` when available; without it their guarded integration scenarios do not execute.
4. Inspect production SQL plans for the tenant-leading liability indexes and high-volume register paging, reconcile GL debit=credit, restore/rollback from backup in staging, and monitor duplicate-key/concurrency errors.

## Remaining release-gate decisions

This adds **loss/damage charge-to-invoice posting**, but does not by itself certify the entire Library V2 module as 100%. Subsequent implementation is documented in `../V2_LIBRARY_OVERDUE_REVERSAL_HANDOFF_20261030.md` (overdue policy, approved unpaid reversal) and `../V2_LIBRARY_EMPLOYEE_CIRCULATION_HANDOFF_20261030.md` (campus-scoped employee borrowing). Paid-charge Finance correction, restored-SQL concurrency/load/UAT and production Windows Defender acceptance still require independent approval. No workflow is deemed accepted merely because a UI button or test exists.
