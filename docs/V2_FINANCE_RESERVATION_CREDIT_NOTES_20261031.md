# V2 Finance — reservation negative credit-note contract (2026-10-31)

> **Final code verification:** The implemented original-invoice negative credit-note settlement and its paid/unpaid reconciliation are included in [`ce0bb2e`](https://github.com/cseashrafulislam/EduOS/commit/ce0bb2e055f566616d81249e40af4b1d3a9b22e4), with [CI](https://github.com/cseashrafulislam/EduOS/actions/runs/41187500922) and disposable [SQL acceptance](https://github.com/cseashrafulislam/EduOS/actions/runs/41187500912) both successful. See the [V2 Final Development Baseline](V2_FINAL_DEVELOPMENT_BASELINE_20261031.md). The original production-signoff restrictions below still apply.

**Scope:** `Ashraful` only. This increment extends the existing V2 hostel upcoming-start reschedule and transport upcoming-rider extension Finance approval. No V1 schema or LMS/Library baseline changes.

## Signed Finance approval

- **Positive selected adjustment:** the existing draft StudentInvoice + approved unposted GL journal flow remains intact.
- **Zero selected adjustment:** no-adjustment attestation or the existing independent manual settlement route.
- **Negative selected adjustment:** an independent Finance reviewer chooses one posted, unreversed, *single-fee-head* original StudentInvoice (same tenant, campus, active student/enrollment), an open accounting period, and—if cash has already been allocated to that invoice—a valid student-credit liability account. The legacy manual posted-journal option remains available.
- The original invoice, receipt and payment allocation are **never edited** by a reservation credit note. The original income account is debited for the selected credit. Unpaid value credits the original student receivable. Any excess corresponding to already-paid value credits the selected liability account. No payout occurs.
- Finance approval stores a unique clearance-specific, *approved but not posted* `StudentReservationCreditNote` Journal, with `SourceId` linked to the original StudentInvoice. The approval source snapshot includes this evidence.
- Operations rechecks scope, original payment/refund evidence, balances, no other pending credit, journal line split, open period, source versions and clearance snapshot. Only a successful reservation modification transitions the same journal to **Posted**, inside the existing serializable Operations transaction. Failed changes cannot post it.

## Capacity and accounting example

An original invoice of BDT 200 with BDT 80 paid and BDT 120 gross unpaid gets an independently approved BDT 150 reservation credit:

| Journal posting | Debit | Credit |
| --- | ---: | ---: |
| Original fee income | 150 | — |
| Original student receivable | — | 120 |
| Student-credit liability | — | 30 |

**Effective receivable due = 0; student-credit payable = 30.** The original invoice continues to show its immutable historical `Total=200`, `Paid=80`, `Due=120` in storage. New Finance invoice API exposes `GrossDueAmount`, the credit split, and an effective `DueAmount=0` for current collection. Reconciliation separates total credit notes, reservation credits, student-credit liability and effective due. Aging and due-calendar reports exclude the cleared balance.

Additional credits use remaining original total and unpaid capacity, including prior *posted* reservation credit notes. An *approved* pending credit blocks competing automatic credits against that original. Cash refund and credit-note workflows refuse overlapping original invoices; refund payout requires a separate verified Finance workflow and is never inferred from a liability journal. Credit notes do not synthesize receivable payment allocations.

## Explicit safety limits

1. An invoice with multiple fee heads, fine origin, invalid/reversed GL or payment journals, any refund allocation, mismatched historical allocations, or ambiguous prior credits is **not automatically credited**. Finance must reconcile it manually.
2. A generic active BDT liability account must be explicitly selected for the paid portion. The reviewing institution is responsible for choosing its *student-credit payable* account; this change does not auto-create chart-of-account entries or move cash.
3. No account-credit redemption, bank/cash refund initiation, or automatic student-to-student credit transfer is introduced. Posted student-credit liabilities remain recorded until separately approved Finance settlement.
4. Closing the selected accounting period between Finance approval and Operations consumption blocks posting. A stranded approved credit journal must be reconciled by Finance; automatic deletion/rewriting is not performed.
5. SQL Server integration, restored-data UAT and business sign-off must be performed before claiming live Finance completion. This contract is not certification of all V2 features or a production migration.

## Verification

- Build and test evidence belongs to the exact pushed commit; do not substitute a previous green CI run.
- Unit cases cover tenant/clearance-scoped idempotency, paid/unpaid credit arithmetic, amount bounds, posted-only balance aggregation, and original invoice immutability.
- Existing V2 Finance and cross-module regression suites remain required. Production-specific SQL Server transaction/index verification requires `EDUOS_SQL_AUTH_TEST_CONNECTION` in a disposable test environment.
