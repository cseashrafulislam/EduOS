# EduOS C3 — one shared AI/Voice backend for all pages

This is the central **page command preview** contract (version 1). It builds on the existing
`EduOS.AI` capability registry, existing local-first Page Assistant and separate,
permissioned `/api/ai/voice/attendance/preview`. It does **not** create a second AI engine,
a per-page backend, new database tables, or a domain-write service.

## Server endpoints

- `GET /api/ai/voice/page/contract`: authenticated current institution member only;
  reports backend contract, supported non-mutating actions and speech availability policy.
- `POST /api/ai/voice/page/preview`: same-origin authenticated, anti-CSRF, bounded request
  body, API rate limited, no-store. Validates current user and active tenant membership
  against the trusted `EduOSDbContext` tenant. Receives a browser transcript/text and
  **untrusted** page-control metadata. Compiles exact bilingual commands into a proposed
  field focus, plain-text draft, search filter or dropdown selection. It never runs
  database commands, navigates, mutates fields, posts accounting/attendance/marks/fees,
  executes arbitrary SQL, persists transcripts or sends audio to any provider.

Example (all labels/options come from current visible, opted-in UI controls):

```json
{
  "utterance": "select Class as One",
  "pagePath": "/Admissions/NewApplication",
  "targets": [
    {
      "key": "classChoice",
      "kind": "select",
      "labels": ["Class", "শ্রেণি"],
      "options": [
        { "key": "class-one", "labels": ["One", "এক"] }
      ]
    }
  ]
}
```

A successful preview returns `decision: "Ready"`, `action: "SelectOption"`,
`targetKey: "classChoice"`, `optionKey: "class-one"`,
`requiresConfirmation: true`, `businessWriteExecuted: false`.
Unrecognized or ambiguous controls return `Unavailable` or `ClarificationRequired`
without an action. Only exact reviewed labels/options match; no fuzzy lookup.

## Shared frontend adapter contract (later per-page integration)

1. Use **one** shared Global/Page Assistant, not microphones or backends per page.
   Collect only currently visible, enabled, eligible fields and authorized options;
   provide stable keys and bilingual accessible labels. Omit secrets, finance,
   grades, identity/PII, linked widgets and unsafe controls.
2. Use browser local speech only when the on-device pack is available and user
   has consented, otherwise accept typed text. No server audio upload or remote
   speech provider is configured. Server sees only user-submitted bounded text.
3. Send the preview with antiforgery token. Maintain a request-generation guard,
   abort old requests on page/tenant/session/control change, and compare the
   returned page/target/options against the **current live DOM** before showing
   preview. Never apply a stale, disabled or invisible target. For dependent
   dropdowns re-fetch options through the original authorized page service,
   validate the current parent selection, and re-preview rather than silently
   choose a stale option.
4. Require the user's explicit confirmation; use the existing audited page
   adapter for optional low-risk local draft changes/undo only. Existing domain
   Save/Submit/Approve/Posting endpoints remain the sole authoritative writers,
   with their original validation, authorization, idempotency and transaction rules.
5. This preview is **not** a permission grant. User-supplied control aliases,
   route and option keys cannot attest server authorization. A privileged read,
   entity lookup, finance or other business action MUST call the existing
   module/permission/record-authorized API and validate tenant/campus again.

## Acceptance status

The pure compiler has deterministic xUnit cases for bn/en commands, ambiguous
labels/options, unsupported fields, protected labels, malformed metadata,
and non-mutation. This delivery is a **backend contract**, not all-page UI
integration or final production acceptance. Required before sign-off:
net10 build/xUnit, current-SHA CI, authenticated multi-role/tenant/API/CSRF
integration, real browser/local STT/mobile tests, and per-route rollout matrix.
The existing attendance preview remains read-only and does not claim a count.
