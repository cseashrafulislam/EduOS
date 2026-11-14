# EduOS Unified UX — Lane D: Finance, Inventory, Communication & Audit

Source: [canonical route ownership](../UX_ROUTE_OWNERSHIP_20261114.json). Branch: `Ashraful`. This is the **page-by-page UX implementation ledger**. Existing checked routes may have exact-SHA Final QA evidence recorded below. Unchecked routes are not yet formally credited UX Done under the new process; a previously built page should be audited and reused, not reimplemented without a gap.

**Owner override — UX FIRST (2026-11-14):** Complete **all applicable UX implementation before the separate Final QA phase**. Work in canonical menu order; finish the first unfinished page's Razor/JS, backend/API where needed, backend-generated system Codes, compact responsive design, bn/en Help, shared AI/Voice and security-safe flows, then mark it **[x] UX Done** and immediately move on. Finish all pages of a menu before the next menu. Do not hold UX implementation hostage to expensive SQL/Chromium/browser Final QA. Do lightweight authoring review/syntax/compile checks where practical. Keep already verified routes intact, but recognize historic QA PASS is limited to the tested source SHA.

**Two status tracks:** checkbox **[x] = UX Done only**, not a claim that later Final QA has passed. Track `Final QA: Not Run / historical exact-SHA PASS / later PASS / FAIL` separately in the final-phase register. For every newly UX Done page, record route, code commit, changed files and deferred QA. No fake PASS, no test deletion and no waiver of tenant isolation, audit, financial/inventory or approval safety. Fix discovered critical defects during implementation. The 5 scheduled UX automations remain **OFF** per owner; this policy change does not enable them.

**Design standard:** One shared title, no top gaps, duplicate headings or decorative Refresh; practical mobile-friendly create/edit/list/report UI; multilingual Help, shared context-aware AI/Voice only where relevant; immutable server-generated internal Codes; reuse canonical domain data and backend validation. After all UX routes are done, run the full combined .NET/JS/SQL/Chromium/tenant/security/accessibility/locale/retry/report QA and repair any issues before release.

**Assigned canonical menu pages: 39.** Related subpages are included in each route's verification, not counted again as separately assigned menu entries.

## management:finance (26)

- [x] `/FeeDiscountRules/Index`
- [x] `/AdmissionCashReceipts/Index`
- [x] `/FeeReceivablesAging/Index`
- [x] `/FeeHeadAccounting/Index`
- [x] `/AccountingSetup/Index`
- [x] `/AccountingJournals/Index`
- [x] `/InstitutionBudgets/Index`
- [x] `/AccountingReports/Index`
- [x] `/FeeOperations/Structures`
- [x] `/FeeOperations/Billing`
- [x] `/FeeOperations/Ledger`
- [x] `/StudentInvoicePosting/Index`
- [x] `/StudentPaymentPosting/Index`
- [x] `/FeeDueCalendar/Index`
- [x] `/FeeInstallmentProjection/Index`
- [x] `/FeeInstallmentPlans/Index`
- [x] `/FeeInvoiceCoverage/Index`
- [x] `/FeeDiscountAttributionReview/Index`
- [x] `/FinanceAdjustments/Policies`
- [x] `/FinanceAdjustments/StudentAdjustments`
- [x] `/FinanceAdjustments/FineRegister`
- [x] `/FinanceAdjustments/Refunds`
- [x] `/FinanceAdjustments/HostelClearances`
- [x] `/FinanceAdjustments/TransportClearances`
- [x] `/FinanceAdjustments/ReservationPreflight`
- [x] `/FinanceAdjustments/DiscountAwards`

## management:inventory (7)

- [x] `/InventoryCatalog/Index`
- [x] `/InventoryMovements/FacilityFinance`
- [x] `/AssetLifecycle/Index`
- [x] `/InventoryMovements/Transactions`
- [x] `/InventoryMovements/Adjustments`
- [x] `/InventoryMovements/StockCounts`
- [x] `/InventoryMovements/Reports`

## management:communication (4)

