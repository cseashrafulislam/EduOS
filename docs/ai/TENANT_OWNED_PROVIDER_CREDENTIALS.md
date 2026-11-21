# EduOS — Institute-Owned AI, SMS and Email Credentials

## Where an institute enters credentials
TenantAdmin opens **Account → Gateway Setup** (`/Account/GatewaySetup`). Existing SMS provider API URL, sender ID and password/key, and Email SMTP host, login and app password are unchanged. A new **AI Provider** section supports **OpenAI Responses API only** for this release (other AI providers are not advertised as compatible).

Each institute enters:
- AI model name and its own OpenAI API key; optionally enables AI and separately enables Student Tutor.
- Its own SMS API key and provider parameters (already implemented).
- Its own SMTP login and password (already implemented).

The AI section is optional and cannot block existing onboarding completion or SMS/Email configuration. The provider bills the institute's own API account. EduOS currently reserves up to 20 attempts/day/tenant by default; this counter is NOT monetary spend or token billing.

## Security and data ownership
- GET `/api/ai/provider-settings` returns provider, model, `hasApiKey`, flags and rowversion only. It **never returns the key, ciphertext, key suffix or unmasked password**. The key input is blank after a successful save; blank during edit retains the existing key.
- PUT `/api/ai/provider-settings` is TenantAdmin-only, active tenant membership verified by the server, CSRF-protected, rate-limited, no-store and limited to 8 KB. The server derives TenantId from the authenticated session, never the JSON payload.
- A unique TenantId PK, tenant-scoped query, and mandatory rowversion for update prevent cross-tenant access and lost updates. First-time concurrent creation returns a 409 conflict.
- The provider secret is encrypted with ASP.NET Core Data Protection, using a separate tenant-specific purpose. The production key ring must persist securely and be backed up. Do not store raw secrets in Git, browser storage, audit details, API responses or logs.
- Key rotation: enter a new key and save. Key removal: disable AI, choose remove key and save. Existing key is preserved on ordinary edits.
- Fixed outgoing HTTPS endpoint `https://api.openai.com/v1/responses`; no tenant-controlled AI URL, DNS or proxy destination. Only the configured model identifier is tenant-controlled.
- Platform feature kill switch `EduOsAi__Enabled=false` stops external AI even for institutions with keys. When enabled, generation resolves only the **authenticated tenant's** encrypted key: there is **no shared EduOsAi__ApiKey fallback** and no global tenant ID allowlist. Student Tutor requires both tenant opt-in and per-request consent. All previous tenant/campus/role checks, server-side request consent, SQL usage quota and non-write rules remain.
- Authentication/recovery emails sent by the EduOS platform may still use separate platform-level operational SMTP configuration. Institution messaging gateway settings are not a replacement for the platform's own auth infrastructure.

## Deployment and verification
1. On the feature branch, run the full .NET 10 build, xUnit, JavaScript contracts, AI language gate and EF snapshot verification. Do not merge merely because source checks pass.
2. Apply additive migration `20261121100000_TenantAiProviderCredentials` **after** `20261121080000_AiDailyProviderCallBudget` to a disposable and then staged SQL Server. Check idempotent migration, backup and rollback strategy. The new table is not included as a tracked EF entity; it is a dedicated SQL settings store, like the existing provider quota.
3. Keep `EduOsAi__Enabled=false` during migration. Configure durable Data Protection keys consistently across all app instances. Only then enable the deployment flag on staging.
4. For two test institutions A and B, verify A saves key A, B saves key B, and neither reads or invokes with the other's key. Test ordinary edit retains key, rotation, removal, stale rowversion 409, duplicate first create 409, revoked TenantAdmin 403, switching institution mid-save, and missing/damaged key fail-closed without provider request.
5. Validate SMS and Email forms still save/retain secrets independently; AI database failure must not break SMS/Email setup. Test Teacher/Student/Management AI with explicit consent, disabled provider, Student Tutor denied, provider 401/429/timeout, quota limit and concurrency.
6. Inspect privacy review, school/guardian permissions for learner use, real provider billing limits, and backup/restore of secrets. Production merge/cutover requires operator approval.

### Code map
- Migration: `EduOS.Persistence/Migrations/20261121100000_TenantAiProviderCredentials.cs`
- Credential service: `EduOS.Service/Services/AI/TenantAiProviderSettingsService.cs`
- API: `EduOS.App/Controllers/Api/TenantAiProviderSettingsController.cs`
- Page: `EduOS.App/Views/Account/GatewaySetup.cshtml`
- Browser: `EduOS.App/wwwroot/js/account/gateway-setup.js`
- External generation: `EduOS.Service/Services/AI/EduOsAiWorkspaceService.cs`

**Release status**: Code under review in the AI feature branch, not merged/deployed and not considered production-ready before all gates above.
