# Historical Cash-in-Hand Ledger Designation

Scope: EduOS institutional Finance only, one canonical posted general ledger. SaaS subscription billing is excluded.

## Authoritative data and approval

- `Accounts.IsCashInHand` is the **only** active cash-ledger classification. `BankAccounts` remains the bank mapping source; a ledger cannot have both designations.
- New, unused cash posting accounts are designated during standard Chart of Accounts setup.
- An existing unclassified account with any journal history can only be designated through `CashLedgerDesignationRequests`. A Principal or TenantAdmin submits specific evidence and a unique request ID. **A distinct TenantAdmin** independently approves or rejects it.
- Submitted proposals are immutable except the single review transition. The original posted journals, journal lines, periods, amounts, document numbers, and source references are never updated.
- Review rechecks current active BDT asset posting status, bank conflict, journal history, tenant and request rowversion **inside a serializable transaction**. Retried requests are idempotent; two pending proposals for the same account are database-rejected. Approval atomically marks the account and the audit request. Normal edits cannot reverse historical classification.

## Deployment and migration

1. Back up production SQL and verify restore before applying `20261104173000_AccountCashLedgerDesignation`, `20261104174000_BankLedgerAttributionIntegrity`, and `20261104180000_LegacyCashLedgerDesignationApprovals` in order.
2. Before the unique live bank mapping migration, investigate duplicate `BankAccounts` records using `WHERE IsDeleted = 0 GROUP BY TenantId, AccountId HAVING COUNT_BIG(*) > 1`. The migration **fails without deleting or remapping data** if duplicates exist. Resolve with independent accounting evidence and audited decisions, not automatic deduplication.
3. Deploy code and migrate SQL together in staging. Validate that the cash ledger request list is tenant-restricted, that the submitter cannot review, and that cash Book/Movement totals remain unchanged until a distinct checker approves.
4. Reconcile each proposed historic cash account to physical cash books, bank mappings and original vouchers. Do not infer cash solely from the account name. Record evidence in the reason field, and retain supporting records under the institution's records policy.
5. Test an as-of date before and after original posted activity; compare cash Book, consolidated movements, Trial Balance and journal/source drill-down. The designation changes *report classification*, never booked money.

## Cash Flow limitation

`Cash Movements` is a reconciled receipts, payments and internal transfer control, **not** a statutory Operating/Investing/Financing Cash Flow Statement. Those classifications require approved historical counterparty activity policies or journal-level allocation evidence. No account-name/type guesses or unreviewed automatic backfill are allowed.

## Operational safety

- If a reviewer rejects a proposal, the account remains unclassified; submit a new proposal with corrected evidence.
- If cash or bank mappings appear inconsistent, Finance reports fail closed. Do not bypass the constraints or mutate posted journals.
- Monitor 409 conflicts, pending proposals and migration/SQL integrity test results. SQL deployment and authenticated institution browser acceptance remain separate go/no-go gates.
