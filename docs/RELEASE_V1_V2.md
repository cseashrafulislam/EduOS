> **SUPERSEDED — HISTORICAL CONTRACT ONLY (2026-10-28).** The owner replaced this two-version scope with the [authoritative V1/V2/V3 release contract](RELEASE_V1_V2_V3.md). Do **not** use the V1 HR/LMS or V2 Inventory assignments below when planning, coding or giving release status. This file is retained only to preserve historical decisions and links.

# EduOS — Version 1.0 and Version 2.0 Release Contract

**Decision baseline:** 2026-10-28 | **Owner-provided launch cohort:** 16 institutions, seven institution categories, approximately 40,000 initial student records. **Status: requirements / release gate, not a claim of current implementation or deployment approval.**

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

### Module-complete release rule: no Basic V1 / Advanced V2 split

1. Each business module is owned by **exactly one version for its complete agreed functional scope**. Plan, implement and test its setup, transactions, lifecycle, approvals, corrections, reports, print/export, UI, mobile, security, concurrency and historical behavior **together**.
2. **Results example:** if V1 includes Results, exam configuration, mark rules, grade/GPA/CGPA, practical/MCQ, moderation, reassessment, approval/publication, transcripts, certificates, corrections and relevant reports are V1 obligations. Nothing may be labelled 'Advanced Results — V2'.
3. **HR example:** if V1 includes HR, employee lifecycle, recruitment if offered, attendance/shifts/leave, salary configuration, payroll approval, bank/payslip output, finance posting, correction and reports all belong in V1. Nothing may be labelled 'Advanced HR/Payroll — V2'.
4. **Hostel example:** Hostel is a V2 module. Before V2 enables it, the agreed full hostel scope—buildings/rooms/beds, allocations/transfers, reservation and eligibility, billing/refund/finance control, occupancy, permissions, exceptions, audit and reports—must be finished. 'Basic Hostel now; Advanced Hostel later' is equally prohibited.
5. The completion gate applies to **all applicable institute presets**, not only one demonstration tenant. Any missing agreed module workflow is a release blocker, even if the rest of the UI or codebase builds.
6. Future security fixes, operational defects, new legal rules and backward-compatible version upgrades remain legitimate **maintenance**, not a plan to defer known functional requirements.
### Mandatory V1 business areas

| Area | Acceptance scope; no model/API-only completion |
| --- | --- |
| SaaS/platform | Tenant signup/onboarding, institute preset/configuration, subscription/feature entitlement, campus/operating unit, user invitation, account recovery and self-service administration |
| Security and identity | Auth/MFA, tenant + campus + role/resource/ownership permissions, account/membership lifecycle, audit, privacy-safe learner linking and multi-institute support |
| Institution and academic setup | Full academic programme/class/semester/cohort configuration, curriculum and effective versions, subjects/course selection, credits/prerequisites/electives as applicable, staffing, calendar, timetable conflicts, instruction progress, room/online schedule and all agreed academic reports |
| Admission/student lifecycle | Full configured online/counter application, verification, tests/merit, offer/acceptance/fee where applicable, enrolment, guardian, records/documents, subject registration, promotion/repeat, transfer/readmission/completion, credential history and reports; bulk import is a release gate |
| Attendance | Student and applicable staff/instructor attendance, absence/leave approval where relevant, ownership validation and reporting |
| **Full exam/assessment/result** | Complete exam/test and question/assessment setup, written/MCQ/practical components, mark entry, review/moderation, grading and credit/GPA/CGPA, reassessment/retake/improvement where policy applies, authorized approvals, immutable publication, controlled correction, mark sheet/tabulation/transcript/certificate/verification and **every agreed result report**. No later 'advanced results' backlog |
| **Full LMS** | Complete course authoring/enrolment, lesson/content/versioning, scheduled access, online/live-class integrations in the agreed provider scope, question banks/quizzes/attempt policies, assignments/submissions/rubrics/feedback, progress/grades, learning certificate, student/instructor portal and complete LMS reports. No later 'advanced LMS' backlog |
| Fees and collections | Full fee policy, structures/instalments, invoice/demand, discount/waiver/scholarship/penalty rules, collection, due, verified gateway where enabled, receipt, refund/correction, reconciled posting, audit and required reports; no later deferred fee-feature tranche |
| **Full accounts and finance** | Complete chart of accounts, balanced vouchers, cost/department/project allocation where configured, cash/bank, receivables/payables, budgets where offered, statements, reconciliation, authorized posting/reversal, closed period, approval/audit and all agreed financial reports. No later 'advanced accounts' tranche |
| **Full employee HR and payroll** | Complete recruitment/onboarding, employee/teacher contracts/history, position/assignment, shifts/attendance/leave, approved performance/appraisal rules if offered, effective salary/benefit/deduction/loan rules, configurable verified statutory policy, payroll calculation/period lock, maker-checker approvals, payslips, bank/export/payment reconciliation, journal posting, controlled reversal/correction, audit and all HR/payroll reports. No later 'advanced HR/payroll' tranche |
| Portals and communication | Complete approved admin/principal/teacher/instructor/accountant/HR/student/guardian self-service, notices, delivery/acknowledgement and all in-scope notification channels with auditable status and preferences. If a provider is not ready, it must not be marketed as included |
| **Complete UI/PWA/reports** | Every V1 page, form, dialog, table, help guide, PDF/Excel/print and error state polished; consistent bilingual content, PWA, saved tenant-scoped theme/sidebar/font/allowed workspace customization, accessibility, permission-aware reports and efficient data access. No later 'advanced design polish' for V1 pages |

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

