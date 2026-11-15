# EduOS — Codespaces + GitHub Actions staging QA
Updated: 2026-11-15. Canonical branch: `Ashraful`.

## Execution boundaries

- GitHub Actions runs a **disposable** SQL Server 2022 container and Staging-mode ASP.NET Core application under `https://qa.eduos.test:7243` on its own isolated runner, with generated synthetic demo identities. It does **not** access the user's private Codespace, real institution or production server.
- The existing **Codespaces** setup uses the same isolated internal origin, private SQL Server container on `127.0.0.1:14333`, database `EduOS_Demo_Codespaces`, and generated secrets under `~/.config/eduos/`. External app preview is private and not accepted by the browser QA scripts as a test target.
- The connected GitHub API can commit code and inspect Actions evidence, but **cannot run commands in an existing private Codespaces terminal**. Codespaces execution requires an interactive Codespace user to start the VS Code task. Do not mark Codespaces tests PASS just because Actions passed.

## One-click Codespaces QA

1. Open the Codespace on the `Ashraful` branch in the VS Code browser editor.
2. Select **Terminal → Run Task → EduOS: Full Codespaces Functional + SQL QA**. The task safely fetches `origin/Ashraful`, fast-forwards without rewriting local commits, and invokes `.devcontainer/qa.sh test`. If local edits conflict or the branch diverged, it fails without force pushing/resetting.
3. The task starts the **private** SQL Server + HTTPS ASP.NET application if needed, migrates the isolated demo database and runs the below ordered real browser and SQL verification gates. It produces output in the VS Code Task terminal. No passwords are printed.
4. Results are local and ignored by Git: `qa-artifacts/*.json`, `qa-artifacts/test-results/*.trx`. Check the commit SHA and all gate results, then stop via **Terminal → Run Task → EduOS: Stop Codespaces QA App (preserve SQL)** if desired.

Other available VS Code tasks: start private QA app and show current private QA status. App URL (inside Codespace) is `https://qa.eduos.test:7243`; forwarded port 7243 remains private and is not a permanent UAT host.

## Shared automated acceptance (Actions + Codespaces)

The Codespaces `test` scope executes in dependency order:

1. Real TenantAdmin Year + Term form POST/reload and denied Student API.
2. Programme, Class, Subject and independent Section actual forms/reload.
3. Curriculum, subject mapping, Batch, Offering and Student + Guardian real form, identical-request replay and same-request-ID changed-payload conflict.
4. 13 synthetic institution roles' authorized sidebar GET routes.
5. TenantAdmin sidebar Chromium desktop/mobile rendering.
6. Other 12 roles' unique sidebar Chromium desktop/mobile rendering.
7. Independent SQL assertions for persisted Year/Term, Academic Foundation and Student/Guardian/Enrollment/Offering.

Codespaces SQL evidence explicitly allows **only** `EduOS_Demo_Codespaces` or runner-only `EduOS_Demo_AutoQa` over `127.0.0.1:14333`; it rejects arbitrary SQL hosts/databases before connecting. Browser evidence must match the checked-out 40-character commit SHA. A missing or failed gate is not a PASS.

- [Actions disposable real browser + SQL workflow](../../.github/workflows/real-data-entry-qa.yml)
- [Codespaces launcher](../../.devcontainer/qa.sh)
- [Source parity regression](../../EduOS.Tests/JavaScript/codespaces-qa-parity.test.cjs)

## What these checks **do not** certify

These are **selected real functional acceptance flows and read-only menu render checks**, not a complete CRUD/approval/report test of every one of the 204 declared canonical routes. Nor do they substitute for real multi-tenant/campus adversarial UAT, complete Finance/Stock/Payroll reconciliation, load/capacity testing, deployment rollback/restore rehearsal on production-like infrastructure, or independent institution user sign-off.

To finish release acceptance, record route-by-route Create/Edit/Approve/Export and denied paths, negative cross-tenant/campus tests, financial/stock invariants, mobile/accessibility/language evidence, p95/p99 performance, backup/restore/rollback, and named user/owner sign-off in [final QA issue #139](https://github.com/cseashrafulislam/EduOS/issues/139). **Release remains blocked until all nine release gates pass.**

Do not enable public Codespaces port visibility, real payment callbacks, real SMS/email or production database access for these synthetic tests. Do not auto-run heavy, data-writing UAT on Codespaces start.
