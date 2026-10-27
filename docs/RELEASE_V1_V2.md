# EduOS — Version 1.0 and Version 2.0 Release Contract

**Decision baseline:** 2026-10-27 | **Owner-provided launch cohort:** 16 institutions, seven institution categories, approximately 40,000 initial student records. **Status: requirements / release gate, not a claim of current implementation or deployment approval.**

## 1. Confirmed launch cohort

| Institute category | Count | Required first-release behavior |
| --- | ---: | --- |
| Primary-through-college institutions | 2 | Multiple academic levels, years, groups/sections, enrolment, class timetable, exam/result, promotion and fees |
| Madrasas | 3 | Configurable programme/level names, academic terms, curricula, grading, notices and fees without madrasa-specific entity forks |
| Training centres | 2 | Cohort/batch, module/course, trainer, attendance, competency assessment and certificate |
| Coaching centre | 1 | Course/batch, flexible test/routine, fees/instalments and result/progress |
| Online LMS institutes | 3 | Online course enrolment/access, lesson/content, quiz, assignment, progress, certificate and appropriate fee handling without forcing physical classroom workflows |
| Universities | 3 | Programme/department, semester/term, credits, curriculum version, subject registration, prerequisites where configured, grade/GPA/CGPA and transcript |
| Private teachers | 2 | Lightweight tutor/group/student, class/lesson, attendance, test, fees and progress without forcing irrelevant campus bureaucracy |
| **Total** | **16** | All applicable workflows in the agreed Version 1 scope must work for real users |

The 40,000 figure is an **initial student-record estimate across the cohort**, not a verified count of unique people or concurrent users. Confirm counts and import sources before sizing and migration. One learner may have **two or three independent institution memberships**; these must not be collapsed into one tenant-owned Student row.

## 2. Non-negotiable meaning of Version 1.0

**Version 1 is an advanced, production-usable cross-institution Education ERP through HR & Payroll**, not a narrow school demo, an incomplete MVP, or a UI catalogue. Every module listed below must have the complete applicable server rules, data persistence, authorized UI, reporting and tests before its real-user route is enabled. For modules in scope, no Under Construction button or unimplemented workflow is acceptable.

The word **100%** means **all agreed V1 acceptance cases pass and no known release-blocking defect remains**, not that there can never be future fixes, security updates or compatibility changes. Features not explicitly in the frozen V1 scope remain unavailable/gated with existing code and data preserved.

### Mandatory V1 business areas

| Area | Acceptance scope; no model/API-only completion |
| --- | --- |
| SaaS/platform | Tenant signup/onboarding, institute preset/configuration, subscription/feature entitlement, campus/operating unit, user invitation, account recovery and self-service administration |
| Security and identity | Auth/MFA, tenant + campus + role/resource/ownership permissions, account/membership lifecycle, audit, privacy-safe learner linking and multi-institute support |
| Institution and academic setup | Academic years/terms, programme/level/department, group/track, class/batch/cohort, curriculum/courses/subjects/credits, room where relevant, instructor assignments, routine and calendar |
| Admission/student lifecycle | Configurable admission/enrolment, applicant verification/selection, student/guardian documents and profiles, course/subject registration, promotion, repeat, transfers/completion and lifecycle reports |
| Attendance | Student and applicable staff/instructor attendance, absence/leave approval where relevant, ownership validation and reporting |
| Exam/assessment/result | Exam/test setup, teacher-owned marks, completeness, moderation/verification, approval, locked publication, grading/GPA/CGPA where applicable, mark sheets, tabulations, transcript/certificate output |
| **Advanced LMS** | Course creation/enrolment, lesson/content resources, scheduled access, question bank/quiz attempts and evaluation, assignments/submissions/feedback, learning progress, instructor/student views and LMS-specific reports |
| Fees and collections | Fee policy, demand/invoice, instalment, discount/waiver/scholarship, due, collection, receipt, refund/correction, payment reconciliation, audit and reports |
| **Advanced accounts** | Chart of accounts, balanced vouchers, cash/bank, ledger, student receivable control, financial statements, authorized posting/reversal, period/approval control and reconciliation; any online gateway used for real money must be verified/certified |
| **Advanced HR & Payroll** | Employee/teacher contracts and history, employment assignment, attendance/shifts/leave, pay structure and effective dates, benefits/deductions/loan rules where offered, payroll period calculation, validation, multi-level approval, pay slips, bank/export reconciliation, accounting posting, cancellation/reversal and audit; statutory deductions need current verified rules |
| Portals and communication | Appropriate admin/principal/teacher/instructor/accountant/HR/student/guardian self-service, notices and essential verified notifications |
| UI/PWA and reports | Clean professional bilingual pages, non-intrusive header guide, responsive dialog/form/list/report, safe PWA, saved permitted per-user appearance, PDF/Excel/print, scoped efficient queries and error states |

**Institute adaptation must be configuration-driven.** The system must not require a campus building/room for purely online providers or force semester/credit rules on primary classes and private tutors. Models remain generic; institutional labels and approved policy rules vary by preset.

## 3. Privacy-safe same-learner / multi-institution identity

Use the existing platform-global `Person`/protected identity foundation and tenant-owned `Student`/enrolment ownership model; inspect actual EF mapping and the existing `StudentPersonLink` before implementing changes. Do **not** invent a second global-student source of truth.

