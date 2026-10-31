# V2 Finance integration — exact-code QA handoff (2026-10-31)

**Branch:** `Ashraful`  
**Verified code commit:** [`aff7a72c2365d579f22271ffd1794761b6850b92`](https://github.com/cseashrafulislam/EduOS/commit/aff7a72c2365d579f22271ffd1794761b6850b92)  
**CI:** [SUCCESS, run 41175721344](https://github.com/cseashrafulislam/EduOS/actions/runs/41175721344)  
**Disposable SQL Server migration and acceptance smoke:** [SUCCESS, run 41175721313](https://github.com/cseashrafulislam/EduOS/actions/runs/41175721313)

## Completed and verified scope

- Existing tenant/campus-configurable `DailyProrated`, `FixedMonthly`, `Manual`, and manual override calculations continue to determine the signed Finance adjustment. Neither student nor institute counts are fixed in code.
- Finance review of eligible positive hostel pre-arrival reschedule and transport upcoming-rider extension adjustments now optionally prepares an institutional `StudentInvoice` (draft) and balanced `Journal` (approved but unposted), using one configured active fee head, active BDT income/receivable accounts and an open accounting period.
- An independently reviewed, single-use Finance clearance is required. The original requester may not approve. Source versions, campus, student/enrollment identity, deterministic clearance-specific idempotency keys and complete invoice/journal provenance are checked.
- Successful Operations consumption issues the invoice and posts the journal **in the same serializable transaction as the actual reservation change**; failed change/version conflict leaves both unposted. Closing the accounting period between approval and consumption blocks posting.
- Existing independently approved posted-manual-journal settlement is preserved, including signed negative period adjustments and zero-adjustment attestations. No cash payout is automatically issued.
- Finance UI exposes Auto Invoice (optional), open accounting period and fee head. Existing manually posted settlement is still selectable.
- SQL Server acceptance test covers automatic positive transport extension, independent approval, failed-version replay, closed-period block/reopen, successful posting, exact GL balance, and idempotent replay. `node --test EduOS.Tests/JavaScript/*.test.cjs` passed locally (314/314); full .NET CI and SQL smoke passed at the exact commit above.

## Not completed: do not claim 100% Finance or start phase 2 as if this is resolved

1. **Automatic negative credit note and original-invoice settlement** are not implemented. Existing V1 Finance credit notes are tied to an independently approved cash refund, and posted paid invoices are immutable. A new automatic credit requires explicit original invoice links, prior credit capacity checks and safe behavior for paid-versus-unpaid amounts. Do **not** create a parallel ad-hoc credit ledger or silently modify a posted invoice.
2. **Automatic cash refund is not permitted** by this integration. Continue to require the existing Finance refund request, independent approval, actual payout evidence and appropriate credit-note journal.
3. Broader institute-owned UAT, real restored-data checks and user sign-off remain outside this code-level acceptance. These are phase 3, not grounds to reimplement accepted LMS/Library functionality.

## Sequencing rule from owner

Finish the remaining **V2 Finance-only** credit/settlement contract before moving to **phase 2: Hostel and Transport outstanding operations**. Only then conduct **phase 3: Final Acceptance**. Never restart or rebuild the already verified LMS/Library baseline. This handoff records a tested increment; it does **not** certify V2 100% complete.
