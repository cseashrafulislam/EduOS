# Historical institutional journal integrity

Scope: Finance-owned institutional accounting. Platform SaaS billing is excluded.

## Period closure

The existing authorized Accounting Setup period workspace calls
`GET /api/finance/accounting/setup/periods/{id}/close-readiness`.
The close operation repeats readiness checks within its existing serializable
transaction. Legacy soft-deleted journal headers and journal lines remain
historical evidence. Both are counted independently with explicit tenant
and period predicates, even when visible posting lines still balance.
Closure is refused until reconciliation resolves the historical anomaly.

## Trial Balance

The authorized Trial Balance report fails closed when a posted or reversed
journal header or one of its lines is soft-deleted on/before the as-of date.
Draft-only tombstones do not prevent a valid Trial Balance. No historical
journal or line is restored, rewritten, deleted, reposted or reversed by
these read-side checks.

## Boundaries

The new checks reuse canonical Journal, JournalLine and AccountingPeriod
entities, existing DI/API/Razor/navigation and immutable ledger rules.
They require no migration. They do not repair other reports automatically;
Ledger Statement, Income Statement, Balance Sheet and Bank Book should be
audited separately for the same historical-tombstone reporting exposure.
SQL Server query-plan and migration smoke verification is required before
production rollout.
