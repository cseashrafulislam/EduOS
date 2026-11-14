# EduOS Unified UX — Lane D: Finance, Inventory, Communication & Audit

Source: [canonical route ownership](../UX_ROUTE_OWNERSHIP_20261114.json). Branch: `Ashraful`. This is the **page-by-page UX implementation ledger**. Existing checked routes may have exact-SHA Final QA evidence recorded below. Unchecked routes are not yet formally credited UX Done under the new process; a previously built page should be audited and reused, not reimplemented without a gap.

**Owner override — UX FIRST (2026-11-14):** Complete **all applicable UX implementation before the separate Final QA phase**. Work in canonical menu order; finish the first unfinished page's Razor/JS, backend/API where needed, backend-generated system Codes, compact responsive design, bn/en Help, shared AI/Voice and security-safe flows, then mark it **[x] UX Done** and immediately move on. Finish all pages of a menu before the next menu. Do not hold UX implementation hostage to expensive SQL/Chromium/browser Final QA. Do lightweight authoring review/syntax/compile checks where practical. Keep already verified routes intact, but recognize historic QA PASS is limited to the tested source SHA.

**Two status tracks:** checkbox **[x] = UX Done only**, not a claim that later Final QA has passed. Track `Final QA: Not Run / historical exact-SHA PASS / later PASS / FAIL` separately in the final-phase register. For every newly UX Done page, record route, code commit, changed files and deferred QA. No fake PASS, no test deletion and no waiver of tenant isolation, audit, financial/inventory or approval safety. Fix discovered critical defects during implementation. The 5 scheduled UX automations remain **OFF** per owner; this policy change does not enable them.

**Design standard:** One shared title, no top gaps, duplicate headings or decorative Refresh; practical mobile-friendly create/edit/list/report UI; multilingual Help, shared context-aware AI/Voice only where relevant; immutable server-generated internal Codes; reuse canonical domain data and backend validation. After all UX routes are done, run the full combined .NET/JS/SQL/Chromium/tenant/security/accessibility/locale/retry/report QA and repair any issues before release.

**Assigned canonical menu pages: 39.** Related subpages are included in each route's verification, not counted again as separately assigned menu entries.

## management:finance (26)

- [ ] `/FeeDiscountRules/Index`
- [ ] `/AdmissionCashReceipts/Index`
- [ ] `/FeeReceivablesAging/Index`
- [ ] `/FeeHeadAccounting/Index`
- [ ] `/AccountingSetup/Index`
- [ ] `/AccountingJournals/Index`
- [ ] `/InstitutionBudgets/Index`
- [ ] `/AccountingReports/Index`
- [ ] `/FeeOperations/Structures`
- [ ] `/FeeOperations/Billing`
- [ ] `/FeeOperations/Ledger`
- [ ] `/StudentInvoicePosting/Index`
- [ ] `/StudentPaymentPosting/Index`
- [ ] `/FeeDueCalendar/Index`
- [ ] `/FeeInstallmentProjection/Index`
- [ ] `/FeeInstallmentPlans/Index`
- [ ] `/FeeInvoiceCoverage/Index`
- [ ] `/FeeDiscountAttributionReview/Index`
- [ ] `/FinanceAdjustments/Policies`
- [ ] `/FinanceAdjustments/StudentAdjustments`
- [ ] `/FinanceAdjustments/FineRegister`
- [ ] `/FinanceAdjustments/Refunds`
- [ ] `/FinanceAdjustments/HostelClearances`
- [ ] `/FinanceAdjustments/TransportClearances`
- [ ] `/FinanceAdjustments/ReservationPreflight`
- [ ] `/FinanceAdjustments/DiscountAwards`

## management:inventory (7)

- [ ] `/InventoryCatalog/Index`
- [ ] `/InventoryMovements/FacilityFinance`
- [ ] `/AssetLifecycle/Index`
- [ ] `/InventoryMovements/Transactions`
- [ ] `/InventoryMovements/Adjustments`
- [ ] `/InventoryMovements/StockCounts`
- [ ] `/InventoryMovements/Reports`

## management:communication (4)

- [ ] `/CommunicationWorkspace/Index`
- [ ] `/CommunicationAdministration/Index`
- [ ] `/CustomFields/Index`
- [ ] `/DocumentAdministration/Index`

## management:governance (2)

- [ ] `/AuditLogs/Index`
- [ ] `/ImportHistory/Index`

## Verified completion evidence

| Route | Final commit SHA | Build / JS / .NET / Browser / SQL | Roles / Tenant / Device / Locale | Result / Notes |
| --- | --- | --- | --- | --- |

## Current in-progress or blocked route

