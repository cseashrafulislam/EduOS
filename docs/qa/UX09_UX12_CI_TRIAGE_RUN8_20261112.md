# UX-09 / UX-12 — exact-HEAD CI triage and independent QA gates (2026-11-12)

## Baseline and evidence
Read AGENTS.md, PROJECT_MASTER_PLAN.md and UI_UX_FINAL_IMPLEMENTATION_PLAN_20261109.md §4.0 at source HEAD `a8f14908fbb26b9a88156c66db234282ecb8e869`. Exact-HEAD [CI run 43128804598](https://github.com/cseashrafulislam/EduOS/actions/runs/43128804598) failed JavaScript contracts: **1681 pass, 7 fail**. [SQL Server Migration Smoke 43128804690](https://github.com/cseashrafulislam/EduOS/actions/runs/43128804690) succeeded. CI is not an authenticated multi-tenant/browser acceptance or performance benchmark.

## This QA-owned patch
- `central-work-center-page-actions.test.cjs`: existing fake DOM omitted native `Element.append` and `setAttribute` invoked by authorized source-summary buttons. Restored both DOM methods rather than weakening the product's server-count validation. Added tests that fail closed on unknown/negative/inconsistent server source counts and that prevent export/print during a second-page fetch and after its failure.
- `ux03-all-pages-coverage.test.cjs`: source manifest now reports **296** views (formerly 295). Assert the exact current count and explicitly require the shared official education source view; keep the 0 authenticated acceptance invariant and all source-only labels. No fake browser certification.

## Owner handoff for remaining failures
| Failure on baseline CI | Classification | Owner |
| --- | --- | --- |
| Central Work Center page actions (2) | Fake DOM missing native methods; QA harness fix in this patch | UX-09/12 |
| Razor source inventory (1) | Newly added shared education view increased 295 to 296; QA inventory fix | UX-09/12 |
| Feature Discovery (1) | Invalid regular-expression flags in feature source | UX-01 |
| Page Assistant field draft (1) | Invalid JavaScript token/syntax in field-draft test/source import; inspect stack before any feature change | UX-06 |
| UI shell (1) | Invalid regular-expression flags in test/source import; inspect stack before feature change | UX-01 |
| Cash collection single-flight (1) | One preflight/post assertion failed; requires UX-02 finance flow review with idempotency proof | UX-02 |

## Not independently certified
The F01–F12 source-contract matrices are *not* proof of completed workflows. Actual role × tenant × campus × route × viewport (360/390/768/1280) × locale (bn-BD/en-BD) acceptance remains **NOT RUN** without authenticated test fixtures. Duplicate guardian/sibling, parallel student/import retries, attendance not silently Present, fee receipt/GL idempotency, historical rollover, protected draft/consent, Student 360 redaction, and untrusted AI voice require executed transaction/API/browser tests. Query plans, p75/p95 before/after, bundle size and cold/warm page loading are **NOT MEASURED**. Do not claim improvements or green CI until new exact-SHA runs finish. No production data or domain logic changed.
