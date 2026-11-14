# EduOS Unified UX — Lane A: Platform, Institution & Academic Setup

Source: [canonical route ownership](../UX_ROUTE_OWNERSHIP_20261114.json). Branch: `Ashraful`. This is the **page-by-page UX implementation ledger**. Existing checked routes may have exact-SHA Final QA evidence recorded below. Unchecked routes are not yet formally credited UX Done under the new process; a previously built page should be audited and reused, not reimplemented without a gap.

**Owner override — UX FIRST (2026-11-14):** Complete **all applicable UX implementation before the separate Final QA phase**. Work in canonical menu order; finish the first unfinished page's Razor/JS, backend/API where needed, backend-generated system Codes, compact responsive design, bn/en Help, shared AI/Voice and security-safe flows, then mark it **[x] UX Done** and immediately move on. Finish all pages of a menu before the next menu. Do not hold UX implementation hostage to expensive SQL/Chromium/browser Final QA. Do lightweight authoring review/syntax/compile checks where practical. Keep already verified routes intact, but recognize historic QA PASS is limited to the tested source SHA.

**Two status tracks:** checkbox **[x] = UX Done only**, not a claim that later Final QA has passed. Track `Final QA: Not Run / historical exact-SHA PASS / later PASS / FAIL` separately in the final-phase register. For every newly UX Done page, record route, code commit, changed files and deferred QA. No fake PASS, no test deletion and no waiver of tenant isolation, audit, financial/inventory or approval safety. Fix discovered critical defects during implementation. The 5 scheduled UX automations remain **OFF** per owner; this policy change does not enable them.

**Design standard:** One shared title, no top gaps, duplicate headings or decorative Refresh; practical mobile-friendly create/edit/list/report UI; multilingual Help, shared context-aware AI/Voice only where relevant; immutable server-generated internal Codes; reuse canonical domain data and backend validation. After all UX routes are done, run the full combined .NET/JS/SQL/Chromium/tenant/security/accessibility/locale/retry/report QA and repair any issues before release.

**Assigned canonical menu pages: 41.** Related subpages are included in each route's verification, not counted again as separately assigned menu entries.

## platform:other (1)

- [x] `/Dashboard/Admin`

## platform:institutions (2)

- [x] `/PlatformTenants/Index`
- [x] `/PlatformCatalogAdministration/Index`

## platform:features (4)

- [x] `/PlatformFeatureAdministration/Index`
- [x] `/PlatformProductModuleAdministration/Index`
- [x] `/PlatformInstitutionModulePresets/Index`
- [x] `/PlatformModuleFeatures/Index`

## platform:subscriptions (3)

- [x] `/SubscriptionPlanAdministration/Index`
- [ ] `/SubscriptionPaymentHistory/Index`
- [ ] `/Account/PaymentVerification`

## platform:governance (1)

- [ ] `/LegalDocuments/Index`

## setup:other (1)

- [x] `/Dashboard/Index`

## setup:access (2)

- [ ] `/TenantUsers/Index`
- [ ] `/TenantPermissionOverrides/Index`

## setup:institution (13)

- [ ] `/TenantDomains/Index`
- [ ] `/TenantTerminology/Index`
- [ ] `/TenantSettings/Index`
- [ ] `/SmartSetup/Index`
- [ ] `/NumberSeries/Index`
- [ ] `/TenantModules/Index`
- [ ] `/IntegrationAdministration/Index`
- [ ] `/InstitutionManagement/Profile`
- [ ] `/InstitutionManagement/Campuses`
- [ ] `/InstitutionManagement/Regional`
- [ ] `/InstitutionManagement/Branding`
- [ ] `/InstitutionManagement/Subdomain`
- [ ] `/InstitutionManagement/History`

## setup:subscription (2)

- [ ] `/SubscriptionBilling/Index`
- [ ] `/UsageHistory/Index`

## setup:work-center (1)

- [ ] `/CentralWorkCenter/Index`

## setup:academic-setup (11)

