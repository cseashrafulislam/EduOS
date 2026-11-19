# EduOS AI Workspace — Teacher Copilot, Student Tutor, Management Analytics

Branch: `Ashraful`. Status: code integrated; production release is NOT approved without the checks below.

## Features
- **Teacher Copilot** `/AiWorkspace/Teacher` — licensed LMS users with teacher/administrator rights can request a lesson-plan, worksheet or original practice-MCQ **draft** using their own topic, class and duration. Server verifies an active teaching employee for Teacher.
- **Student Tutor** `/AiWorkspace/Tutor` — enrolled Student can request a concept explanation and a self-check question. This is general tutoring, **not** retrieval of their gradebook, published curriculum evidence or an official answer. Use `/Library/StudyAssistant` for approved quoted/cited study excerpts.
- **Management Analytics** `/AiWorkspace/Management` — TenantAdmin/Principal reads authorized campus enrollment and finalized attendance **aggregates** from existing SQL tables; a separate, opt-in AI interpretation takes aggregated counts only, not row-level or student data.

No new data authority and no new database table. AI-generated responses never save/post marks, attendance, fees, grades, approval or student records. No automatic app-side prompt/response storage.

## Security and boundaries
- MVC and API endpoints re-enforce authentication, roles, LMS/ATTENDANCE entitlement and tenant scope. Management Principal requires an active `UserCampusAccess` for requested campus; TenantAdmin can see own institution campuses.
- Student eligibility requires their user-linked active student record and current enrollment. Teacher eligibility requires active employee/teaching assignment or tenant-level administrator role.
- Campus/date inputs are revalidated. Management summaries use only finalized `StudentAttendance` joined to its `AttendanceSession` and `StudentEnrollment` within the same tenant/campus.
- `CurrentActiveEnrollments` is a present-day count. Attendance counts are **entries**, not distinct students. Rate = `(Present + Late)/(Present + Late + Absent)`; `Leave` and `Excused` excluded. Missing denominator = no rate. Range capped at 31 calendar days.
- User-supplied topic text is bounded. Obvious email/Bangladeshi mobile patterns are blocked; this is **not** a comprehensive PII detector. UI warns users not to enter personal/confidential data. Only topic/class/question or aggregate numbers are sent to an external provider.
- All model calls originate server-side over fixed HTTPS, with no browser key. Requests specify `store: false`; this does not supersede provider contractual retention/privacy policies. No transcript history is persisted by the app.
- Model call timeout: 30 seconds; output cap 900 tokens; provider errors are sanitized; `AiGenerationPolicy` allows six model calls per authenticated user per ten minutes in a single app instance. Before broad rollout add durable per-tenant spend/quota enforcement and monitor costs (the in-process rate limiter is not distributed).

## Production configuration (server environment or managed secrets only)

| Name | Value |
| --- | --- |
| `EduOsAi__Enabled` | `true` to enable model generation; unset/false disables it |
| `EduOsAi__ApiKey` | provider secret, **never commit or log** |
| `EduOsAi__Model` | compatible OpenAI Responses API text model name |

The service sends `POST https://api.openai.com/v1/responses` with `max_output_tokens=900` and `store=false`. If the model isn't configured, the Teacher/Tutor buttons are disabled; management verified SQL metrics remain usable. Provider endpoint and credentials cannot be supplied by the browser.

## API

- `GET /api/ai/workspace/status`
- `POST /api/ai/workspace/teacher` (Teacher, Principal, TenantAdmin + LMS)
- `POST /api/ai/workspace/tutor` (active Student + LMS)
- `GET /api/ai/workspace/management/campuses` (Principal, TenantAdmin + ATTENDANCE)
- `GET /api/ai/workspace/management/summary?campusId=...&fromDate=YYYY-MM-DD&toDate=YYYY-MM-DD`
- `POST /api/ai/workspace/management/interpret` (same management permission)

All writes require the normal antiforgery token. The only POST effects are provider calls; they do not mutate ERP state.

## Release gate
1. `dotnet build EduOS.slnx` and `dotnet test` on the pinned .NET 10 SDK; run `node --test EduOS.Tests/JavaScript/ai-workspace-contract.test.cjs`.
2. Inspect and validate the environment key/model in staging; test explicitly **disabled** mode. Do not use real pupil data in provider smoke tests.
3. Test TenantAdmin, Principal, Teacher, Student and unauthorized users. Validate cross-tenant IDs, campus access revocation, expired/current enrollment, suspended employees, and legacy batchless StudentEnrollment records.
4. Test 429 limits, provider failures/timeouts, model format changes, session switch, XSS, empty/oversized/PII-bearing prompts, browser assistive access and mobile viewport.
5. Confirm production privacy notice, processor/data-processing agreement, school approval for external AI services, tenant spend ceilings, observability and rollback owner. No automatic model output posting or publish actions.
6. Check CI + SQL data correctness and complete authorized user UAT before enabling feature in production.

**Rollback:** Set `EduOsAi__Enabled=false` to disable model calls without data migration. To hide pages, revert this feature's MVC/sidebar changes. No newly created database schema or stored prompt data requires rollback.