- [x] `/CommunicationWorkspace/Index`
- [x] `/CommunicationAdministration/Index`
- [x] `/CustomFields/Index`
- [x] `/DocumentAdministration/Index`

## management:governance (2)

- [x] `/AuditLogs/Index`
- [x] `/ImportHistory/Index`

## Current UX-first implementation checkpoint — 2026-11-14

**39/39 UX D routes source-implemented; 0 remain.** Finance: 26/26, Inventory: 7/7, Communication: 4/4, Governance: 2/2. This is source-level UX completion only; all Full Final QA gates remain separate. This means UX Done at source level only, **not Full Final QA** or production certification. Five scheduled UX automations stay OFF.

| Routes | UX implementation | Source commits | Final QA |
| --- | --- | --- | --- |
| `/FeeDiscountRules/Index` | Compact editor, no duplicate header/Refresh; safe failure retry and 409 conflict reload | `fa4aecf4` (previously implemented) | Deferred |
| `/AdmissionCashReceipts/Index` | Remove redundant title, move Admissions link to result toolbar, compact cash collection form and bilingual feedback; preserve posted GL and reversal semantics | `d31abb5a` | Deferred |
| `/FeeReceivablesAging/Index` | Single shared title, compact aging filter, preserve current-not-as-of accounting caveat and reconciliation | `d31abb5a` | Deferred |
| `/FeeHeadAccounting/Index` | Compact mapping and checkboxes, backend-generated retry-stable fee head code, bilingual static labels | `8f10ac20`, `d2c58255` | Deferred |
| `/AccountingSetup/Index` | Compact account/period/bank tabs and checkbox/action layout, server-generated new GL account code with retry replay, preserved historical account locks, bilingual labels | `e758bf0b`, `d2c58255` | Deferred |
| `/AccountingJournals/Index` | Remove duplicate title, compact controls, label reversal date/reason, keep maker-checker/post/reverse flows | `e758bf0b`, `d2c58255` | Deferred |
| `/StudentInvoicePosting/Index` | Remove misleading "cash posting unavailable" note, compact invoice filter/actions, bilingual labels and link to authorized cash posting route; preserve source-linked posted GL and reversal warning | `31f8b8dd` | Deferred |
| `/StudentPaymentPosting/Index` | Remove duplicate title and link-only row, group receipt filters/actions and navigation, bilingual labels; preserve one-campus, posted-invoice-first accounting safeguards | `31f8b8dd` | Deferred |
| `/InstitutionBudgets/Index` | Move Central Accounts action to budget register, group line addition and submit, place actual-vs-budget in detail actions | `09028510` | Deferred |
| `/AccountingReports/Index` | Compact central ledger introduction and horizontally scrollable tabs, replace generic Refresh with specific Load saved drafts; preserve ledger report definitions | `09028510` | Deferred |
| `/FeeOperations/Structures`, `/FeeOperations/Billing`, `/FeeOperations/Ledger` | Shared view removes redundant feature header, aligns active checkbox, puts batch/structure alongside and retains billing preflight | `09028510` | Deferred |

