# UX-09/UX-12 — opt-in read-only performance evidence (2026-11-12)

## Baseline and scope
- Audited Ashraful HEAD before work: `a3e548db3413f06045da04f1b979562be5561728`.
- CI on that SHA: https://github.com/cseashrafulislam/EduOS/actions/runs/43147888065 — **FAIL**, 1715 PASS / 5 FAIL (runner-reported). Failing suites: Feature Discovery syntax, UI Shell syntax, Cash Collection preflight, two Sidebar Navigation assertions. Those are UX-01/UX-02 feature-owner issues; no domain implementation modified here.
- SQL migration smoke: https://github.com/cseashrafulislam/EduOS/actions/runs/43147888051 — **PASS** (GitHub reported).
- Added QA-owned `EduOS.Tests/JavaScript/ux09-readonly-performance-probe.cjs` and `ux09-readonly-performance-probe.test.cjs`. Local equivalent harness: 5/5 Node tests PASS; exact repository CI remains pending until workflow finishes.

## Safe use
This harness is **opt-in**, not run against production by CI. It requires an explicitly provided read-only bearer token in `EDUOS_QA_READONLY_TOKEN`, and a JSON config with `baseUrl`, `samples` (5–100), `timeoutMs` (100–30000), `gitSha`, `role`, `tenantAlias`, `campusAlias`, `locale`, `device`, and `routes: [{"id":"student-list","path":"/api/..."}]`. Use a controlled non-production seeded environment, approved test identities and exact existing GET paths. Never put credentials, personal data or real tenant IDs in config, output or Git. Execute:

```sh
EDUOS_QA_READONLY_TOKEN='[ephemeral-token]' node EduOS.Tests/JavaScript/ux09-readonly-performance-probe.cjs /secure/config.json /secure/baseline.json
# Repeat with the same role/tenant/campus/locale/device/sample count against the candidate SHA:
EDUOS_QA_READONLY_TOKEN='[ephemeral-token]' node EduOS.Tests/JavaScript/ux09-readonly-performance-probe.cjs /secure/candidate.json /secure/candidate-results.json /secure/baseline.json
```

Outputs use create-only mode 0600, never save tokens or response bodies. The probe uses sequential GET, no redirects, no-store, bounded timeouts and counts non-200 as failures. Comparison refuses mismatched scope or failed route. It reports **client-observed p75/p95 HTTP duration only**; not browser interaction p95, SQL IO/plan, N+1, throughput or production user experience. Do not treat successful 200s as cross-tenant authorization evidence; negative IDOR scenarios require independent authenticated identities.

## Remaining independent acceptance
- F01–F12: duplicate Guardian/sibling, student/import concurrent retry, attendance not silently Present, invoice/receipt/GL idempotency, historical rollover, protected drafts/consent, Student 360 and dashboard tenant isolation, AI voice untrusted-command rejection.
- Execute real authenticated role × tenant × campus × 360/390/768/1280 viewport × bn-BD/en-BD matrix and capture route-level PASS/FAIL/NOT RUN with exact SHA.
- For any claimed optimization, collect comparable seeded SQL actual plans, logical reads, API waterfall, baseline/post p75/p95 and errors. No speedup, fewer clicks or DB optimization is claimed in this batch.
- UX-01/UX-02 owners must reconcile their exact-HEAD failing feature tests; do not bypass or weaken the gates.
