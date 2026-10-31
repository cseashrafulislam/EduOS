# V3 Procurement — source-linked gross payable aging

The supplier payable aging panel on the existing invoice matching page reads only **current posted** Procurement supplier invoices with a matching posted, tenant/campus-scoped V1 Finance Journal and its exact AP liability credit. It does not mutate GL, stock, invoices, payment records or bank accounts.

The due-date reference controls days overdue (not a historical journal cutoff). Posted invoices with a pending reversal remain visible and flagged until the independent contra is posted. Reversed/rejected/draft invoices are excluded. Tenant/campus entitlement is enforced by the existing invoice visibility service on every page and CSV request; paging is 1–100 and CSV is capped at 500 records, with spreadsheet-formula escaping.

**Critical limitation:** This is gross posted AP exposure, **not a certified outstanding/unpaid amount**. Unlinked manual payments and bank disbursements are deliberately not deducted. Settlement, payment reversal, cash/bank reconciliation and invoice credit/return remain separate unimplemented acceptance items. No payment execution is authorized by this report. Complete source-linked settlement before using these amounts as disbursement instructions.

Acceptance requires exact-SHA Release CI, SQL Server integration (posted vs reversed, aging and tenant isolation), mobile bilingual/browser, authenticated HTTP IDOR/CSRF, representative p95/p99 and institutional UAT. Do not mark the whole Procurement module development-complete from this read-only slice.
