# V1 — Overdue daily fine contract

Daily fine is an **explicit, one-time assessment** of a configured BDT-per-day rate against one previously issued, unpaid student invoice. It is not a periodic job and does not automatically accumulate without administrative action. Fixed fines remain supported unchanged.

1. A tenant administrator creates an active fine rule with `IsDaily=true`, a strictly positive 2-decimal `Amount` (BDT/day), `GraceDays`, and a dedicated fee head with income and receivable GL mappings.
2. The assessor selects one enrolment, one **original posted tuition invoice**, and a non-future `FineDate`. Eligible source invoices must be unpaid or partially paid, have nonzero current due, an intact posted source journal and no fine-invoice lineage or posted journal reversal. No tenant-foreign reference is accepted.
3. Calculate `OverdueDays = FineDate - SourceDueDate - GraceDays` (calendar days; the source due date itself is day zero). `1 <= OverdueDays <= 3650`. Assessment is `Rate * OverdueDays`, capped at BDT 999,999,999,999.00 and recorded with due-date, rate and overdue-day snapshots. No overdue day means no charge.
4. The database allows at most **one daily fine per original invoice and fine rule** (unique tenant/source/rule index) even if the client changes its idempotency key or retries concurrently. A rule's amount, type, fee head and grace days cannot change after use. The original invoice is never mutated by assessment.
5. A **different** institution administrator confirms the assessment in an open accounting period. Posting creates its own immutable fine invoice, invoice line and balanced debit-receivable/credit-fine-income journal; posting is idempotent against the fine id. The original tuition invoice remains unchanged. An already billed fine cannot be administratively waived without a separately approved accounting credit/reversal workflow.
6. The student register and fine register expose the original invoice identity and overdue days. Posted totals only recognize an actual posted fine journal.

## Migration and rollout

`20261030040000_V1DailyFineAssessment` is additive: nullable source invoice, due-date snapshot, overdue days and daily-rate snapshot columns; tenant-aware source FK, filtered unique tenant/source/rule index and a consistent-nullability check. Existing fixed fines remain null in every new column. **Do not** apply production migrations on app startup. Back up the database, run idempotent migration SQL in a restored staging copy, check old fine balances, index-creation duration and two-admin posting, then deploy compatible API/UI code. A schema downgrade is possible only when no daily fine data remains: down migration drops these new columns and would lose those snapshots, so prefer forward correction after production use.

## Intentionally separate follow-up

Reassessing the same rule/source for additional days is refused: the one-time assessment is historical, not a running balance. A new policy for incremental fines, late payment cutoffs, refunds or posted fine credits needs a separately audited adjustment workflow; it must never overwrite the earlier fine/invoice/journal.