- [ ] `/AcademicManagement/Years`
- [ ] `/AcademicManagement/Terms`
- [ ] `/AcademicManagement/Departments`
- [ ] `/AcademicManagement/Classes`
- [ ] `/AcademicManagement/Batches`
- [ ] `/AcademicManagement/Sections`
- [ ] `/AcademicManagement/Rooms`
- [ ] `/AcademicManagement/Subjects`
- [ ] `/AcademicManagement/Curricula`
- [ ] `/AcademicManagement/Prerequisites`
- [ ] `/AcademicManagement/Offerings`

## Verified completion evidence

**Exact tested application code/QA workflow commit:** `06d691fc2f130016c2a813f0994830cc431590df` on `Ashraful`. **Dedicated GitHub Actions gate:** [Dashboard Final QA — Real SQL and Browser (PASS)](https://github.com/cseashrafulislam/EduOS/actions/runs/43560859792). This gate checks both dashboards on the real MVC app, an isolated migrated SQL Server 2022 database, genuine cookie/CSRF login and Chromium. The later ledger-only update does not modify any tested application source.

| Route | Final code commit SHA | Build / JS / .NET / Browser / SQL | Roles / Tenant / Device / Locale | Result / Notes |
| --- | --- | --- | --- | --- |
| `/Dashboard/Admin` | `06d691fc2f130016c2a813f0994830cc431590df` | PASS / PASS / PASS / PASS / PASS | Anonymous denied; SuperAdmin; English desktop 1366px; Bangla mobile 390px; API vs rendered KPI, authenticated permissioned links, read-only Help and shared Assistant | **Verified Complete** — isolated Dashboard final QA PASS |
| `/Dashboard/Index` | `06d691fc2f130016c2a813f0994830cc431590df` | PASS / PASS / PASS / PASS / PASS | TenantAdmin, Teacher, Student; demo tenant only, platform-wide API forbidden; financial counts absent for unauthorized non-admin roles; desktop and mobile | **Verified Complete** — isolated Dashboard final QA PASS |

| `/PlatformTenants/Index` | `7cf614874196ecf4a336fcfc6c3cd8b860d2d805` | PASS / PASS / PASS / PASS / PASS | SuperAdmin English desktop; Bangla mobile; anonymous/TenantAdmin/Teacher denied; audited suspend/reactivate; replay-safe mutations; stale version 409; immutable system tenant and paged history | **Verified Complete**, retested on [final two-page gate PASS](https://github.com/cseashrafulislam/EduOS/actions/runs/43564208667) |
| `/PlatformCatalogAdministration/Index` | `7cf614874196ecf4a336fcfc6c3cd8b860d2d805` | PASS / PASS / PASS / PASS / PASS | SuperAdmin metadata edit/replay/restore, immutable Code, false/stale 409 + automatic reload + successful activation, activation replay and restore; mobile Bangla Help, forbidden tenant roles | **Verified Complete** — [final SQL/Chromium gate PASS](https://github.com/cseashrafulislam/EduOS/actions/runs/43564208667) |

### Exact-SHA QA evidence — 2026-11-14

- **Build and tests:** `dotnet restore EduOS.slnx`, Release full-solution build, filtered xUnit `DashboardServiceTests|PlatformDashboardServiceTests`, dashboard Node contracts, reviewed bilingual Dashboard Page Help Node tests and browser script syntax all PASS on the tested commit.
- **Real SQL/browser:** clean, disposable `EduOS_Demo_DashboardQA` SQL Server 2022 database; EF migrations and SuperAdmin seed PASS, isolated Staging demo tenant/role seed PASS. Real Chromium test script `tests/browser/dashboard-live.cjs` PASS in all six logged scenarios: anonymous access blocked, SuperAdmin English desktop and Bangla mobile, TenantAdmin, Teacher and Student login, role-scoped Dashboard API data, platform API denial, no cross-role private values, expected navigation, accessibility elements, no horizontal overflow, shared Page Help and one shared page-assistant launcher/voice entry.
- **QA fixes tested:** compact bilingual admin dashboard and removed obsolete Refresh; canonical Dashboard service retained; bilingual page manual and its tests aligned; `ErrorController` preserves denied 403/no-store; `Program.cs` skips MVC error-page re-execution on `/api` so private endpoints retain HTTP error contracts. Browser tests use actual Login Razor form and its anti-forgery wrapper; authenticated API requests use that browser session, not a cookie-independent API client.
- **Scope:** Dashboard-specific verification is complete. The repository-wide **CI** workflow on this commit still reports FAILURE due to other JavaScript contracts; the separate general **SQL Server Migration Smoke** run was in progress when this acceptance evidence was recorded. Those are **product-wide release blockers and must not be described as a fully green repository/release gate**. This scoped page sign-off does not certify unrelated modules, load benchmarks, production institution UAT or deployment.
- **Automation:** remains OFF per owner's earlier instruction. No production database or existing tenant data was changed by these QA fixtures.

### Platform Tenant Lifecycle exact-SHA QA — 2026-11-14

- Commit `a9e699054127335c1a0c8e197f8aa45f8d00cc10`: .NET 10 Release build, focused `PlatformTenantLifecycleServiceTests` + `PlatformTenantStateHistoryTests` xUnit tests, lifecycle/catalog Node contracts and real Chromium on disposable migrated SQL Server 2022 **PASS**.
- Real browser five PASS scenarios: anonymous denied; SuperAdmin English desktop end-to-end search and state history, Active → Suspended → Active; replayed request does not duplicate audits; stale RowVersion returns 409; reserved `EDUOS-SYSTEM` denies mutations; Bangla mobile and reviewed Help; tenant-role API and page denied.
- Preserved historical tenant Codes, subscription/accounting sources, existing APIs and approval rules. Added server-side guard for reserved bootstrap tenant. The dedicated gate is green; unrelated repository-wide CI failures still block broad product release certification.
- Only the lifecycle route was marked verified; catalogue route remains unchecked until its own metadata/activation/browser QA completes. Tested source SHA is exact; this ledger-only commit changes no application code.

### Platform Institution Management full-menu sign-off — 2026-11-14

- **Final tested application commit:** `7cf614874196ecf4a336fcfc6c3cd8b860d2d805` (`Ashraful`); **[dedicated GitHub Actions real SQL/browser QA PASS](https://github.com/cseashrafulislam/EduOS/actions/runs/43564208667)**. All 18 applicable workflow steps concluded success/skipped as designed; **six browser scenarios PASS**. Both `/PlatformTenants/Index` and `/PlatformCatalogAdministration/Index` were included in that same final exact-code SHA run.
- **Verified gates:** .NET 10 full solution Release build; focused lifecycle/history/catalog activation/metadata xUnit tests; lifecycle/catalog Node UI contract tests; EF migration, isolated SuperAdmin/tenant seed on disposable SQL Server 2022; real staging auth, CSRF, role separation and Chromium English desktop/Bangla mobile; shared Page Help/Assistant; operational read and mutation flows.
- **Lifecycle:** paged list/search, recorded state history, Active→Suspended→Active, idempotent replay without duplicate audit, stale row-version 409, protected `EDUOS-SYSTEM`, anonymous/TenantAdmin/Teacher forbidden, no customer data migrated or altered outside the disposable QA database.
- **Catalog:** immutable master Code, localized type/cycle list, edit name/Bangla name/description/default cycle/order, field validation, metadata idempotent retry and SQL round-trip restoration, activation/deactivation, stale UI version 409 followed by automatic list reload and valid retry, activation replay and restoration, role denial, mobile help. **Production bug fixed:** removed unnecessary manually started relational transaction from `PlatformCatalogService.MetadataAdministration.cs`; EF `SaveChangesAsync` still atomically writes metadata + audit while remaining compatible with SQL Server retrying execution strategy. Failure-only Try again and committed-but-refresh-failed messaging preserve truthful UI state.
- **Scope limitation:** Dedicated Platform Institutions QA is fully green; unrelated general `CI` and product-wide SQL acceptance are separate release gates and may remain failing. This is page-scoped Final QA, not complete EduOS production/UAT or load-certification. Existing 5 scheduled UX automations remain OFF.

## UX-first implementation progress — 2026-11-14

**New owner process:** UX is delivered to completion before a separate unified Final QA phase. This checkbox is a statement about implemented UX, **not** Final QA results. Four historically [x] Dashboard/Institution Management routes have their previous exact-SHA QA logs above; those results are retained for history.

| Route | UX implementation | Final QA |
| --- | --- | --- |
| `/PlatformFeatureAdministration/Index` | **UX Done**; view `e3105c8f7936b34118c3997a58531378d031921a`, JS `f938c57e69e9413b0807ad0c6fb03e3eb52ccc2f`, contextual bilingual Help `c9809fb5b93b195fdc1e6a3605597c3aaf2dcef4` | **Deferred / Not Run for new UX** |

Feature page delivered: compact responsive bilingual list/editor, no duplicate page title or decorative Refresh, read-only immutable Code, existing authorized metadata API retained, server-generated request ID client-side per intent for retry semantics, checkbox-style availability, loading/empty/error status, failure-only retry control, stale row-version conflict handling, and shared Page Help. Authoring check: JavaScript parsed and Razor element IDs/source references reviewed; **no authenticated browser/SQL/independent QA run was performed on these new changes**. Keep previously implemented feature backend and plan assignments as the single source of truth.

**Product Module Catalog — UX Done:** `/PlatformProductModuleAdministration/Index` implemented in view `ca1290e628dd57de399f6c86787d5ee4cb8b466c`, JS `59308c21950188e5986719a649d68a1b4e80e5d6`, bilingual Help `235cffd08607cf80618fa4a434f761e5bc3dfaa8`. Compact mobile-friendly module list/form, no duplicate heading/Refresh, immutable read-only Code, core-module always-active control, existing authorized server APIs and rowversion, failure-only Try again, conflict reload, locale labels and shared Help. Basic code/syntax review only. **Final QA deferred / NOT RUN**.

**Institution Module Presets — UX Done:** `/PlatformInstitutionModulePresets/Index` view `03571986d80b4f561d02d239a78c14103d909695`, JS `d041bcaf5e16539280b59d8baa2f542a05279210`, contextual bilingual Help `78b3b756e2d3a6f0c1097e1d4b0c95186ab9ee58`. Compact type selector/table, no redundant header/Refresh, localized required/enabled checkboxes and core locks, existing CSRF-protected API, row-level save with retry-safe request intent, optimistic conflict reload, failure-only retry and stale response guard. No new persistence domain. **Final QA: deferred / NOT RUN**.

**Module Feature Mapping — UX Done:** `/PlatformModuleFeatures/Index` view `e96f8c2fab0d61897fc743a7fef73eb63bb6a85c`, JS `99cdf17c51626d1ef02a2f7ce8fcfddbbf246203`, bilingual Help `a1ad152979dc1c94f4df996587c5acb7f9153562`. Responsive module selector/mapping table, no duplicate heading/Refresh, localized feature rows and locked core/inactive mappings, existing audited API and version fields, idempotent retry intent, conflict reload, failure-only retry, async sequence guard. **Final QA deferred / NOT RUN**, not counted as QA PASS.

**Subscription Plan Administration — UX Done:** `/SubscriptionPlanAdministration/Index` view `72248e9e544ba840ad1a98887c20757c2f6881c9`, JS `81a81122d7a530d23d9aac4f0fbe86b1694d061d`, bilingual Page Help `6119c356c2bdf8340cbc2d819e3e543bfc9ef927`. Compact responsive pricing/limits/feature configuration, no redundant header/Refresh, localized lifecycle and pricing actions, unchanged original SuperAdmin API/transaction/backend price snapshot ownership, read-only existing Code, stable client intent request IDs and conflict reload. **Final QA deferred / NOT RUN.** Financial edge cases (large decimal values, old/new snapshots, concurrent plan feature updates) remain mandatory for later QA, not certified by this UX checkpoint.

**Completed menu:** platform:features — `4 / 4 UX Done`. **Current menu:** platform:subscriptions — `1 / 3 UX Done`. Next route `/SubscriptionPaymentHistory/Index`. Final QA deferred until all UX routes are finished. Finish UX for the remaining three routes, then advance to platform:subscriptions. The scheduled automations remain OFF. No Final QA is required between UX pages, but safety regressions must be fixed when discovered.

