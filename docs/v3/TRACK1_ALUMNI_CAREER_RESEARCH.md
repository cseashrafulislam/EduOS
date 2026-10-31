# EduOS V3 Track 1 — Alumni, Career, Research & Accreditation

Owner path: `Ashraful` only. The V1/V2 development baselines and V3 Inventory & Store baseline are frozen. Do not recreate their models, finance entries or stock records.

## Verified preflight (2026-10-31)

Starting owner baseline: `ea8c2a9a2953f7757f4e8f8d31003fd5d6822c6b`; first-run inspected HEAD: `5b9401a16346243fb63e1c56f5712871047e721b`. Targeted App/Core/Persistence/Service trees had no named Alumni, Career, Research or Accreditation implementation. V1 `StudentExitRecord` with `ExitType.Completed`, `FeesCleared`, `StudentEnrollment.State.Completed`, and `Student.UserId` is the canonical proof of completion and self-service ownership.

## Batch A — verified alumni consent and restricted directory

- [x] Dedicated `ALUMNI` product module and feature with explicit subscription/tenant enablement; excluded from V1 pilot. No blanket automatic opt-in or changed pricing policy.
- [x] Graduates self-register against the completed V1 exit and enrollment, without modifying V1 academic identity or creating a cross-tenant global Person directory.
- [x] Consent defaults private; revocation clears alumni contact data. Optimistic rowversion, idempotent client request, serializable transaction and privacy-safe immutable action audit.
- [x] Tenant-admin-only, campus-restricted, consent-filtered paged directory and current-page CSV; spreadsheet formula injection protected.
- [x] Responsive accessible bilingual self-service and directory view; entitlement-gated navigation.
- [x] Focused C# contract, SQL integration and Node JS tests added.
- [x] Initial consent slice exact-SHA CI (3,116 tests), JS, focused Alumni SQL and fresh/repeat migrations verified in runs 41207222682 / 41207090083; shared SQL smoke failures were unrelated V2/Inventory tests.
- [x] Event draft creation, separate-admin publish approval, cancellation, campus-restricted event listings, verified alumni RSVP, idempotency/rowversion, consent-revocation RSVP clearing, aggregated engagement CSV and bilingual mobile UI — implementation prepared 2026-10-31; exact-SHA CI and SQL verification pending.
- [x] Consent-safe attendance check-in/undo: paged campus-restricted verified-going roster without contact details, published-event time window, idempotency, rowversion, audit, aggregate checked-in report/CSV, bilingual responsive UI and focused tests (implementation prepared 2026-10-31; exact-SHA verification pending).
- [ ] Invitations/notification delivery (requires opt-in communication policy) and real-institute consent policy/UAT.
- [x] Reject stale RSVP idempotency replay after consent revocation or superseding response (prior audit commands retained); regression added to real SQL integration and C# contract tests at `d301c4a7`.
- [x] RSVP hardening at 14f673d: SQL smoke 41212957560 PASS; CI 41212957597 CANCELLED by subsequent pushes. Latest shared a36ea8ac exact-SHA CI 41214255559 and SQL 41214255467 PASS.
- [ ] Attendance exact-SHA Release build, fresh/repeat SQL, SQL integration and browser/JS evidence — report only verified results.
- [ ] Re-evaluate campus access policy for institution-wide TenantAdmin accounts with no explicit campus grants before real-institute acceptance.

## Queue (do not duplicate other tracks)

B. Employer verification, internships/jobs, student applications and placements with outcome audit/report.
C. Research proposal/ethics, grants, projects and permissioned outputs.
D. Accreditation standards, outcome mapping, controlled evidence and export.

Next-run rule: start from this track's last pushed SHA recorded in run history; compare changed OWNED paths and relevant cross-contracts only. Finish outstanding Alumni engagement work before opening Career. All production deployment, pilot UAT, load testing, financial/legal policy approval and real-customer writes remain out of scope.