| `/FeeDueCalendar/Index`, `/FeeInstallmentProjection/Index`, `/FeeInvoiceCoverage/Index`, `/FeeDiscountAttributionReview/Index` | Removed redundant visible headings, kept print-only report titles; moved fee navigation next to export/print actions and retained filters and report caveats | `3139c714` | Deferred |
| `/FeeInstallmentPlans/Index` | Removed duplicate title and redundant inline Help popup; reused shared Help; compacted filter/actions without changing maker-checker or agreement issuance | `fe085636` | Deferred |
| `/FinanceAdjustments/Policies`, `/FinanceAdjustments/StudentAdjustments`, `/FinanceAdjustments/FineRegister`, `/FinanceAdjustments/Refunds` | One shared Razor implementation; removed duplicate title and empty action spacing; fixed checkbox layout; retained historical accounting/posted refund guards | `53f03539` | Deferred |
| `/FinanceAdjustments/HostelClearances`, `/FinanceAdjustments/TransportClearances`, `/FinanceAdjustments/ReservationPreflight`, `/FinanceAdjustments/DiscountAwards` | Compact finance forms and registers, contextual load and navigation buttons, preserved explicit non-payment/non-posting warnings and review permissions | `53f03539` | Deferred |
| `/InventoryCatalog/Index` | New item/location codes created by backend with retry-stable GUID and existing API compatibility; hidden manual Code controls, compact forms, labeled checkboxes, catalog-to-assets navigation | `39d8c3b4`, `6eb30704` | Deferred |
| `/InventoryMovements/FacilityFinance` | Removed duplicated shell title, compact reconciliation, preserved maker-checker and tenant session protection | `1f090bc0` | Deferred |
| `/AssetLifecycle/Index` | Shared shell title; asset category Code created by backend with duplicate retry protection; category form compact and authorized Asset Accounting navigation | `1f090bc0`, `b3b4f973` | Deferred |
| `/InventoryMovements/Transactions`, `/InventoryMovements/Adjustments`, `/InventoryMovements/StockCounts`, `/InventoryMovements/Reports` | Shared route view and partials; compacted drafts, movement ledger, adjustments, blind-count controls, reorder, reservations, stock position, lot aging and history | `1f090bc0`, `7203f9c1`, `dd7fc9c5` | Deferred |
| `/CommunicationWorkspace/Index` | Shared title, compact notice/editor/inbox/messages and contextual preference/device load actions | `7cd2be0a` | Deferred |
| `/CommunicationAdministration/Index` | Shared title, compact category/template setup; server-issued Notice Category (`NTC-`) and Template (`MSG-`) Codes, retry matching and existing explicit-Code edit compatibility | `7cd2be0a`, `cd4ccc39`, `0abdc04a` | Deferred |
| `/CustomFields/Index` | Hidden internal tenant profile FieldKey; server-generated immutable `CF_` Key with exact retry matching of definition and options, preserved existing field identities and tenant authorization | `7cd2be0a`, `cc1ffa89` | Deferred |
| `/DocumentAdministration/Index` | Single page title and compact tab/attachment workflow; `DCT-` type and `DTP-` template server-generated Codes with retry matching; existing file scanning and authorization retained | `7cd2be0a`, `dea33b4f`, `c40d694b` | Deferred |
| `/AuditLogs/Index`, `/ImportHistory/Index` | Removed duplicate headings, compact UTC filters and history results, preserved existing server-side tenant-scoped paging and report detail | `7cd2be0a`, `847ab959` | Deferred |

**Additional UX repair:** Document attachment no longer asks the user to type a scanned-file GUID: upload/select a verified file, see the selected filename/status, and let server handle the reference. Existing type/template edit and secure scanning preserved (`ba73e07a`).

**Safety/compatibility:** No schema/data migration, no posted accounting changes. Financial server authorization, tenant-owned queries, audit, rowversion and reversal rules retained. New internal code generation is server-owned and retry-stable; legacy explicit Code API calls remain. Source-level review preserves critical DOM IDs and form counts.

**Verification state:** .NET canonical Core/Persistence/App builds passed on source implementation commits through `dd7fc9c5`. Broader JavaScript contracts remain failed (63 failures recorded on `dd7fc9c5`), including unrelated modules; an initial new source test was too broad about print-only titles and was corrected. Inventory Physical Count Chromium workflow also failed at `7203f9c1` while loading the dynamic module in its test environment. Exact current-head CI/Chromium/tenant SQL/mobile/accessibility and financial-security Final QA remain NOT CERTIFIED. Source-level completion is **not** a release PASS.

**UX-first lane D implementation:** all assigned canonical routes checked as UX Done. **Next stage:** combined Final QA only after remaining UX lanes are complete. Latest working code `ba73e07a`; scope-sensitive release acceptance has **not** been run at exact final SHA.

## Verified completion evidence

| Route | Final commit SHA | Build / JS / .NET / Browser / SQL | Roles / Tenant / Device / Locale | Result / Notes |
| --- | --- | --- | --- | --- |

## Archived prior checkpoint (QA-first policy superseded by UX-first)

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
