# EduOS — institute-owned AI, SMS and Email credentials

## Setup and day-2 administration
- **Onboarding:** `/Account/GatewaySetup`.
- **Later changes:** `/CommunicationAdministration/Index`.
- Both views reuse `EduOS.App/Views/Shared/_TenantAiProviderSettings.cshtml` and `wwwroot/js/ai/provider-settings.js`. The existing independently-owned SMS gateway API key and Email SMTP account/password flows are unchanged.
- TenantAdmin can configure a **private OpenAI Responses API key**, model, provider enable switch, and a separate **Student Tutor** opt-in. There is **no shared `EduOsAi__ApiKey` fallback**, no trusted tenant ID from a browser payload, and no tenant-supplied arbitrary provider endpoint.
- `GET /api/ai/provider-settings` returns provider, model, enabled flags, `hasApiKey` and rowversion; never plaintext, ciphertext or partial secret. Empty edit retains current key. Update rotates key. Disable+clear deletes saved key.
- Key ciphertext uses ASP.NET Core Data Protection with a tenant-bound purpose. All instances must share a durable **encrypted and backed-up** key ring. Loss of key ring causes providers to fail closed, not expose a fallback key.
- `PUT /api/ai/provider-settings` requires verified live TenantAdmin membership, CSRF token and bounded input. Existing rows require matching rowversion; first-create uniqueness and database rowversion prevent lost updates. Role, tenant and permission checks remain server-side.

## Tenant-owned AI capabilities
The reviewed, code-owned registry is extensible without a fixed platform agent count. Current optional external capabilities:
- `general`: optional unverified general-knowledge Q&A explanation.
- `teacher`: Teacher Copilot draft lesson/worksheet/practice questions.
- `management`: interpreted aggregate management data.

`GET /api/ai/provider-settings/agents` and `PUT /api/ai/provider-settings/agents/{code}` allow a TenantAdmin to enable/disable reviewed capabilities with optimistic concurrency. No arbitrary executable agent code, SQL, prompt definitions or tools can be created by tenant input. Student Tutor's **single** authoritative enable switch remains `StudentTutorEnabled` under provider settings. Local canonical MIS Q&A remains available regardless of provider key, agent toggles or model outage.

## Usage and token observations — not a billing ledger
- `GET /api/ai/provider-settings/usage?days=30` shows tenant-scoped SQL reservations and **provider-observed** input/output tokens for 1–90 UTC days; the shared settings UI displays last 30 days.
- The server reserves at most `EduOsAi__DailyCallsPerTenant` model attempts per UTC day using `sp_getapplock` and a short transaction. Default 20; allowed 1–100. The SQL Server UTC day returned by the reservation is reused for provider-observed token telemetry, even if local time differs.
- Provider non-200, timeouts, missing usage JSON and SQL recording failures can omit billed tokens. Counts are **not** provider charges, a monetary hard cap or an invoice. Review the institute's actual provider dashboard before budgeting/settlement. Do not estimate money using floating-point JavaScript.
- Prompt text, speech transcripts, model answers, student details and raw keys are **not persisted** in the AI quota/telemetry tables.

## Schema / deployment
Apply **additive** migrations in order:
1. `20261121080000_AiDailyProviderCallBudget`
2. `20261121100000_TenantAiProviderCredentials`
3. `20261121110000_AiObservedTokenTelemetry`
4. `20261121120000_TenantAiAgentControls`

The PR includes `.github/workflows/ai-tenant-sql-qa.yml` and `EduOS.Tests/App/AiTenantSqlMigrationTests.cs` to apply all migrations and verify schema on a disposable SQL Server. Use staging backup/restore and institute A/B negative access and provider smoke tests **before** any production migration. Keep `EduOsAi__Enabled=false` during rollout. No production release is approved just because CI passes.

**Rollback:** Platform switch `EduOsAi__Enabled=false` stops outbound model calls without deleting credentials. Migrations guard rollback with data; do not drop tables containing tenant secrets, observability history or agent preferences.
