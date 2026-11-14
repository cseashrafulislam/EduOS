# EduOS Unified UX — Lane E: HR, Payroll, Campus Operations & Career

Source: [canonical route ownership](../UX_ROUTE_OWNERSHIP_20261114.json). Branch: `Ashraful`. This is the **page-by-page UX implementation ledger**. Existing checked routes may have exact-SHA Final QA evidence recorded below. Unchecked routes are not yet formally credited UX Done under the new process; a previously built page should be audited and reused, not reimplemented without a gap.

**Owner override — UX FIRST (2026-11-14):** Complete **all applicable UX implementation before the separate Final QA phase**. Work in canonical menu order; finish the first unfinished page's Razor/JS, backend/API where needed, backend-generated system Codes, compact responsive design, bn/en Help, shared AI/Voice and security-safe flows, then mark it **[x] UX Done** and immediately move on. Finish all pages of a menu before the next menu. Do not hold UX implementation hostage to expensive SQL/Chromium/browser Final QA. Do lightweight authoring review/syntax/compile checks where practical. Keep already verified routes intact, but recognize historic QA PASS is limited to the tested source SHA.

**Two status tracks:** checkbox **[x] = UX Done only**, not a claim that later Final QA has passed. Track `Final QA: Not Run / historical exact-SHA PASS / later PASS / FAIL` separately in the final-phase register. For every newly UX Done page, record route, code commit, changed files and deferred QA. No fake PASS, no test deletion and no waiver of tenant isolation, audit, financial/inventory or approval safety. Fix discovered critical defects during implementation. The 5 scheduled UX automations remain **OFF** per owner; this policy change does not enable them.

**Design standard:** One shared title, no top gaps, duplicate headings or decorative Refresh; practical mobile-friendly create/edit/list/report UI; multilingual Help, shared context-aware AI/Voice only where relevant; immutable server-generated internal Codes; reuse canonical domain data and backend validation. After all UX routes are done, run the full combined .NET/JS/SQL/Chromium/tenant/security/accessibility/locale/retry/report QA and repair any issues before release.

**Assigned canonical menu pages: 44.** Related subpages are included in each route's verification, not counted again as separately assigned menu entries.

## management:people (23)

- [ ] `/EmployeeAttendance/Index`
- [ ] `/LeaveEntitlements/Index`
- [ ] `/PayrollOperations/Index`
- [ ] `/EmployeePortal/Index`
- [ ] `/HrSetup/Index`
- [ ] `/HR/Directory`
- [ ] `/HR/Leaves`
- [ ] `/HrEmployeeRecords/Index`
- [ ] `/HR/ShiftAssignments`
- [ ] `/HR/CampusAssignments`
- [ ] `/HR/BankAccounts`
- [ ] `/EmployeeContracts/Index`
- [ ] `/Recruitment/Index`
- [ ] `/PayrollSalaryStructures/Index`
- [ ] `/PayrollSalaryComponents/Index`
- [ ] `/PayrollBonuses/Index`
- [ ] `/PayrollLoanRegister/Index`
- [ ] `/LoanDisbursement/Index`
- [ ] `/LoanRecovery/Index`
- [ ] `/PayrollSalaryStatement/Index`
- [ ] `/PayrollReconciliation/Index`
- [ ] `/PayrollJournalPosting/Index`
- [ ] `/PayrollPaymentPosting/Index`

## management:campus (18)

- [ ] `/FacilityCosts/Index`
- [ ] `/FacilityWorkOrders/Index`
- [ ] `/FacilitySpaces/Index`
- [ ] `/HostelOperations/Index`
- [ ] `/TransportOperations/Index`
- [ ] `/OperationsInsights/Index`
- [ ] `/HostelOperations/Applications`
- [ ] `/HostelOperations/Setup`
- [ ] `/HostelOperations/Allocations`
- [ ] `/HostelOperations/Reports`
- [ ] `/TransportOperations/Setup`
- [ ] `/TransportOperations/Assignments`
- [ ] `/TransportOperations/Reports`
- [ ] `/hostel/guardian-stays/print`
- [ ] `/hostel/my-history/print`
- [ ] `/TransportOperations/Family`
- [ ] `/TransportOperations/Trips`
- [ ] `/TransportOperations/DriverTrips`

## academics:career (3)

- [ ] `/CareerEmployers/Index`
- [ ] `/CareerOpportunities/Index`
- [ ] `/CareerPlacements/Index`

## Verified completion evidence

| Route | Final commit SHA | Build / JS / .NET / Browser / SQL | Roles / Tenant / Device / Locale | Result / Notes |
| --- | --- | --- | --- | --- |

## Current in-progress or blocked route

`/EmployeeAttendance/Index` — QA Pending / Blocked (2026-11-14). Inspected exact source at `63836763afeb85ea9ed72e9f107b79e36cc5c8a5`: MVC and API controllers, Razor form/list/correction/history, JS request retry and form guard, attendance service, existing JS regression tests. Source findings: page repeats shell title with an H2 and long note; Present is preselected for new attendance, contrary to explicit-attendance intent; register lacks observed export/print and complete bilingual/browser verification. Existing service uses tenant-scoped queries, serializable record transaction, leave checks and audited correction; these are source observations, not runtime PASS. Local `git ls-remote https://github.com/cseashrafulislam/EduOS.git refs/heads/Ashraful` FAIL (`Could not resolve host: github.com`); `dotnet` NOT AVAILABLE; Node v22.16.0 AVAILABLE. Release build NOT RUN; focused JS/.NET tests NOT RUN; authenticated SQL/browser/mobile/theme/bn-en/IDOR/CSRF/retry/concurrency NOT RUN. No production data writes. Keep route unchecked; do not proceed to next route until complete.


