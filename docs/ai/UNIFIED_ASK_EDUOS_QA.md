# Ask EduOS — one Q&A page, local-first with optional institution-owned AI API

## End-user behavior
**Only one Q&A page**: `/AiWorkspace/Institution` (TenantAdmin + REPORTING access).
One prompt, one answer display; never ask users to choose separate "Local AI" and "API AI" pages.

1. Every submission calls `POST /api/ai/institution/ask` **first**, using the exact active tenant and explicit business-date range. This path is free of external AI, never requires API credentials, and calls the existing canonical MIS. It returns the authoritative amount/count, source table/report, definition and **Current/Period** scope.
2. If the answer is unavailable/forbidden, an integrity check fails, the selected module is disabled, or a date/filter is invalid, stop and display that failure. **Never** substitute model-generated numbers, even when the institution has a key.
3. Only a genuinely unknown alias returns "Unsupported question". The same page then offers optional general learning Q&A. A second backend action `POST /api/ai/institution/general` is called only after explicit unchecked-by-default per-request consent and positive `GET /api/ai/institution/status` (key configured and platform feature enabled). The server independently blocks institution-specific, private, long-ID, injection or high-risk prompts. This is an educational general-explanation **draft**, never a verified institutional figure.
4. No API key configured? The user can still use all authorized local MIS questions. Unsupported local questions get an actionable message to choose a suggestion; the system does not fake AI or silently charge EduOS.
5. General AI uses only the **authenticated institution's** encrypted key, fixed HTTPS provider URL, per-user 6 per 10 minutes limiter, SQL per-tenant UTC-day limit, no automatic ERP writes, and no user-key exposure.

## Why not store millions of literal Q&A rows?
A query catalogue scales via **canonical metric definition × reviewed question aliases/grammar × authorized explicit parameters**.
It must never invent date filters, owner identity, a query, or a reference report name from free-text. The bounded grammar already supports multiple English/Bangla wording variations for canonical MIS metrics, but **does not currently claim millions of independently implemented metrics or questions**. New question families must be added only with:
- A single original authoritative service/procedure/query definition (no duplicated business logic).
- Verified tenant/role/module/campus/date/status rules on the server, including absent/unavailable and current-vs-historical semantics.
- SQL projection, pagination, suitable indexes for large data and safe response limits when returning row sets.
- Explicit allowed filters; no arbitrary SQL or LLM-generated execution.
- Contract tests for Bangla/English variants, unauthorized tenant access, duplicate facts and financial accuracy.

## API and backwards compatibility
- The existing `POST /api/ai/institution/ask` schema/response remains unchanged; it is no longer restricted by the billable AI rate limiter. This preserves integrations that call local MIS.
- New `GET /api/ai/institution/status` contains booleans only.
- New `POST /api/ai/institution/general` is authorized, CSRF protected, rate limited and consent-required; the provider prompt includes **only the user's screened general question**, no institution database rows.
- Teacher Copilot, Student Tutor and Management Analytics are distinct role-specific workflows and retain their existing endpoints. They are not new local/cloud Q&A pages.
- Changes stay isolated to draft PR #140 until branch CI, full QA, SQL migration, two-tenant key isolation, role/browser and privacy verification succeed.

## Operational tests
- No key + active institution + supported metric: returns exact database value with correct definition and source, no outbound request.
- With a key + supported metric: still uses local MIS; does not consume model quota.
- With a key + unknown public concept + explicit consent: general AI may respond, marked as draft.
- No consent / no key / provider disabled / student tutor disabled: no external request.
- With a key + unsupported institution fee/attendance/student/exam question: explicitly refuses rather than hallucinating numbers.
- With a key + unavailable module or integrity error: preserve original 403/409 and no provider request.
- Test two active institutions and a logged-in tenant switch, provider retries/429, cross-session stale reply cancellation, XSS, mobile keyboard and screen reader.
