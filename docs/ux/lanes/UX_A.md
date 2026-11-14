# EduOS Unified UX — Lane A: Platform, Institution & Academic Setup

Source: [canonical route ownership](../UX_ROUTE_OWNERSHIP_20261114.json). Branch: `Ashraful`. This is a **per-page UX + Final QA completion ledger**. Existing pages may already have partial implementation; unchecked means **NOT verified in this consolidated acceptance cycle**, not proof that the implementation is absent.

**Execution rule:** Select the FIRST unchecked route below, audit its related create/edit/detail/approval/report pages, complete all applicable UX/backend/AI-Voice/security improvements, execute required FINAL QA at the final commit SHA, and only then check the route. If tests fail or required QA is unavailable, leave unchecked and record `QA Pending / Blocked`; resume the same route next run rather than moving on. Record commit SHA, PASS/FAIL/BLOCKED/NOT RUN and affected shared regressions below. Update **only this lane's ledger**; other lanes' files are owned by their own tasks.

**Quality:** One shell title, no top gaps/duplicate notes, backend-generated immutable internal Codes, practical responsive forms/tables/reports, bn/en Help, appropriate shared Global Page Assistant (not per-page copies), server tenant/campus/role checks, concurrency/idempotency and source/report correctness. Run application build, targeted JS/.NET tests, applicable database/integration, actual mobile/browser checks; do not mark Verified Complete based on source-only assertions. Preserve existing APIs and history. Exact-code-SHA QA evidence is required.

**Assigned canonical menu pages: 41.** Related subpages are included in each route's verification, not counted again as separately assigned menu entries.

## platform:other (1)

- [x] `/Dashboard/Admin`

## platform:institutions (2)

- [ ] `/PlatformTenants/Index`
- [ ] `/PlatformCatalogAdministration/Index`

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

### Exact-SHA QA evidence — 2026-11-14

- **Build and tests:** `dotnet restore EduOS.slnx`, Release full-solution build, filtered xUnit `DashboardServiceTests|PlatformDashboardServiceTests`, dashboard Node contracts, reviewed bilingual Dashboard Page Help Node tests and browser script syntax all PASS on the tested commit.
- **Real SQL/browser:** clean, disposable `EduOS_Demo_DashboardQA` SQL Server 2022 database; EF migrations and SuperAdmin seed PASS, isolated Staging demo tenant/role seed PASS. Real Chromium test script `tests/browser/dashboard-live.cjs` PASS in all six logged scenarios: anonymous access blocked, SuperAdmin English desktop and Bangla mobile, TenantAdmin, Teacher and Student login, role-scoped Dashboard API data, platform API denial, no cross-role private values, expected navigation, accessibility elements, no horizontal overflow, shared Page Help and one shared page-assistant launcher/voice entry.
- **QA fixes tested:** compact bilingual admin dashboard and removed obsolete Refresh; canonical Dashboard service retained; bilingual page manual and its tests aligned; `ErrorController` preserves denied 403/no-store; `Program.cs` skips MVC error-page re-execution on `/api` so private endpoints retain HTTP error contracts. Browser tests use actual Login Razor form and its anti-forgery wrapper; authenticated API requests use that browser session, not a cookie-independent API client.
- **Scope:** Dashboard-specific verification is complete. The repository-wide **CI** workflow on this commit still reports FAILURE due to other JavaScript contracts; the separate general **SQL Server Migration Smoke** run was in progress when this acceptance evidence was recorded. Those are **product-wide release blockers and must not be described as a fully green repository/release gate**. This scoped page sign-off does not certify unrelated modules, load benchmarks, production institution UAT or deployment.
- **Automation:** remains OFF per owner's earlier instruction. No production database or existing tenant data was changed by these QA fixtures.

## Current in-progress or blocked route

**Both Dashboard routes Verified Complete.** Per canonical menu order, the next unverified UX A menu is **platform:institutions** and its first page is `/PlatformTenants/Index`. Do not rework either Dashboard without new defects; preserve their focused browser/SQL regression workflow whenever shared authorization, layout, dashboard services or error handling changes.
