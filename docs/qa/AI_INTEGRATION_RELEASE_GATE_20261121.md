# EduOS AI Integration — release gate (2026-11-21)

Branch `feature/ask-eduos-ai-hardening-20261120` → Draft PR #140 → `Ashraful`.

## Changes implemented in source
- Explicit tenant allowlist `EduOsAi__ApprovedTenantIds` and per-request user consent for any outbound Teacher/Tutor/Management generation. Student Tutor needs a second opt-in `EduOsAi__StudentTutorEnabled=true`.
- New SQL-backed tenant UTC-day model-call attempt ceiling: migration `20261121080000_AiDailyProviderCallBudget` and short transactional `sp_getapplock` counter update; default 20/day (config `EduOsAi__DailyCallsPerTenant`, valid 1–100). No prompts/student data in quota table. Fail-closed if DB quota unavailable.
- Tenant/campus/permission-controlled local Management questions for finalized attendance entries and current enrollment; no provider calls, no claims of distinct students in attendance marks.
- TenantAdmin-only, REPORTING-entitled Institution Ask EduOS page. Exact bilingual queries map to the existing `ICentralInstitutionMisService`, not bespoke finance/HR/fee SQL. MIS includes current published exam *result-version record* count, not unique exam/learner success metrics. Monetary values travel to browser as exact decimal strings.
- Existing global on-device speech Page Assistant now offers a separate explicit Review command against the existing, authenticated, anti-CSRF shared page-intent preview endpoint. Localized field/filter drafts are opt-in and require confirmation, without submission. Select previews focus only; no auto-changing dependent controls.

## Acceptance evidence and blockers
- Source and executable JS syntax smoke checks performed on changed browser scripts; structural assertions passed in review. This is **not** a substitute for .NET compilation, API functional tests or real browser UAT.
- GitHub PR checks (CI and AI product-language gate) currently end in failure with no job steps returned for the observed runs. The cause is unverified, and these checks must be diagnosed in GitHub Actions before code release. Do not assume an application compile failure or QA pass.
- No production database migration, provider-key activation, actual user acceptance, mobile/assistive accessibility testing or tenant stress test has been performed here.

## Mandatory release commands (run on the exact PR HEAD)
```bash
dotnet restore EduOS.slnx
dotnet build EduOS.slnx -c Release --no-restore
dotnet test EduOS.Tests/EduOS.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~InstitutionMisQuestionPlannerTests|FullyQualifiedName~AiInstitutionMetricServiceTests|FullyQualifiedName~ManagementQuestionPlannerTests|FullyQualifiedName~CentralInstitutionMisServiceTests|FullyQualifiedName~ReviewedVoicePageIntentCompilerTests"
node --test EduOS.Tests/JavaScript/ai-workspace-contract.test.cjs EduOS.Tests/JavaScript/ai-institution-contract.test.cjs EduOS.Tests/JavaScript/ai-page-intent-review.test.cjs EduOS.Tests/JavaScript/ai-provider-budget-contract.test.cjs
dotnet ef migrations has-pending-model-changes --project EduOS.Persistence --startup-project EduOS.App --context EduOSDbContext
dotnet ef migrations script --idempotent --project EduOS.Persistence --startup-project EduOS.App --context EduOSDbContext --output /tmp/eduos-ai-migration.sql
```

## SQL, security and operations acceptance
1. On a disposable database, back up and restore before/after applying the idempotent migration. Confirm `AiProviderDailyUsage` composite PK, FK, check constraint; verify safe reverse migration refusal when rows exist.
2. Simulate 100 concurrent requests across **two** application instances at the same tenant+UTC day: successful reservations must never exceed the configured cap, no duplicate per-day row; check independent tenants. Repeat across day boundary and when SQL is unavailable.
3. Verify ModelOff, no/mismatching ApprovedTenantIds, unchecked consent, disabled StudentTutorEnabled, missing quota migration and exhausted calls cause **zero** outbound requests. Verify timeout/error handling and anonymized provider logs. Test duplicate HTTP retry conservatively consumes capacity and cannot exceed it.
4. TenantAdmin, Principal, Student, Teacher, Accountant and unentitled roles across two seeded tenants and campuses: unauthorized routes must return 403/404. Cross-tenant enrollment, finance and assessment data must never be visible.
5. Verify current vs historical semantics; `ResultPublicationState.Published` version count; unavailable module stays null/unavailable; decimals match canonical original report without Javascript Number conversion; GL integrity 409 fails closed.
6. Browser Chromium phone/tablet/desktop, bn-BD/en-BD, local speech installed/unavailable, keyboard/screen reader, session rotation, stale abort, malicious transcript, controls with dependent selections. No page or business mutation before explicit confirmation, and never a posting/approval endpoint.
7. Production approval: institution/legal consent for third-party AI (especially minors), secrets in environment only, model-token monetary spend monitoring and alerting, max 900 output tokens, versioned prompts, access logs without request content. The daily call counter is **not** an actual monetary spend ledger.

## Remaining restrictions
- Natural language support is intentionally a reviewed, finite grammar. Arbitrary ERP SQL, unknown metrics, prompts that try to change tenant/campus/date scope, official mark generation and AI-driven auto-posting are not supported.
- Exam question output in Teacher Copilot is an unapproved practice draft; it must not silently enter the official assessment question bank.
- Per-page Voice field edits are limited to opted-in low-risk controls. Full all-page/role/locale integration cannot be certified until the route-by-route matrix and UAT passes.
- Provider-ready status depends on actual server config. Do not enable external AI in production prior to privacy approval, migration and QA.
