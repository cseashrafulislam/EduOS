# UX-09 / UX-12 exact-HEAD CI triage — 2026-11-12 07:48 Asia/Dhaka

## Baseline (independently inspected)
- Ashraful HEAD before QA changes: `91cba492ccdadf5cc22df62a8981ec9f2eee7d81`
- CI: https://github.com/cseashrafulislam/EduOS/actions/runs/43136273684
- GitHub Actions job `130912266599`: 1,704 tests; 1,701 PASS; 3 FAIL. These are reported runner results, not local execution.
- SQL Migration Smoke on this SHA was still in progress at inspection. Do not mark SQL PASS until it completes.

## Reproduced by source inspection and exact runner logs
1. **UX-01 Feature Discovery, feature owner**: `EduOS.App/wwwroot/js/platform/feature-discovery.js:25` contains an overescaped regex literal `/\\/Index\\/?$/i` that terminates the regex early; Node reports `SyntaxError: Invalid regular expression flags`. This prevents the Feature Discovery test module from loading. Owner must repair source and re-run the browser navigation flow.
2. **UX-01 UI Shell, feature owner**: `EduOS.App/wwwroot/js/platform/ui-shell.js` contains the analogous overescaped canonical route literal `/^\\/(?!\\/).../` and the test suite has further overescaped regexes in new source-contract assertions. CI reports `SyntaxError: Invalid regular expression flags`. Owner must repair source AND its tests, then verify authorized breadcrumb/shortcut behavior in a browser.
3. **UX-02 Finance Cash Collection, QA fixture gap**: `high-use-batch2-workflows.test.cjs:233` expected a payment preflight without setting the required `feePaymentDate`. `fees.js:388-395` explicitly rejects missing dates before any request, so zero calls was correct. This QA-only change supplies a valid historic date for the positive single-flight case, and adds a separate negative test that missing dates cause zero preflight/payment writes.

## Scope and verification
The only application behavior exercised by the QA change is existing cash collection: selecting an outstanding invoice, entering an amount/date, submitting twice, and expecting one read-only preflight followed by one payment post. No payment, ledger, tenant, role or campus production code changed.

**Not independently run:** updated JS suite, authenticated browser, .NET, SQL transactions, p75/p95 browser/SQL metrics, bn/en/mobile and multi-tenant scenarios. Next gate is exact new-HEAD CI and owner repairs for the two syntax failures. Avoid asserting overall UX-09/UX-12 acceptance on this QA patch.

## Next QA batch
Use authenticated tenant A/B, campus A/B and role matrices for F01–F12, especially sibling link retry, student import concurrency, attendance explicit confirmation, fee allocation idempotency, historical rollover, draft/consent isolation, Student 360 cross-tenant leakage and untrusted AI voice commands. Measure before/after page interaction and SQL query counts under equivalent data. Domain defects belong to UX2/UX3/UX4 owners.
