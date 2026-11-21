# EduOS AI Workspace — Teacher Copilot, Student Tutor, Management Analytics

Branch: `Ashraful`. Status: code integrated; production release is NOT approved without the checks below.

## Features
- **Teacher Copilot** `/AiWorkspace/Teacher` — licensed LMS users with teacher/administrator rights can request a lesson-plan, worksheet or original practice-MCQ **draft** using their own topic, class and duration. Server verifies an active teaching employee for Teacher.
- **Student Tutor** `/AiWorkspace/Tutor` — enrolled Student can request a concept explanation and a self-check question. This is general tutoring, **not** retrieval of their gradebook, published curriculum evidence or an official answer. Use `/Library/StudyAssistant` for approved quoted/cited study excerpts.
- **Management Analytics** `/AiWorkspace/Management` — TenantAdmin/Principal with Attendance.View permission reads authorized campus enrollment and finalized attendance **aggregates** from existing SQL tables; a separate, opt-in AI interpretation takes aggregated counts only, not row-level or student data.
- **Institution-wide Ask EduOS** `/AiWorkspace/Institution` — strictly tenant-admin + REPORTING authorized, bilingual exact-question lookup over canonical `ICentralInstitutionMisService` for fee dues, active employees/students, approved-unposted payroll, posted GL income/expenses/net surplus, finalized attendance, published result-version counts (not distinct exams/students), facilities and library. This preserves its authoritative module entitlements, data-integrity refusal, period/current distinctions and zero-versus-unavailable semantics. No duplicate domain SQL, model calls or AI data tables. `POST /api/ai/institution/ask` checks at most 366 business dates.
- **Ask EduOS verified metrics** `POST /api/ai/workspace/management/ask` — model-free bilingual question parser for exactly one allowlisted campus-scoped enrollment/attendance metric. The server re-executes the authorized SnapshotAsync with the explicit 31-day range. The answer labels enrollment counts as current and attendance values as finalized entries (not distinct students). Unsupported date/class/section/financial requests fail closed. No external AI provider or billing is needed.

AI has no new authoritative academic/financial data tables; it adds tenant-scoped credential storage and a SQL provider-attempt quota. AI-generated responses never save/post marks, attendance, fees, grades, approval or student records. No automatic app-side prompt/response storage.

## Security and boundaries
- MVC and API endpoints re-enforce authentication, roles, LMS/ATTENDANCE entitlement and tenant scope. Management Principal requires an active `UserCampusAccess` for requested campus; TenantAdmin can see own institution campuses.
- Student eligibility requires their user-linked active student record and current enrollment. Teacher eligibility requires active employee/teaching assignment or tenant-level administrator role.
- Campus/date inputs are revalidated. Management summaries use only finalized `StudentAttendance` joined to its `AttendanceSession` and `StudentEnrollment` within the same tenant/campus.
- `CurrentActiveEnrollments` is a present-day count. Attendance counts are **entries**, not distinct students. Rate = `(Present + Late)/(Present + Late + Absent)`; `Leave` and `Excused` excluded. Missing denominator = no rate. Range capped at 31 calendar days.
- User-supplied topic text is bounded. Obvious email/Bangladeshi mobile patterns are blocked; this is **not** a comprehensive PII detector. UI warns users not to enter personal/confidential data. Only topic/class/question or aggregate numbers are sent to an external provider.
- All model calls originate server-side over fixed HTTPS, with no browser key. Requests specify `store: false`; this does not supersede provider contractual retention/privacy policies. No transcript history is persisted by the app.
- Model call timeout: 30 seconds; output cap 900 tokens; provider errors are sanitized; `AiGenerationPolicy` allows six model calls per authenticated user per ten minutes in a single app instance. Before broad rollout add durable per-tenant spend/quota enforcement and monitor costs (the in-process rate limiter is not distributed).

## Production configuration — institute-owned keys

Each TenantAdmin configures the **institution's own** OpenAI API key, model, AI enable switch and separate Student Tutor opt-in in the existing **Account → Gateway Setup** page, beside SMS and Email. Secrets are protected using a tenant-specific Data Protection purpose, never returned after save and never committed to source. This replaces the old shared \`EduOsAi__ApiKey\` and tenant ID allowlist; there is no fallback to a platform-wide provider secret.

| Server setting | Purpose |
| --- | --- |
| \`EduOsAi__Enabled\` | deployment-wide emergency disable switch; off by default |
| \`EduOsAi__DailyCallsPerTenant\` | SQL-backed atomic maximum outbound attempts per UTC day; defaults to 20, range 1–100 |

Release supports **OpenAI Responses API** at a fixed HTTPS endpoint. Other providers require separately reviewed adapters; a custom tenant-supplied URL is not permitted. Per-request consent and role/tenant authorization remain required. No model-generated answer automatically mutates ERP records.

For secret entry, migration, tenant-isolation and rollout rules see [Institute-Owned Provider Credentials](TENANT_OWNED_PROVIDER_CREDENTIALS.md).

## API

- `GET /api/ai/workspace/status`
- `POST /api/ai/workspace/teacher` (Teacher, Principal, TenantAdmin + LMS)
- `POST /api/ai/workspace/tutor` (active Student + LMS)
- `GET /api/ai/workspace/management/campuses` (Principal, TenantAdmin + ATTENDANCE)
- `GET /api/ai/workspace/management/summary?campusId=...&fromDate=YYYY-MM-DD&toDate=YYYY-MM-DD`
- `POST /api/ai/workspace/management/ask` (authorized local-first metric question; no provider)
- `POST /api/ai/workspace/management/interpret` (same management permission)

All writes require the normal antiforgery token. The only POST effects are provider calls; they do not mutate ERP state.

## Durable provider call budget (migration required)
Deploy migrations `20261121080000_AiDailyProviderCallBudget` and `20261121100000_TenantAiProviderCredentials` before enabling the external model. `AiProviderDailyUsage` stores only tenant ID, UTC date, reserved call count and timestamp (no prompt, transcript, student or model response). SQL `sp_getapplock` serializes one short per-tenant daily reservation across application instances. The provider is called only if a reservation succeeds. Failed or timed-out calls still consume a reservation, since the provider may charge for them. These counters are **not a token-billing ledger or monetary price cap**; review token usage/model costs separately and disable the provider using `EduOsAi__Enabled=false` if budgets are at risk. The migration blocks destructive rollback while usage rows exist.

## Release gate
1. Build and test the complete .NET 10 solution, xUnit, Node contracts and EF model snapshot; validate idempotent SQL migrations.
2. With `EduOsAi__Enabled=false`, verify zero outbound calls. Enable only in disposable/staged environments after secret storage and durable key ring are configured.
3. Verify two tenants cannot read, replace or use each other's AI credentials; check rotating, clearing, stale versions, mid-session institution switch, permission revocation and failing/decryption errors.
4. Validate Teacher, Student and Management role/consent, campus access, 429 limits, timeouts, PII guards, provider rejection, browser/mobile UX and language behavior. Existing SMS/Email gateways must remain unaffected.
5. Approve processor agreements, school consent, model spend monitoring, backup/restore, production deployment and rollback owner.

**Rollback:** Disable external provider calls using `EduOsAi__Enabled=false`; never delete stored secrets as an automated rollback. Migrations reject rollback while their data exists.
