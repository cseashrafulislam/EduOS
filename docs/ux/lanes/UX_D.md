# EduOS Unified UX — Lane D: Finance, Inventory, Communication & Audit

Source: [canonical route ownership](../UX_ROUTE_OWNERSHIP_20261114.json). Branch: `Ashraful`. This is a **per-page UX + Final QA completion ledger**. Existing pages may already have partial implementation; unchecked means **NOT verified in this consolidated acceptance cycle**, not proof that the implementation is absent.

**Execution rule:** Select the FIRST unchecked route below, audit its related create/edit/detail/approval/report pages, complete all applicable UX/backend/AI-Voice/security improvements, execute required FINAL QA at the final commit SHA, and only then check the route. If tests fail or required QA is unavailable, leave unchecked and record `QA Pending / Blocked`; resume the same route next run rather than moving on. Record commit SHA, PASS/FAIL/BLOCKED/NOT RUN and affected shared regressions below. Update **only this lane's ledger**; other lanes' files are owned by their own tasks.

**Quality:** One shell title, no top gaps/duplicate notes, backend-generated immutable internal Codes, practical responsive forms/tables/reports, bn/en Help, appropriate shared Global Page Assistant (not per-page copies), server tenant/campus/role checks, concurrency/idempotency and source/report correctness. Run application build, targeted JS/.NET tests, applicable database/integration, actual mobile/browser checks; do not mark Verified Complete based on source-only assertions. Preserve existing APIs and history. Exact-code-SHA QA evidence is required.

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

**Selected route:** `/FeeDiscountRules/Index` (first unchecked, remains unchecked). **Status: QA Pending / Blocked** — resume this same route next execution; no route marked Verified Complete.

**Baseline inspected:** Ashraful HEAD `2e8cfb76daeb5ad5f3940ffadba588a9605a3bf1` (2026-11-14 run). Read `AGENTS.md`, `docs/UI_UX_FINAL_IMPLEMENTATION_PLAN_20261109.md`, route ownership, this ledger, `EduOS.App/Controllers/FeeDiscountRulesController.cs`, `EduOS.App/Views/FeeDiscountRules/Index.cshtml`, `EduOS.App/wwwroot/js/finance/discount-rules.js`, and `EduOS.Tests/JavaScript/fee-discount-rules-ui.test.cjs`. Existing controller enforces TenantAdmin + FINANCE; UI uses existing fee API, antiforgery token, rowVersion, session-change guard, and bilingual labels (source inspection only).

**Confirmed source-level UX gaps:** duplicate in-content h1 despite shared shell title; `discountRuleRefresh` button conflicts with no-Refresh rule; explanatory text creates unnecessary vertical space. Existing form/list already support create/edit, fee-head options, effective dates, active status, pagination and confirmation; preserve canonical service and financial history. Do not remove the only stale-data recovery path without replacing it safely (e.g. reload on navigation or conflict recovery).

**QA evidence:** GitHub connector source reads **PASS**. Local `git ls-remote https://github.com/cseashrafulislam/EduOS.git refs/heads/Ashraful` **FAIL** (`Could not resolve host: github.com`). `node --version` **PASS** (`v22.16.0`); `dotnet --version` **BLOCKED** (`dotnet: command not found`). `dotnet build -c Release`, `node --test EduOS.Tests/JavaScript/fee-discount-rules-ui.test.cjs`, .NET integration/security tests, authenticated browser/phone/theme/locale/accessibility, SQL tenant/finance/history and shared regression: **NOT RUN** (repository checkout/runtime unavailable). No application files changed; no route completion or production claims. Ledger-only progress note; this commit is not a verified application implementation.

**Next required work on this same page:** obtain a usable Ashraful checkout and .NET/browser/SQL QA runtime; implement compact single-shell-heading form/list UX without Refresh, ensure safe conflict recovery, verify full create/edit/invalid/duplicate/retry/concurrency/tenant/CSRF/IDOR, run focused and shared regressions at the exact final code SHA. Keep checkbox empty until all required QA passes.
