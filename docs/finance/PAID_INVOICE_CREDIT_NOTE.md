# V1 institutional paid-invoice cash credit notes

## Source and accounting policy

- `StudentInvoice` is the immutable gross billing source once paid. No refund modifies its amount, status, journal, payment allocations, or paid timestamp.
- `Refund` is the approved refund request and payout authority. `RefundAllocation` retains the original receipt allocation and the immutable `IsCreditNote` historical classification.
- A cash payout always creates one posted `StudentRefund` journal: **Dr Student Receivable / Cr Original Cash-in-Hand**. It never initiates electronic payment.
- For each paid-invoice portion, the same transaction also creates one posted `StudentRefundCreditNote` journal for the refund: **Dr Original Fee Income / Cr Student Receivable**. Source income accounts and fee-head/program attribution are taken from the original posted invoice GL, not today's fee configuration. Exact-cent proration is deterministic.
- A partially paid invoice portion follows the existing receivable refund workflow and changes only its outstanding balance. Mixed paid/partially-paid allocations on one receipt are supported by one combined credit note for the paid portions.
- `Journal.SourceType = StudentRefundCreditNote` and `SourceId = Refund.Id` identify the immutable credit-note journal; `Refund.ReversalJournalId` continues to identify the cash payout journal. Each uses a separate deterministic journal idempotency key.

## Control points

- Tenant administration, different requester and approver, existing posted and unreversed original invoice/receipt journals, positive BDT cash amount, actual payout voucher and open accounting period are mandatory.
- Original invoice must be paid or partially paid; the payout rechecks receipt, allocation and GL provenance under a serializable transaction.
- Duplicate payout is a verified replay, not another financial posting. Reused evidence with differing dates/periods is rejected.
- The original income accounts must be active BDT income ledgers; the original receivable and cash accounts must be valid BDT asset ledgers. Historical exceptions require reconciliation, not an arbitrary replacement account.
- Pending refund requests reserve against current invoice capacity. Per-receipt and cumulative per-invoice credit limits are enforced before posting.
- No backfill rewrites old invoices or refunds. The additive migration gives existing `RefundAllocation.IsCreditNote` the historical default `false`.

## Read models and limitations

- The refund register shows the separate credit-note journal number. Student ledger preserves gross billed/paid totals and additionally reports credit-note total, net billed and net paid.
- Receivable and receipt reconciliation validate the credit-note journal evidence and reconcile gross receipt allocations, actual refunds and credited paid invoices separately.
- Bank/mobile-money payouts, prior-period statutory tax adjustments, retrospective credit-note issue without payout, and automated external cash settlement are not introduced by this cash-only workflow.
- Before declaring V1 production complete: rehearse this migration on a restored database, run SQL Server integration, test simultaneous cashier submissions, reconcile real historical data and complete institution-led finance UAT.

## Deployment and rollback

1. Deploy additive migration `20261030020000_V1PaidInvoiceRefundCreditNotes` before enabling the new payout path.
2. Verify successful SQL Server migration smoke and server-side access checks; reconcile trial balance and original receipt/document readbacks.
3. Roll back application code independently while retaining the additive column/index. Do not drop the column once any paid credit notes exist: that would discard historical classification. Reverse posted journal mistakes by approved compensating entries, never by deleting history.