### 2026-11-14 Employee Attendance implementation checkpoint

Route `/EmployeeAttendance/Index` remains **[ ] QA Pending**, 0 newly Verified Complete. Two non-force GitHub Contents API commits on `Ashraful`: `5e2080985fa8e1d78bc0ba14e03ef3fbb5203c19` (Razor compact header, required explicit status placeholder) and `9733f25c82cba90d7ba1f34f845de716f776ecb4` (JS rejects empty status and preserves blank selection on form reset). Post-write file readback PASS for both changes. No API/schema/history/permission changes or production data writes. Local git checkout FAIL (`Could not resolve host: github.com`); .NET SDK unavailable. Release build NOT RUN; JS execution/regressions NOT RUN; authenticated SQL, browser, mobile, bn/en, theme, security and concurrency QA NOT RUN. Register print/export, bilingual help and applicable Global Page Assistant verification still pending. Do not advance to `/LeaveEntitlements/Index` until full final QA passes.

### 2026-11-14 Attendance register export checkpoint

Route `/EmployeeAttendance/Index` remains **[ ] QA Pending**; newly Verified Complete this execution: **0**. Added bounded current-page CSV export (maximum 25 already authorized register rows), formula-injection escaping, disabled export on loading/revoked access and explicit UI scope label. Commits on `Ashraful`: `82023b9c59c5e5de67c8ac39e634cf2e8474fbcd` (view) and `5ea3ca010b0ed041299afad097084c3e290ebee1` (JavaScript). GitHub Contents API write acknowledged; independent browser/SQL execution and exact-HEAD CI remain NOT RUN. Local `git ls-remote` FAIL DNS (`Could not resolve host: github.com`), .NET SDK unavailable, Node v22.16.0 available. Export behavior, print, locale, accessibility, IDOR/CSRF, concurrency, shared-component regression and final application QA still NOT RUN; do not check route or proceed to next route. No production data/schema writes.

### 2026-11-14 Attendance CSV correctness checkpoint

`/EmployeeAttendance/Index` remains **[ ] QA Pending**; newly Verified Complete: **0**. Fixed actual CSV defects in `EduOS.App/wwwroot/js/hr/employee-attendance.js`: the previous literal backslashes in the spreadsheet-formula regex prevented whitespace-prefixed formula detection, and literal `\\uFEFF` / `\\r\\n` emitted text instead of a Unicode BOM and CRLF row separators. Code commit `f4c04a9be06063f8fd38a4163a69ddaa1194792a` on `Ashraful`. Focused Node v22.16.0 CSV behavior assertions **PASS (9)**: formula prefixes, quotes, null, BOM and CRLF; these are isolated algorithm assertions, **not** application integration. Local Git checkout **FAIL** (`Could not resolve host: github.com`); .NET SDK **NOT AVAILABLE**. Release build, full JS/.NET tests, authenticated browser/mobile/bn-en/theme/SQL/security and CSV download integration **NOT RUN**. No route completion or next-route advancement; no production data writes.

### 2026-11-14 Attendance bounded print checkpoint

`/EmployeeAttendance/Index` remains **[ ] QA Pending**; newly Verified Complete this run: **0**. Added Print page action scoped to the currently loaded, authorized register page (up to 25 rows); print stylesheet excludes record entry, filter controls, action buttons and unrelated shell content. Print disabled on stale/loading, empty and revoked-access states; `afterprint` removes scoped print mode. Commits `45b827462b40b4e9f12e63a41e82b60f7fec24bd` (Razor), `b7e2ebdbd0399d3fb22a97085e74df073424df3e` (JS), `64c52595c91d6bbde6a1367fb148c4a714038f81` (print lifecycle correction). Connector readback and 8 structural source assertions PASS, **not browser behavior tests**. Local `git ls-remote https://github.com/cseashrafulislam/EduOS.git refs/heads/Ashraful` FAIL (`Could not resolve host: github.com`); `dotnet` unavailable. Release build, actual JS application integration, authenticated SQL/browser/phone/print preview, locale/theme/accessibility, role/tenant/IDOR/CSRF/concurrency and shared regressions NOT RUN. Print layout may require browser compatibility testing before release. No production data writes, no schema changes, no route advancement. Exact final branch HEAD after this ledger commit requires verification.

### 2026-11-14 Attendance print nested-content correction

`/EmployeeAttendance/Index` remains **[ ] QA Pending**; **0 newly Verified Complete**. At application commit `1620a375056c77342c7056c692da189f0b1b597e`, fixed print stylesheet visibility for nested elements within register table cells: the prior blanket `body ... * { visibility: hidden }` could hide descendant spans even while their `td` was visible. Print scope remains limited to the loaded register; filter/actions remain excluded. Connector source readback structural checks **PASS (5/5)**, not rendered print proof. Local `git ls-remote` **FAIL** (DNS host resolution), Node v22.16.0 **AVAILABLE**, .NET SDK **NOT AVAILABLE**. Release build, targeted application JS/.NET integration, SQL, authenticated browser/print preview/mobile/bn-en/theme/role/tenant/IDOR/CSRF/concurrency **NOT RUN**. No production data or schema changes. Continue same route, do not advance.
