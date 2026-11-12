# UX-09 / UX-12 exact-HEAD CI and staff-efficiency QA — 2026-11-12 08:55 Asia/Dhaka

## Baseline
- Branch: `Ashraful`; reviewed HEAD: `c46693e9e7552122ce3b9ba4fa03a5f957945cad`
- GitHub CI: https://github.com/cseashrafulislam/EduOS/actions/runs/43141829505 — completed failure, 1,699 PASS / 10 FAIL / 1,709 tests (runner-reported, not local execution).
- SQL migration smoke: https://github.com/cseashrafulislam/EduOS/actions/runs/43141829568 — completed success (runner-reported).
- AI language gate: https://github.com/cseashrafulislam/EduOS/actions/runs/43141829630 — completed success (runner-reported).

## Independently diagnosed QA-owner defects
The seven failures in `central-work-center-page-actions.test.cjs` are a QA fixture mismatch after Work Center acquired session-aware authorization:
- Six tests fail with `Cannot read properties of undefined (reading 'authTenantId')` because the fake `document.body` lacked the session dataset; the production script expects the dataset to be supplied by the authenticated shell.
- The static contract assertion for export failed because the real guard now also checks `!sameSession()`; removing this check from production would be a regression.
- The final source-contract test also fails because `read` was never defined in that test file.
- QA fix: supply explicit tenant/session dataset, window session-transition callback, `pagehide` event capture, a file reader and the stronger source guard assertion. Two new behavioral tests exercise session and tenant changes after an authorized page has loaded. No business code or permissions are modified.

## Outside this owner's feature scope
The same CI run has two UX-01 syntax failures in `feature-discovery.test.cjs` and `ui-shell.test.cjs` (invalid regex flags), and one UX-02 Cash Collection preflight expectation failure in `high-use-batch2-workflows.test.cjs` (0 actual versus 1 expected). UX-01/UX-02 owners should reconcile their current source/fixtures; this QA owner must not silently alter those feature implementations. All failures require a fresh exact-HEAD rerun before acceptance.

## F01–F12 acceptance remains open
This batch improves **Work Center session/tenant export regression evidence only**. It is not authenticated cross-tenant evidence. Still required: route/role/campus/locale/device matrix against real app, sibling/guardian duplicates, import retry/concurrency, attendance not-default-Present, receipt/ledger idempotency, historical rollover, protected drafts and consent, Student 360 scope, AI Voice untrusted command rejection, and baseline versus post p75/p95 and SQL query plans. No fewer-clicks/fields or performance gains were measured; no performance optimization is claimed.

## Execution status
- Source and CI log inspection: PASS.
- QA fixture/contract correction: authored.
- Local JS execution: NOT RUN (container cannot resolve github.com for checkout).
- New commit CI: PENDING until GitHub Actions reports the result.
- Authenticated Chrome, .NET/SQL runtime and p75/p95: NOT RUN.
