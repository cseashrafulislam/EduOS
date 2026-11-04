# Institutional Accounting: verified architecture and reporting boundaries

## Canonical accounting source

- The only institutional financial ledger is `Journals` + `JournalLines` + `Accounts`, scoped by `TenantId`. Financial statement reports must read **Posted and Reversed** journals together because a reversal is an independent compensating journal. Draft/rejected/cancelled journals do not contribute to posted balances.
- Fee invoices/receipts, admissions, procurement supplier invoices/settlements, payroll, facility service bills and assets retain their **operational source/subledger history**. A source transaction must post to the canonical GL exactly once under its existing idempotent workflow; it must not be summed directly together with a GL entry in income, cash or balance statements.
- `BankAccounts.AccountId` is an explicit account mapping and may be used for Bank Book and Bank Liquidity. `Accounts` currently has **no explicit Cash-in-Hand, cash-equivalent, fund, cost-centre or cash-flow-activity classification**. An account name or account-code prefix is not authoritative for these classifications.
- Historical journals cannot be edited or deleted for reporting convenience. For incorrect postings use the existing controlled approval and compensating-reversal workflows; run source-to-GL reconciliation before any backfill.
- General Ledger now applies the same account-scoped, posted-journal integrity preflight used by its paginated activity audit. Income Statement additionally rejects malformed zero/negative/dual-sided posting lines and invalid group/non-postable accounts; a balanced grand total cannot justify invalid source lines.

## Existing and added reports

| Report | Source / eligibility | Status and correctness boundary |
| --- | --- | --- |
| General Ledger / Posted Activity | Posted GL journal lines, tenant-wide TenantAdmin | Existing; audit activity is paged and reconciled for the full selected period |
| Trial Balance | Posted GL through date | Existing; debits equal credits, detects invalid historical entries |
| Income Statement | Posted GL Income/Expense accounts | Existing; signed contra balances preserved |
| Balance Sheet | Posted GL Asset/Liability/Equity accounts | Existing; lifetime unclosed surplus is displayed separately |
| Bank Book, Bank Liquidity, Bank Statement Comparison/Reconciliation | Explicit BankAccount → GL mapping | Existing; liquidity excludes unconfigured physical cash and is not a classified cash-flow statement |
| **Posted Day Book** | Posted/reversed GL journal headers and lines | **Added**; pagination is by whole journal; full-period Debit/Credit totals stay equal across pages; page-only CSV |
| **Comparative Income Statement** | Existing independently reconciled income statement for two periods | **Added**; prior-only/current-only accounts included, signed delta calculated |
| **Comparative Balance Sheet** | Existing independently reconciled balance sheet for two as-of dates | **Added**; current and historical position reconciled independently; unclosed surplus distinct from equity |
| **Income / Expense by Campus or Journal Source** | Posted income/expense journal lines and their recorded campus/source attribution | **Added**; unassigned campus retained explicitly and totals must match the institution-wide income statement; it is not an independent per-campus balance sheet |
| Journal / Voucher Register | Existing AccountingJournals workflow | Existing link; includes drafts/approval/reversal states, and is deliberately not a second writable journal register |
| Student Fee Receivables Aging / Collection / Invoice Coverage / Due Calendar | Existing Fees source tables and GL reconciliation | Existing independent operational reports, linked from accounting workspace where relevant |
| Procurement Supplier Payables / Facility Supplier Bills | Existing Procurement and Facility source tables | Existing module-specific aging/settlement evidence; not automatically combined as one AP book |
| Payroll Reconciliation | Existing Payroll workflow | Existing independent report, linked to canonical posted GL |

## Open design decisions / release gates

1. **Statement of Cash Flows (Operating/Investing/Financing) and consolidated Cash Book:** not valid without an explicit tenant-scoped Cash-in-Hand/Equivalent account designation, journal classification policy including cash/non-cash and internal transfers, opening-balance migration and end-to-end cash reconciliation. Bank Liquidity must not be relabelled Cash Flow. Add additive schema/configuration and independent SQL transaction fixtures before offering these statements.
2. **Budget vs Actual / Fund / Department / Project statements:** require authoritative approved budgets, accounting dimensions and historical journal-line attribution; no guessed joins or account-code heuristics. Define dimensions on canonical journal lines and migrate older history conservatively, with 'unassigned' remaining visible.
3. **Consolidated AP aging across Procurement and Facilities:** each source currently has its own aging rules. Consolidation requires source-level outstanding-liability reconciliation, shared cutoff rules and strict deduplication of payments and GL movements. It is not a sum of both operational totals plus GL liability.
4. **GL posting coverage for every module:** execute source-to-posted-journal exception reconciliation at an exact database snapshot before declaring institution-wide totals complete. Missing or duplicate source postings must go to controlled correction; do not auto-post historical transactions.
5. **Quality:** independent .NET/SQL migration and authenticated tenant/campus browser acceptance, high-volume database paging and cross-tenant/role attempts still need CI and staging verification. JavaScript contract tests alone cannot constitute financial or production acceptance.

All new reports are read-only, require institutional TenantAdmin + FINANCE authorization, preserve the old GL interfaces/records, add no database table/migration, use bounded SQL pages where rows can grow large, and export formula-escaped CSV. Comparative reports inherit their source statements' historical soft-delete, currency and journal-balance checks.
