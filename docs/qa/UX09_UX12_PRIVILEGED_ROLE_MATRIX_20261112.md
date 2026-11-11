# UX-09 / UX-12 — privileged staff-efficiency route matrix (2026-11-12)

## Exact-source evidence
Reviewed branch HEAD `9a815915fc58642e6e3c40dfa807a93315402a1d` and the current AGENTS, master plan, UX plan §4.0. Existing `ux09-ux12-staff-efficiency-contract.test.cjs` covers coarse controller gates. Added `ux09-ux12-privileged-role-matrix.test.cjs` to assert **positive and negative** controller role matrices and the stricter method-level Attendance finalize/correct and Finance post gates. It is intentionally source-contract QA, not proof of an authenticated request. Role lists below are controller-level; method-level overrides and scoped permission services can further restrict.

| Refinement | Controller | Allowed controller roles | Negative roles covered | Runtime |
| --- | --- | --- | --- | --- |
| F02 sibling/guardian reuse | StudentGuardians | TenantAdmin | Teacher, Student, Guardian, Accountant | NOT RUN |
| F04 import preview/execute | StudentImports | TenantAdmin | Teacher, Student, Guardian, Accountant | NOT RUN |
| F05 attendance roster | StudentAttendance | TenantAdmin, Principal, VicePrincipal, Teacher | Student, Guardian, Accountant | NOT RUN |
| F05 attendance finalize/correct | StudentAttendance method override | TenantAdmin, Principal, VicePrincipal | Teacher, Student, Guardian, Accountant | NOT RUN |
| F06 invoice register | StudentInvoicePosting | TenantAdmin, Principal, Accountant | Teacher, Student, Guardian | NOT RUN |
| F06 invoice posting | StudentInvoicePosting method override | TenantAdmin, Principal | Accountant, Teacher, Student, Guardian | NOT RUN |
| F12 voice attendance preview | AiVoiceAttendance | TenantAdmin, Principal, VicePrincipal plus Attendance.View | Teacher, Student, Guardian, Accountant | NOT RUN |

Each role must be exercised with tenant A/campus A, tenant A/campus B, tenant B/campus A and a revoked/suspended membership. Device/locale dimensions: Chrome desktop 1366×768, mobile 390×844 and 320×640, each en-BD and bn-BD. Never infer a 200/403/404 outcome from attribute text alone. For each denied case assert no PII in body, cache headers and logs; for permitted cases assert exact campus-scoped records. All state-changing scenarios must use isolated seeded test tenants, not production.

## Current CI and ownership
Exact HEAD [CI 43113709802](https://github.com/cseashrafulislam/EduOS/actions/runs/43113709802) reported **1674 pass / 3 fail** (JavaScript contract stage). Failing files: `feature-discovery.test.cjs` (UX-01 feature JS regex), `page-assistant-field-draft.test.cjs` (UX-06 field draft behavior), `ui-shell.test.cjs` (UX-01 shell assertion). These are not UX-09/12-owned business sources; preserve failures rather than bypassing tests. [SQL smoke 43113709637](https://github.com/cseashrafulislam/EduOS/actions/runs/43113709637) was still running when checked.

## Performance and dynamic acceptance still blocked
No authenticated browser, EF SQL trace, execution plans or latency sampling was available in this run. Baseline and post p75/p95, SQL logical reads, query counts, LCP, click/field reductions: **NOT RUN; no invented values**. Pending dynamic scenarios: duplicate sibling links and revoked consent; concurrent student-import retries; unmarked attendance never silently Present; same-invoice posting replay; rollover historical preservation; Student 360 cross-tenant denial; AI voice untrusted transcript and explicit confirmation. These must be validated independently against the same deployed SHA with seeded test data.

## Why this test does not replace runtime verification
Static attribute tests detect accidental widening/removal of privileged route gates and broken route contracts; they do not test middleware order, permission-service behavior, database scoping, data races or browser usability. Changes to approved role policy require coordinated review, not automatic relaxation of assertions. The canonical service/API and UI implementations remain unchanged.