**Selected route:** `/FeeDiscountRules/Index` (first unchecked, remains unchecked). **Status: Implemented / QA Pending / Blocked** — resume this same route next execution; no route marked Verified Complete.

**Baseline inspected:** Ashraful HEAD `2e8cfb76daeb5ad5f3940ffadba588a9605a3bf1` (2026-11-14 run). Read `AGENTS.md`, `docs/UI_UX_FINAL_IMPLEMENTATION_PLAN_20261109.md`, route ownership, this ledger, `EduOS.App/Controllers/FeeDiscountRulesController.cs`, `EduOS.App/Views/FeeDiscountRules/Index.cshtml`, `EduOS.App/wwwroot/js/finance/discount-rules.js`, and `EduOS.Tests/JavaScript/fee-discount-rules-ui.test.cjs`. Existing controller enforces TenantAdmin + FINANCE; UI uses existing fee API, antiforgery token, rowVersion, session-change guard, and bilingual labels (source inspection only).

**Confirmed source-level UX gaps:** duplicate in-content h1 despite shared shell title; `discountRuleRefresh` button conflicts with no-Refresh rule; explanatory text creates unnecessary vertical space. Existing form/list already support create/edit, fee-head options, effective dates, active status, pagination and confirmation; preserve canonical service and financial history. Do not remove the only stale-data recovery path without replacing it safely (e.g. reload on navigation or conflict recovery).

**QA evidence:** GitHub connector source reads **PASS**. Local `git ls-remote https://github.com/cseashrafulislam/EduOS.git refs/heads/Ashraful` **FAIL** (`Could not resolve host: github.com`). `node --version` **PASS** (`v22.16.0`); `dotnet --version` **BLOCKED** (`dotnet: command not found`). `dotnet build -c Release`, `node --test EduOS.Tests/JavaScript/fee-discount-rules-ui.test.cjs`, .NET integration/security tests, authenticated browser/phone/theme/locale/accessibility, SQL tenant/finance/history and shared regression: **NOT RUN** (repository checkout/runtime unavailable). No application files changed; no route completion or production claims. Ledger-only progress note; this commit is not a verified application implementation.

**Next required work on this same page:** obtain a usable Ashraful checkout and .NET/browser/SQL QA runtime; implement compact single-shell-heading form/list UX without Refresh, ensure safe conflict recovery, verify full create/edit/invalid/duplicate/retry/concurrency/tenant/CSRF/IDOR, run focused and shared regressions at the exact final code SHA. Keep checkbox empty until all required QA passes.

### 2026-11-14 Lane D implementation follow-up

- **Selected route:** `/FeeDiscountRules/Index` remains **[ ]**. **New Verified Complete routes: 0**; no advance to the next route.
- **Ashraful HEAD before edits:** `64d8d0ef5f4284193bde712a7875485463d881b6` via GitHub branch-ref commit retrieval. **Final code HEAD:** `fa4aecf4ee19234cce5931cf8454856879c0c6b0` (verified branch-ref after three non-force GitHub contents commits).
- **Changed only selected route:** `EduOS.App/Views/FeeDiscountRules/Index.cshtml` (remove duplicate h1, verbose introduction/footer, decorative Refresh; compact spacing); `EduOS.App/wwwroot/js/finance/discount-rules.js` (replace manual Refresh with actionable load-error retry and automatic reload after 409 stale-version conflict, preserve explicit error); `EduOS.Tests/JavaScript/fee-discount-rules-ui.test.cjs` (regression checks for compact shell and recovery). No service/schema/API/financial data changes.
- **PASS (source-level, not runtime QA):** fetched all three final files at exact SHA; JavaScript parser via `new Function` for page script and test file; asserted no duplicate h1/Refresh, retry and conflict reload present, CSRF/session guard still present, new test case present. GitHub branch-ref SHA and contents commits confirmed.
- **BLOCKED:** `git ls-remote https://github.com/cseashrafulislam/EduOS.git refs/heads/Ashraful` => `Could not resolve host: github.com`; `dotnet` executable unavailable. Node v22.16.0 present, but full checkout absent.
- **NOT RUN:** repository `node --test EduOS.Tests/JavaScript/fee-discount-rules-ui.test.cjs`, shared JS suite, .NET Release build and .NET integration/security tests, authenticated SQL/financial-history and browser/mobile/light-dark/bn-en/accessibility/tenant/role/IDOR/CSRF/retry/concurrency tests. **Source-level checks must not be represented as final acceptance.**
- **Next:** resume SAME route, acquire executable checkout/.NET + authenticated SQL/browser QA, run full required gates and fix any failures before checking `[x]` or proceeding to `/AdmissionCashReceipts/Index`.
