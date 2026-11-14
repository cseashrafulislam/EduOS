# EduOS Final QA — exact-SHA acceptance matrix (2026-11-15)

## Scope and evidence rules
- Canonical branch: `Ashraful`. Evidence baseline before safety changes: `35d8f02399e5cd515062d7f65c04aefe8165e751`.
- [CI](https://github.com/cseashrafulislam/EduOS/actions/runs/43624050735): **PASS** on the baseline.
- [Unified independent QA](https://github.com/cseashrafulislam/EduOS/actions/runs/43624050686): **PASS**, JavaScript **1969/1969**, .NET **3724/3724**, EF snapshot/idempotent script/publish and eight synthetic Chromium suites PASS.
- [Disposable SQL Server smoke](https://github.com/cseashrafulislam/EduOS/actions/runs/43624050699): **PASS** on same baseline, including fresh/repeat migration, selected tenant/finance/security/domain SQL tests and backup/restore drill.
- [Academic browser smoke](https://github.com/cseashrafulislam/EduOS/actions/runs/43620664819) **PASS** at older SHA `79a1afed...`; do not count it as current exact-SHA browser evidence.
- Earlier 56 JavaScript and 61 .NET failures were addressed before this baseline. Historic failure results are not current release failures.
- Current changes improve authenticated academic QA safety, not production academic domain business logic. They need a new exact-SHA CI/QA result after commit.

## Nine final release gates

| # | Gate | Verified evidence | Still required before release |
|---|---|---|---|
| 1 | Authenticated staging | Existing opt-in academic, sidebar role and enterprise trace workflow definitions | Configure **isolated synthetic staging** test environment, approved host, credentials, tenant/campus/role fixtures, execute all on exact candidate SHA; record redacted results |
| 2 | Full menu and page QA | Static/synthetic sidebar/navigation and Chromium suites PASS | Enumerate all **204 declared canonical routes** against actual enabled entitlements; validate individual navigation, view/form/read-only export and forbidden responses without claiming routes not exercised |
| 3 | Whole-system ERP E2E | SQL module integration sets PASS | Single real SQL-backed institution onboarding → setup → admission → enrollment → attendance → fees → GL → reports, plus procurement → stock → assets → facilities → Finance trace and reconciliation |
| 4 | Security and tenancy | .NET authentication/tenant/permission regressions and selected SQL integration PASS | Authenticated multi-tenant/multi-campus role matrix with IDOR, negative APIs, exports, cache headers, session revocation and MFA scenarios on isolated staging |
| 5 | Concurrency and posting | Selected SQL idempotency/rollback/concurrency checks PASS | Full-chain double submit, competing write, retry after timeout, posted reversal, cash/GL and stock source balance invariants and rollback |
| 6 | Mobile, language, accessibility | Eight synthetic Chromium suite groups PASS | Authenticated 360/390/768/1280, bn-BD/en-BD, light/dark, keyboard, role-specific pages and report/print checks; collect route-level failures |
| 7 | Performance and reporting | Read-only performance probe harness exists; selective SQL tests PASS | Opt-in 40K synthetic student import and 5K/10K/50K supplier AP benchmark were **SKIPPED** on baseline; collect SQL actual plans, logical IO, p95/p99 and report reconciliation |
| 8 | Deployment/recovery | Fresh/repeat EF migrations, idempotent script, publish and disposable backup/restore PASS | Production-like environment rollout/rollback, restore checksum, mixed-version compatibility, audit/monitor alerts and rollback rehearsal |
| 9 | Institutional UAT | GitHub QA issue #139 remains open | Institution user/roles test canonical workflows; sign-off and explicit owner go/no-go |

## Executable staged browser evidence and safety
- Academic live workflow: `.github/workflows/academic-live-browser.yml` (manual dispatch, approved `staging` or `production` GitHub environment).
- Required GitHub environment variable: `EDUOS_E2E_EXPECTED_HOST` (bare DNS hostname, matching `EDUOS_BASE_URL`); credentials remain GitHub environment secrets. Only exact `Ashraful` SHA accepted.
- Academic live guard: `tests/browser/academic-live-guards.cjs`; unit tests: `EduOS.Tests/JavaScript/academic-live-guards.test.cjs`. It allows same-origin GET/HEAD/OPTIONS and **only exact login POST**; blocks foreign-origin requests and all other writes. Do not run mutation/UAT tests on production.
- Multi-tenant read-only staging: `.github/workflows/enterprise-trace-authenticated-staging.yml`; sidebar role staging: `.github/workflows/sidebar-role-staging.yml`. These use different synthetic fixtures and cannot certify the other nine gates merely because unit tests pass.
- Verify release evidence by matching `head_sha`, workflow run, job steps, TRX test counts, scope and environment. A skipped, earlier-SHA or synthetic-only check is NOT PASS for authenticated UAT.
- **Release decision:** BLOCKED until all required acceptance evidence is linked and independent institutional sign-off is recorded. Do not deploy, migrate production data or close #139 from these automated passes alone.
