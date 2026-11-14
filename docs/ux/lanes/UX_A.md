# EduOS Unified UX — Lane A: Platform, Institution & Academic Setup

Source: [canonical route ownership](../UX_ROUTE_OWNERSHIP_20261114.json). Branch: `Ashraful`. This is a **per-page UX + Final QA completion ledger**. Existing pages may already have partial implementation; unchecked means **NOT verified in this consolidated acceptance cycle**, not proof that the implementation is absent.

**Execution rule:** Select the FIRST unchecked route below, audit its related create/edit/detail/approval/report pages, complete all applicable UX/backend/AI-Voice/security improvements, execute required FINAL QA at the final commit SHA, and only then check the route. If tests fail or required QA is unavailable, leave unchecked and record `QA Pending / Blocked`; resume the same route next run rather than moving on. Record commit SHA, PASS/FAIL/BLOCKED/NOT RUN and affected shared regressions below. Update **only this lane's ledger**; other lanes' files are owned by their own tasks.

**Quality:** One shell title, no top gaps/duplicate notes, backend-generated immutable internal Codes, practical responsive forms/tables/reports, bn/en Help, appropriate shared Global Page Assistant (not per-page copies), server tenant/campus/role checks, concurrency/idempotency and source/report correctness. Run application build, targeted JS/.NET tests, applicable database/integration, actual mobile/browser checks; do not mark Verified Complete based on source-only assertions. Preserve existing APIs and history. Exact-code-SHA QA evidence is required.

**Assigned canonical menu pages: 41.** Related subpages are included in each route's verification, not counted again as separately assigned menu entries.

## platform:other (1)

- [x] `/Dashboard/Admin`

## platform:institutions (2)

- [x] `/PlatformTenants/Index`
- [x] `/PlatformCatalogAdministration/Index`

## platform:features (4)

- [ ] `/PlatformFeatureAdministration/Index`
- [ ] `/PlatformProductModuleAdministration/Index`
- [ ] `/PlatformInstitutionModulePresets/Index`
- [ ] `/PlatformModuleFeatures/Index`

## platform:subscriptions (3)

- [ ] `/SubscriptionPlanAdministration/Index`
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

## Current in-progress or blocked route

**Completed menu:** platform:institutions (2/2 Verified Complete). **Next UX A menu:** platform:features (4 routes), starting with `/PlatformFeatureAdministration/Index`; do not mark any next-menu page complete until its own exact-SHA UX/SQL/browser QA succeeds. Previously verified Dashboard and Institution pages remain part of shared-regression checks.