V2 contains **only new, separately complete modules**: Library; Hostel; Transport; Inventory and Procurement; Assets and Facilities; visitor/campus operations; independently scoped business intelligence (not missing V1 reports); and separately approved Alumni, Career, Research, Accreditation or external-provider integrations not already promised in V1. **Do not put Advanced Result, Advanced LMS, Advanced HR/Payroll, Advanced Accounts, Advanced Academic, Advanced Admission or unfinished V1 UX in V2.** Existing implementations/data remain preserved and gated until the entire new module has passed acceptance.

**No rework promise is interpreted as stable ownership/contracts and zero planned V1 redesign**, not a ban on bug fixes, evolving statutory rules, security patches, additive migrations, reporting corrections or measured performance improvements. Version upgrades are rehearsed on restored data and preserve signed historical results, vouchers, payroll and transcript output.

### Whole-module acceptance for V2

| New V2 module | Must be complete before being enabled |
| --- | --- |
| Library | Catalogue/copies, issue/return/renewal/reservation/fine, lost/damaged control, permissions, audit, inventory link where needed, all library reports |
| Hostel | Accommodation/rooms/beds, eligibility/capacity, reservation/allocation/transfer, occupied-change guard, fees/finance adjustment, departures, incidents, audit, occupancy/financial reports |
| Transport | Vehicles/drivers/routes/stops, schedule, student assignment/capacity, reservation/transfer constraints, fees/finance adjustment, maintenance, audit, operational/financial reports |
| Inventory & Procurement | Catalogue/unit conversion, requisition/approval, supplier/PO/receive, store issue/return/adjustment, stock movement history, double-issue protection, reconciliation, all related reports |
| Assets & Facilities | Acquisition, location/custodian assignment, maintenance/lifecycle/depreciation where applicable, approvals, write-off/reversal and reports |
| Other approved V2 module | Freeze its exact complete workflow and reports before development; do not ship a partial slice as a full module |
## 7. Deployment topology and release ownership

- **One codebase / one default SQL database.** No multi-database/sharding implementation now.
- Real institute data lives in the one authorized production environment, upgraded from V1 to V2 with controlled, compatible releases.
- A separate preview/staging URL uses a separate isolated **test** database and anonymized/synthetic data; it must not create a competing live record of the same institute.
- If V1 and V2 feature tiers coexist for customers, use tenant feature entitlements on a supported production release, not two diverging financial/academic databases.
- Keep prior school/college-only staging checklist as a narrow historic pre-release test plan; **this document supersedes it for V1 product scope**, without asserting the current code is compliant.
- Scheduled coding tasks remain paused until the owner approves the exact release backlog. The documentation update itself changes no model, database or deployment.

