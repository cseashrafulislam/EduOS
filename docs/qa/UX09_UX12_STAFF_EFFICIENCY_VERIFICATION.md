# UX-09 / UX-12 independent F01–F12 verification ledger

Evidence base: `Ashraful` commit `acc060e434d1916b18ed88b5278816a5aaedc413`, reviewed 2026-11-12. This ledger is owned by independent QA; domain owners retain feature code. The added Node source-contract suite is executable in the canonical CI workflow, but **static route checks are not authenticated integration tests**. A green CI does not establish tenant, campus, bilingual, mobile or SQL transaction acceptance.

| ID | Existing source evidence / classification | Independent behavior acceptance still required |
| --- | --- | --- |
| F01 | Academic/student dependent setup exists; **Improvement needed / not independently proven** | Eligible year/program defaults; multi-tab stale change; zero/one/many candidates; no guessed fee |
| F02 | `StudentGuardiansController` existing create/link/unlink/reactivate; **Already supported route; behavior pending** | Two siblings, one existing guardian, explicit confirmation, revoked consent, cross-tenant IDOR, concurrent link retry |
| F03 | Student/import identity checks exist; **Improvement needed / unverified** | Minimal PII candidate suggestion, true unique key, parallel inserts and retry; no cross-tenant match |
| F04 | `StudentImportsController` headers/preview/errors/execute/job-status/cohort-preflight; **Already supported routes; behavior pending** | Column aliases, invalid rows, idempotent clientRequestId, simultaneous commit, tenant-safe error CSV |
| F05 | `StudentAttendanceController` roster/session/save/finalize/correct; **Already supported routes; behavior pending** | Opening roster must not write Present; explicit date/session/roster confirmation; atomic save; locked history |
| F06 | Finance invoice posting endpoint and fee workflows; **Improvement needed / unverified** | Posted due, cash/bank choice, one balanced GL receipt, retry/partial/overpayment/reversal |
| F07 | Student progression workflow exists; **Improvement needed / unverified** | Old academic year remains queryable, promoted cohort and sibling links correct, retries safe |
| F08 | Existing guardian and student views; **Improvement needed / unverified** | Role/campus/tenant-limited Student 360, no guardian/medical PII leak |
| F09 | Work Center is canonical; **Improvement needed / unverified** | Authorized actionable inbox, stale assignment and completion state, no duplicate work queue |
| F10 | Smart Setup existing; **Improvement needed / unverified** | Organization-type seed defaults only with confirmation, idempotent tenant/campus setup, no overwrites |
| F11 | Shared forms and page assistance existing; **Improvement needed / unverified** | Draft recovery, explicit consent, mobile viewport, Bengali/English, session/tenant rotation |
| F12 | `AiVoiceAttendanceController` read-only attendance preview; **Already supported limited preview; broader voice unverified** | Untrusted transcript, preview-before-execute, rejected unauthorized intent, voice no bypass of original save |

## Required authenticated matrix (NOT RUN)
For each of the F01–F12 workflows record exact route, SHA, actor, tenant, campus, permission, locale, viewport, request ID and result. Actors: TenantAdmin, Principal, Teacher, Accountant, Student, Guardian and denied/unentitled role. Contexts: own campus, other campus same tenant, foreign tenant, revoked membership, rotated session. Devices: Chromium 390x844, 768x1024, 1440x900; locales bn-BD/en-BD; keyboard-only and screen-reader labels. Verify backend denial (403/404) rather than merely hidden controls. No production data writes; use isolated seeded test tenants.

## Performance gate (NOT RUN)
Capture reproducible SQL Server execution plans, actual logical reads, DB round trips, EF query count, response p75/p95 and browser LCP/interaction p75/p95 **before and after** any optimization. Measure identical tenant/campus/role/filters with warm and cold cache separated, 25/100/500 row pages and concurrent request load. Do not assert gains from source inspection, synthetic DOM or a green unit test. Do not add unsafe cross-tenant caching.

## Run outcome
Source-only route contract checks authored; execution is pending CI. No authenticated Chrome, API, EF migration or SQL execution was performed in this connector-only run. No feature completion claim, no numeric performance improvement claim. Domain implementation defects must be assigned to UX2/UX3/UX4 owners rather than introducing a duplicate backend.