- Person identity may be associated with multiple institution-owned learner/student records (2–3 or more); each institute keeps its own admission number, programme, roll, grades, invoices, permissions and history.
- **Matching is not automatic merging.** Name, guardian name, phone or common birthday alone are not sufficient to disclose, join or modify private records. Use verified identifiers, neutral candidate handling, controlled linking/consent and audit.
- The same person attending two institutes must not cause duplicate global identity creation when safely verified, but a suspected match cannot block legitimate admission or expose the other institute's data.
- Institute A cannot see Institute B's marks, payments, guardian details, documents, health records or attendance without an explicit lawful, approved, time-scoped access path.
- Test student role and parent/guardian membership across two institutes in one session, switching institute contexts, direct resource-ID guessing, exports/files, reports and logout/invalidation.

## 4. Initial 40,000-record import and reconciliation

Manual entry of 40,000 records is not an acceptable launch dependency. An authorized **per-institute import workflow** is a V1 release-blocking capability:

1. Inventory all source formats, institutes, academic history, student IDs, guardian contacts, enrolment and opening-fee balances. Define tenant-scoped source identifiers and a documented field map.
2. Dry-run/stage CSV/XLSX files with file checks, header/column validation, required-field rules, date/number normalization, duplicate warnings and a downloadable row-level error report.
3. Import in bounded, resumable batches with unique import job/request identity and idempotent replay; never keep one huge SQL transaction open for 40,000 rows. Each committed batch is atomic, tenant-scoped and auditable.
4. Resolve duplicate records **within a tenant** by verified business key and manual review; cross-tenant Person matching remains privacy-preserving and cannot leak identities through preview/errors.
5. Validate created students, guardians, programme/year/batch/enrolments, course registrations and authorized historical facts. Preserve source-reference and import audit.
6. For financial opening balances, require authorized review, double-entry/reconciliation, verified cutover date and no silent duplicate posting. Historical results/certificates are not fabricated from current rules.
7. Reconcile per-institute source vs imported counts, missing/duplicate records, fee totals, rejected rows and sample profiles; record signed acceptance. Keep encrypted backups, import manifest and recovery/compensating-action plan.
8. Run a realistic simultaneous import/load test across several tenants; bound export/report workload and monitor SQL locks, query plans, connection pool and storage growth.

## 5. V1 acceptance and release decision

**All launch institute categories must pass representative golden journeys**, not just the school preset. At least one test tenant per category; repeat critical tenant-isolation/reconciliation checks across multiple tenants of the same category. Use staging/test data first and a controlled live migration after gates pass.

| Gate | Pass evidence |
| --- | --- |
| Scope | No visible/non-disabled V1 menu reaches a stub, placeholder, unimplemented action or broken report |
| Institution fit | All seven presets complete relevant setup -> enrolment -> teaching -> assessment/result -> applicable fees and reporting |
| Global identity | Same learner in 2–3 tenants links safely without private-history leakage, implicit merge or role/guardian confusion |
| Bulk entry | Approximately 40k student records staged/imported with retry, progress, validation, reconciliation and no tenant bleed |
| Finance | Fee ledger/reports agree; posting/refund/reversal/bank/cash balanced and replay/concurrency-safe |
| HR/payroll | Payroll total/net/payslips/bank export and journal entries reconcile; approval, locked period and correction paths work |
| Access/security | Tenant/campus/role/ownership/entitlement, session, cross-account report/file and integration tests pass |
| UX | Bangla/English, PWA, saved safe themes/navigation, small contextual guide, dialogs, mobile tasks and PDF/print all tested |
| Operational readiness | Production-like load and error/latency evidence, independent security review, migration rehearsal, backups/restore, secret custody, observability and recovery plan |
| Real users | Each participating institute signs off its critical flows; release owner records go/no-go and rollback target |

No claims of 100%-ready from code presence or green CI alone. Final real-world performance/SLO targets are decided using representative user concurrency and infrastructure measurements, not guessed from the student count.

## 6. Version 2.0 — additional scope, not reimplementation

V2 continues from the exact V1 architecture, source, migrations and production data. The original V1 business flows remain regression-protected and usable; new module contracts are integrated without introducing duplicate data stores or breaking historical documents.

Candidate V2 areas, after separate scope approval: Library, Hostel, Transport, Inventory/Procurement, Fixed Assets, Facilities, visitor/security, advanced administrative workflows, deeper reporting/analytics, career/alumni/research/accreditation, optional additional integrations and expanded visual workspace composition. Existing implementations and historical data in these modules are **preserved**, securely gated until accepted, then completed rather than rewritten.

**No rework promise is interpreted as stable ownership/contracts and zero planned V1 redesign**, not a ban on bug fixes, evolving statutory rules, security patches, additive migrations, reporting corrections or measured performance improvements. Version upgrades are rehearsed on restored data and preserve signed historical results, vouchers, payroll and transcript output.

## 7. Deployment topology and release ownership

- **One codebase / one default SQL database.** No multi-database/sharding implementation now.
- Real institute data lives in the one authorized production environment, upgraded from V1 to V2 with controlled, compatible releases.
- A separate preview/staging URL uses a separate isolated **test** database and anonymized/synthetic data; it must not create a competing live record of the same institute.
- If V1 and V2 feature tiers coexist for customers, use tenant feature entitlements on a supported production release, not two diverging financial/academic databases.
- Keep prior school/college-only staging checklist as a narrow historic pre-release test plan; **this document supersedes it for V1 product scope**, without asserting the current code is compliant.
- Scheduled coding tasks remain paused until the owner approves the exact release backlog. The documentation update itself changes no model, database or deployment.

