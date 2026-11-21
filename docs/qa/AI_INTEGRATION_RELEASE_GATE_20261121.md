# EduOS AI — release gate and QA evidence (updated 2026-11-21)

## Target and change scope
Target **draft PR #140**, branch `feature/ask-eduos-ai-hardening-20261120` into `Ashraful`. Do not approve production deployment based solely on source/CI success. Verify the *exact final PR HEAD SHA* after every fix and before merge.

Implemented code covers one local-first Q&A UI, single-metric canonical MIS, Bengali query catalogue, optional institute-owned OpenAI fallback, Teacher/Student/Management workflows, encrypted TenantAdmin provider key UI in onboarding + day-2 settings, optional Q&A browser dictation, controlled external AI capability switches, SQL UTC-day attempt cap and observed token telemetry. The actual supported local metric catalogue is reviewed and finite, not millions of separately implemented questions.

## Required test gates
1. **CI**: exact-head .NET 10 solution build, EF snapshot, all JavaScript contracts, xUnit, idempotent SQL script and publish. GitHub Actions workflow `ci.yml`.
2. **AI Product Language Gate**: exact-head language and content policy source checks `ai-product-language-gate.yml`.
3. **AI Tenant SQL QA**: isolated SQL Server 2022, disposable database `EduOS_AiSqlQa`, complete EF migrations, checks for all four AI migrations, tenant PK/FK, rowversion and token columns, plus idempotent migration script `ai-tenant-sql-qa.yml`. This workflow never targets the production database.
4. **Unified Full QA, Real Data Entry QA, SQL Server Migration Smoke**: current workflows target `Ashraful` by dispatch/push; if this PR changes release branch, run these on the exact merged release SHA before deployment. A green old `Ashraful` run does NOT prove new AI branch.
5. **Manual API+browser UAT**: TenantAdmin, Principal, Teacher, Student, denied roles, disabled LMS/REPORTING, expired memberships, two institute A/B sessions and keys, mid-session switch, role revoke, key rotate/clear, stale rowversion conflict, duplicate first create, 403/409 data refusal, negative invoice/currency integrity and current/period finance semantics. Browser viewport 360/390/768/1280, Bengali/English, keyboard/screen reader, microphone unsupported/error, transcript review and anti-CSRF.

## Controlled provider smoke testing
In a test institute with non-real learner data and managed encrypted keys:
- `EduOsAi__Enabled=false`, key missing, disabled general/teacher/management capability, Student Tutor disabled and consent unchecked must each cause **zero** provider egress.
- Correct institute key + general learning question + consent may call the fixed provider endpoint; local supported metric must always stay model-free even when key is present. A forbidden/integrity/unsupported institution metric must never be guessed by external AI.
- Verify provider 401, 429, invalid response, partial response, timeout and SQL quota unavailable; response messages must never leak a credential.
- Simulate two application instances and concurrent provider attempts: each tenant/day must never exceed SQL limit. Observed token telemetry need not equal actual billed usage; missing/timeout response cannot be assigned a fake zero cost.
- Check encrypted Data Protection key ring persistence across application restarts/nodes and backup/restore.

## Data migration, deployment and rollback
1. Back up production DB, secrets storage and Data Protection key ring. Apply migrations in disposable and staging SQL with `EduOsAi__Enabled=false`.
2. Verify additive deployment order: `20261121080000_AiDailyProviderCallBudget`, `20261121100000_TenantAiProviderCredentials`, `20261121110000_AiObservedTokenTelemetry`, `20261121120000_TenantAiAgentControls`.
3. Approve platform/student processor privacy notices, institute/provider ToS, model spend alert thresholds and reconciliation; enable only for explicitly prepared institutes with their own credentials. Optional free local Q&A should work when all AI keys are absent.
4. After exact merged-SHA CI/SQL/role/browser QA, authorize staging then production rollout with limited users; monitor outbound attempt rate, provider errors, verified-report response time, model token observations, missing observation coverage and user support.
5. Emergency stop: `EduOsAi__Enabled=false`. Do **not** drop nonempty tenant credential or usage tables to roll back; retain audit and restore plan.

## Evidence status
- CI/AI gate results must be read from the latest GitHub Actions runs for PR #140, not reused from older commits.
- Disposable SQL QA tests schema/migration, **not** actual cross-tenant authenticated API isolation or provider billing. Passing it is necessary but insufficient.
- Real browser UAT, staging provider smoke, production migrations and final merge/deploy are **not yet certified**.
