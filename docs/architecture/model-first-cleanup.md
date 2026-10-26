# EduOS model-first cleanup tracker

Target branch: `Ashraful`. Canonical entity declarations under `EduOS.Core/Entities` are the source of truth. The expected chain is entity -> DTO -> repository/query -> service -> API -> MVC -> Razor/JavaScript. This document tracks verified changes and live dependencies; it does not certify that every model has a complete UI.

## Model-first cleanup batches: Students, authentication, Tenant onboarding and LMS

- Consolidated eight student workflow DTO files into `EduOS.Core/DTOs/Students` and the `EduOS.Core.DTOs.Students` namespace. Updated all service interfaces, service implementations, API controllers, certificate view, and test consumers as one batch. No JSON property names, routes, or database columns were changed.
- Removed `EduOS.Service/Services/Students/StudentService.cs`, a wholly commented-out, unregistered old implementation. Active student management uses the registered domain services.
- Removed unused unbounded `GetAllAsync`/`FindAsync` methods and unused direct hard-delete helpers from generic repository contracts and implementation. Authorized domain-specific reversal and soft-delete workflows remain unchanged.
- Added architecture regression tests for canonical student DTO location and generic repository surface.
- Replaced the misleading login-only `Email` request field with `UserNameOrEmail` across the login DTO, API, browser script and form. Password reset and verified contact emails still use actual `Email` fields. The `/api/auth/login` JSON field is now `userNameOrEmail`; there is no legacy login alias.
- Removed unregistered `IAuthenticationService` and `IMfaEnrollmentService` abstractions and their unconsumed token/response DTOs. Renamed the remaining MFA DTO file for its actual active security action models; real cookie authentication and MFA endpoints remain in the active `AuthController`.
- Migrated institution profile onboarding off the `InstitutionProfileWizard*` compatibility layer to canonical Tenant contracts: `TenantOnboardingProfileDto` (`DTOs/SaaS`), `ITenantOnboardingProfileService` and implementation, API `tenant-profile` route, and model-aligned form bindings. The `Tenant.Name`/`Tenant.InstitutionTypeDefinitionId` fields now cross every layer without type-code translation. The canonical profile service exposes and saves existing `Tenant.LegalName` and `Tenant.RegistrationNumber` fields; the UI also displays tenant code, subdomain and verified email read-only. Campus onboarding contracts live under SaaS and academic year/term onboarding contracts under Academic, each using their entity names.
- Unified LMS quiz answer submissions to `SelectedOptionIds` in the API DTO, selection validation, and active learner JavaScript for both single- and multiple-choice questions. Removed the unused monolithic `quizzes.js` script and added API/UI contract regression tests. The existing `QuizAnswer.SelectedOptionId` entity column is intentionally retained for persisted single-choice answers and review/idempotency; it is not an old API alias. Historical clients sending only `selectedOptionId` must upgrade to the single `selectedOptionIds` request shape.

## Remaining live compatibility and migration items

| Area | Verified dependency | Required migration before removal |
| --- | --- | --- |
| Inventory catalog | `InventoryCatalogLocationReads` preserves an array response for old callers | Migrate every response consumer to bounded paging, then retire array mode |
| Transport | Old route/vehicle rows without campus assignment are explicitly rejected pending controlled repair | Migrate historical data through approved tenant-aware remediation, not by deleting validation |
| Authorization | `PermissionAccessHandler` and tenant role resolver retain protections for older global role grants | Migrate grants to tenant memberships, audit privilege equivalence and test isolation first |
| Content security | CSP permits some inline script/style for older pages | Move remaining inline behavior to CSP-compliant assets before tightening policy |
| Certificates | Older certificates lack issued name/code snapshots | Preserve explicit legacy fallback; historical identities must not be fabricated |

## Gate for each remaining workflow

1. Confirm the canonical entity name, relationships, tenant ownership and all persisted fields.
2. Identify every repository, service, controller, view, JavaScript, background job and test caller before renaming a public symbol.
3. Change the full call chain in one coherent batch. Do not introduce aliases that make the old flow active indefinitely.
4. Enforce tenant isolation, field validation, anti-forgery and audit requirements. Do not expose protected identifiers in UI/logging.
5. Run solution build and the full test suite; add regression tests for each security boundary or API contract change.
6. Review database migrations separately; never silently drop or rewrite deployed data or migration history.

Status: **partial cleanup complete; full project coverage still open**. The remaining items require their own dependency-complete migrations and UI verification.
