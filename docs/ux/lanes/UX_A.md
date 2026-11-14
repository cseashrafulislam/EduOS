# EduOS Unified UX — Lane A: Platform, Institution & Academic Setup

Source: [canonical route ownership](../UX_ROUTE_OWNERSHIP_20261114.json). Branch: `Ashraful`. This is a **per-page UX + Final QA completion ledger**. Existing pages may already have partial implementation; unchecked means **NOT verified in this consolidated acceptance cycle**, not proof that the implementation is absent.

**Execution rule:** Select the FIRST unchecked route below, audit its related create/edit/detail/approval/report pages, complete all applicable UX/backend/AI-Voice/security improvements, execute required FINAL QA at the final commit SHA, and only then check the route. If tests fail or required QA is unavailable, leave unchecked and record `QA Pending / Blocked`; resume the same route next run rather than moving on. Record commit SHA, PASS/FAIL/BLOCKED/NOT RUN and affected shared regressions below. Update **only this lane's ledger**; other lanes' files are owned by their own tasks.

**Quality:** One shell title, no top gaps/duplicate notes, backend-generated immutable internal Codes, practical responsive forms/tables/reports, bn/en Help, appropriate shared Global Page Assistant (not per-page copies), server tenant/campus/role checks, concurrency/idempotency and source/report correctness. Run application build, targeted JS/.NET tests, applicable database/integration, actual mobile/browser checks; do not mark Verified Complete based on source-only assertions. Preserve existing APIs and history. Exact-code-SHA QA evidence is required.

**Assigned canonical menu pages: 41.** Related subpages are included in each route's verification, not counted again as separately assigned menu entries.

## platform:other (1)

- [ ] `/Dashboard/Admin`

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

- [ ] `/Dashboard/Index`

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

| Route | Final commit SHA | Build / JS / .NET / Browser / SQL | Roles / Tenant / Device / Locale | Result / Notes |
| --- | --- | --- | --- | --- |

## Current in-progress or blocked route

**Selected menu:** platform:other (1 route). **Current route:** `/Dashboard/Admin` — **Implemented / Final QA Pending / Blocked**. The route stays **[ ]**, and no following route or menu is started.

### 2026-11-14 — SuperAdmin Platform Dashboard implementation checkpoint

- **Branch:** `Ashraful`. **Last application-code commit:** `75278a2497e7865e4108c68f2767d0c21e87d14c` (verified GitHub commit readback).
- **Changes pushed:** `d8497e9ca74c6029474c9e6c8e2714973e0f5947` (compact server-rendered operations dashboard, no duplicate header/decorative Refresh, real empty states and four operational navigation links), `ec731695b410c6bff5cb6d008b254bb9d9d64754` (Bangla tenant/onboarding/subscription labels), `4d9468e1dc3feaee52edad6727e955743cc53a91` (five regression tests), `921fd00114a61b9515fd60e5c4ba8f0289e4ac67` (responsive action card layout), `75278a2497e7865e4108c68f2767d0c21e87d14c` (remove unused refresh-only JavaScript file). No service/API, schema, account, subscription, finance or production data changes.
- **Existing safeguards retained:** MVC and API SuperAdmin role restriction; dashboard service's authenticated `IsSuperAdmin` check; server-side no-store caching; source-backed counts including nullable pending payment count; bounded recent tenant and expiry lists; no new user-controlled Code or tenant data input. The four action destinations have actual SuperAdmin controller actions.
- **PASS — source-level only:** All five focused contract assertions executed against GitHub `Ashraful` file snapshots in a V8 harness with mocked filesystem, checking authorization/no-store source contracts, eight real metrics, compact shell, bilingual labels, empty states/accessibility markup, and existing action routes. All five PASS. This is **not** Node repository test execution or runtime UI verification. GitHub file readback after updates PASS.
- **BLOCKED:** local git clone/ls-remote cannot resolve github.com; `dotnet` executable is absent in the current execution environment. Commit combined status endpoint reports no checks for application-code commit `75278a2497e7865e4108c68f2767d0c21e87d14c`.
- **NOT RUN:** Release build, actual `node --test` repository suite, .NET unit/integration/tenant/security tests, authenticated SQL integration, page service live data, desktop/mobile browser rendering, bn-BD/en-BD and light/dark, accessibility, and Global Page Assistant/Voice smoke tests. No production/UAT certification.
- **Next on this same page:** restore a runnable GitHub checkout + .NET SDK + controlled SQL/browser environment; run named source test using Node, full build/service and browser/tenant/role/locale/voice checks, fix discovered issues, and only then mark this route Verified Complete. Do not proceed to `/PlatformTenants/Index` or other menu routes until required final gates PASS.
