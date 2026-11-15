# EduOS Production Release — Approval and NO_GO checklist
Date: 2026-11-15. Canonical branch: \`Ashraful\`. Decision owner: **not yet recorded**.

## What has been demonstrated

- Five disposable GitHub workflows and the user's private Codespaces selected real UI/SQL tests passed at exact commit \`4963379b4655c5ac639b880c574c57e67b72ae23\` **before** the new release-covering changes. Preserve that historical evidence, but do NOT reuse it to certify a newer commit.
- Current candidate includes two isolated paths for the formerly unobserved 15 routes: 11 platform SuperAdmin pages and 4 optional-module tenant pages. The **commercial** plan is deliberately unaffected by demo-only Research/Accreditation module entitlements.
- New executable \`scripts/qa/release-readiness.cjs\` validates exact-SHA evidence on a candidate after real browser and SQL tests. It emits \`qa-artifacts/release-readiness.json\` with \`productionReleaseApproved:false\`; this is deliberate.

## Gate owners and evidence required

| Release gate | Required evidence | Current status |
|---|---|---|
| 1. Authenticated staging | HTTPS, exact SHA, isolated SQL, roles and access-denial evidence | Recheck latest candidate |
| 2. Declared page navigation | All 204 canonical pages authenticated GET/render and route inventory | 189/204 older candidate; 15 new checks pending |
| 3. Business UAT | Actual Admissions → Enrollment → Attendance → Fees → GL → Report and Procurement → Stock → Assets → Finance with signed scenarios | PENDING |
| 4. Tenant/campus security | Two or more real-shaped tenants and campuses, role matrix, cross-tenant IDOR denial, MFA/logout and export scope | PENDING |
| 5. Financial/stock concurrency | Duplicate retries, competing approvers, balanced journal, immutable posted stock, exact source and reversal | PENDING full-chain |
| 6. UX and accessibility | Keyboard and contrast, bn-BD/en-BD, 360/390/768/1280, light/dark, print and export evidence | PENDING full coverage |
| 7. Performance | 40K student import, 5K/10K/50K supplier AP, SQL actual plans, p95/p99 and reconciled report totals | PENDING |
| 8. Deployment & recovery | Stage deploy of candidate, migration dry-run, backup restore with checksum, rollback and observability; demonstrate on production-like infrastructure | PENDING |
| 9. Institution and owner approval | Named authorized institution UAT approver, product owner, security/operations owner, release time window, Go/No-Go decision | PENDING |

## Release blocking controls

- Never use the disposable Codespaces database as the actual production database.
- No automatic schema migration in production startup. Create a verified database backup and record restore evidence before deployment.
- A green GET/Chromium test does not prove working Create/Edit/Delete, ledger balance, stock reversal or report export.
- No override of tenant isolation, role policies, module entitlements or antiforgery just to make browser QA green.
- Treat EF1002/EF1003 SQL analyzer warnings as **review items**, not automatic proof of injection or automatic safe-to-ignore findings: inspect each query source, ensure only allowlisted SQL fragments and placeholders/DbParameters, and fix any untrusted interpolation.
- Do not enable the public demo login, demo platform identity, real outbound SMS/email or payment callbacks on production.
- CI, all required fresh-snapshot SQL gates and 204-route browser checks must be PASS **on the same immutable HEAD SHA**.
- A release candidate remains **NO_GO** if any mandatory evidence is missing, failed, skipped or attached to an earlier SHA.

## Final manual sign-off (intentionally empty)

| Role | Name / verified identity | Acceptance artifact URL | Approved at UTC |
|---|---|---|---|
| Institution UAT lead | — | — | — |
| Engineering lead | — | — | — |
| Security and tenancy reviewer | — | — | — |
| DBA / backup-restore owner | — | — | — |
| Product / release owner | — | — | — |

**Current decision: NO_GO — production release not approved.** Do not close [issue #139](https://github.com/cseashrafulislam/EduOS/issues/139) before genuine evidence and human sign-off. 
