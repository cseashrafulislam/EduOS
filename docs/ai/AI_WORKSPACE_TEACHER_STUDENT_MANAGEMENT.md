# EduOS AI Workspaces — feature implementation and release boundaries

**Status:** AI code is in draft feature PR #140, not merged into `Ashraful` or confirmed deployed. Source compilation and CI milestones are separate from staging/prod authorization.

## Implemented code
- **Unified Ask EduOS Q&A** `/AiWorkspace/Institution` — one UI for local-first verified MIS and consent-gated general AI fallback. No API key required for reviewed local questions. See [Unified Q&A](UNIFIED_ASK_EDUOS_QA.md).
- **Teacher Copilot** `/AiWorkspace/Teacher` — original draft lesson plans, worksheets and practice questions; LMS/teacher permission, external consent, tenant provider key and capability gate required.
- **Student Tutor** `/AiWorkspace/Tutor` — active student with current enrollment; opt-in enable and consent required. Not a replacement for cited authoritative textbook evidence.
- **Management Analytics** `/AiWorkspace/Management` — authorized campus, explicit date range, current enrollment/finalized attendance metrics. Canonical local read-only answers work without a provider key; optional AI interpretation of aggregate figures requires a key/consent.
- **Voice** — reviewed `AiVoicePageController` safe preview contract for opted-in low-risk fields, and browser-vendor speech-to-text in the one Ask EduOS Q&A page. Neither automatically posts, approves, grades or stores ERP business records.
- **Tenant AI settings** — shared onboarding + Communication Administration component, encrypted private provider key, Student Tutor opt-in, reviewed agent capability switches and tenant SQL usage/token observations. See [Credentials and Agent Controls](TENANT_OWNED_PROVIDER_CREDENTIALS.md).

## Non-negotiable boundaries
- The current tenant is resolved from authenticated server context. Every local query retains authoritative module entitlement, tenant isolation, source-defined status and Current/Period meaning; never infer missing figures or other tenants from prompts.
- No SQL generation or arbitrary cross-tenant lookup. Local question patterns are reviewed and finite; **no claim that millions of independent queries exist**.
- External OpenAI calls require institution-owned key, deployment kill switch `EduOsAi__Enabled=true`, enabled capability, explicit unchecked per-request consent and a per-user rate limit plus atomic tenant UTC-day SQL quota. They use fixed OpenAI HTTPS Responses API with `store=false`, no transcript persistence and original prompts constrained by policy. Provider contractual retention may still apply.
- Recorded usage tokens reflect provider-reported *observations*, not complete billed usage, money, or provider-side spend cap. Actual provider billing is the institute's own responsibility.
- Student/teacher/generated text is a draft requiring human review. No automatic posting, approval, payment, score or inventory write.
- Preserve original SMS/Email gateway ownership, full central MIS `GetAsync`, and external interfaces. Single-metric `GetMetricAsync` uses the canonical MIS implementation with fewer unrelated SQL queries.

## QA and release
Exact-head GitHub CI, AI product-language gate, disposable AI SQL migration QA, staging two-institute key/security UAT, real-browser role/voice/mobile QA, privacy approval, Data Protection key backup/restore, provider billing reconciliation, rollback drills and operator approval are release prerequisites. See [AI Integration Release Gate](../qa/AI_INTEGRATION_RELEASE_GATE_20261121.md).

**No production merge or deployment has been performed by this feature PR.**
