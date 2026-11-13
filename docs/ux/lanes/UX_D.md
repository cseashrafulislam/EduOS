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

None assigned yet. Update when selecting the first unchecked page.
