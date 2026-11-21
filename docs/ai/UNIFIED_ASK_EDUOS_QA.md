# Ask EduOS — one Q&A page: verified local MIS first, optional external model

## User journey
- One Q&A page at `/AiWorkspace/Institution`. No separate local/cloud Q&A page. The form accepts a question, explicit From/To business dates, and an optional unchecked consent checkbox.
- **Every question uses local reporting first.** `POST /api/ai/institution/ask` runs authenticated TenantAdmin/REPORTING authorization, strict bilingual intent matching and a **single requested metric** from existing canonical `ICentralInstitutionMisService.GetMetricAsync`; no key, model, external request, provider attempt quota or duplicated business data.
- `GET /api/ai/institution/catalog` returns a bounded, metadata-only catalogue of reviewed Bengali/English question aliases. The UI offers these as suggestions. Normalization is applied consistently to Bengali question input and aliases. Millions of hardcoded Q&A records are *not* present; actual supported questions are finite reviewed intents that can be expanded without arbitrary SQL.
- A verified answer includes the exact decimal-as-string (or count), source, definition, selected period, and **Current** vs **Period** meaning. "Unavailable" for a disabled module is **not** zero.
- An unsupported question may optionally route to `POST /api/ai/institution/general` for a general learning concept **only if** a private institute-owned OpenAI key exists, provider is enabled, general capability is enabled, and the user explicitly consents for that request. The server applies an allowlisted general-question policy, rate limit and tenant SQL daily attempt quota. No institution table rows, secrets, fee/marks/attendance/student records or account details are sent. Model text is an **unverified draft**.
- Known/local module unavailable, GL integrity errors, invalid date, forbidden request, or any other refusal: do **not** fall back to a generative answer. Never invent institutional figures.

## Performance and reporting integrity
`GetMetricAsync` and the existing full-report `GetAsync` reuse the SAME canonical definitions and server permissions. Single-metric questions avoid querying unrelated Student, Attendance, Finance, Payroll and GL sources; fee-invoice operational due does not require a full GL income calculation. The full MIS report and its API remain unchanged. No LLM-generated SQL and no arbitrary table, tenant or date selection.

## Optional microphone
The same page offers browser speech-to-text **only when the browser supports it**. The transcription provider may be browser/vendor managed. The transcript populates the existing question input, invalidates stale answers, and requires explicit manual review and **Get answer**. It never checks AI consent or submits a record automatically, and EduOS does not upload voice audio in this flow.

## APIs
| Method | Path | Behavior |
| --- | --- | --- |
| GET | `/api/ai/institution/catalog` | Reviewed question aliases and metric IDs (no tenant data) |
| GET | `/api/ai/institution/status` | Local and general-model readiness booleans only |
| POST | `/api/ai/institution/ask` | Verified key-free canonical MIS metric |
| POST | `/api/ai/institution/general` | Separate protected provider operation used from the **same page** |

All endpoints require session-appropriate authentication, TenantAdmin/REPORTING where indicated, CSRF protection for POST and tenant-aware server checks. The external operation alone consumes the stricter AI-generation limiter.

## Scope boundaries and expansion
The catalogue presently covers reviewed institution metrics (students, attendance, fees, posted GL, payroll, inventory, HR, library, hostel, facilities and transport), not millions of independent reports. Future questions must map to a verified canonical report DTO/service, server-owned tenant/module permissions, explicit filters and tested Current/Period semantics. Large row sets require SQL projection/server paging. No database Q&A row proliferation or new editable source of truth.

## Release acceptance
Test with and without a key, institute A/B isolation, stale session, 403/409 refusal, disabled modules, exact finance decimals, Bengali Unicode variants, model outage/timeout, no-consent/no-key, concurrency quota, mobile/keyboard/screen-reader flow and microphone unsupported cases. CI passing does not certify production user acceptance.
