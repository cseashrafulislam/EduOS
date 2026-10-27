# EduOS — Product Blueprint and Delivery Contract

**Decision baseline:** 2026-10-27 | **Working branch:** `Ashraful` | **Status:** approved product direction, not a claim of implemented features.

## 1. Product identity

EduOS is a configurable, multi-tenant **Education ERP + LMS + Finance + Administration** platform. One modular codebase serves schools, colleges, universities, madrasas, polytechnics, vocational/training institutes, coaching centres, private tutors and LMS-only providers. Institution presets configure terminology, rules, enabled modules and UI; they must not cause institution-specific entity/class/table renames or separate forks.

The product promise is an operational lifecycle, not a collection of screens: institute onboarding -> authorized staff and academic setup -> applicant/student lifecycle -> teaching/attendance/assessment -> result/certificate -> billing/accounting -> reporting and operational services. A feature is not complete just because a model, controller or menu exists.

## 2. Product capability map

The root `README.md` section 6 is the detailed module catalogue. These are grouped capabilities, **not separate projects, databases or guaranteed shipped features**.

| Capability group | Main bounded modules and workflows |
| --- | --- |
| SaaS platform | Tenant/onboarding, multi-campus, subscriptions, entitlements, quota, billing, platform oversight |
| Identity and governance | User, multi-institution membership, role/resource permission, MFA, consent, privacy, audit |
| Institution configuration | Academic presets, terminology, policy, numbering, branding, workflow and module settings |
| Admission and student lifecycle | Application, assessment/merit, verification, admission, enrolment, guardian, promotion, transfer, completion |
| Academic management | Programme/level/department, batch/section, curriculum, course/subject registration, term, schedule, instructor, room, calendar |
| Attendance and assessment | Student/staff attendance, exam setup, marks, moderation, grade, results, transcripts, certificates |
| Learning/LMS | Course/content, lesson, assignment, submissions, question bank, quizzes, feedback, learning progress |
| Finance | Student fees/invoice/collection/refund/waiver/scholarship, chart of accounts, vouchers, cash/bank, statements |
| Staff and HR | Teacher/employee lifecycle, assignment, leave, attendance, payroll and performance |
| Campus operations | Library, transport, hostel, inventory, procurement, assets, facilities and maintenance |
| Engagement and services | Student/parent/teacher portals, notice, notifications, messaging, documents, support and events |
| Enterprise capabilities | Reports, analytics, integrations, career/alumni/research, accreditation and administrative approvals |

Cross-cutting capabilities: privacy and tenant isolation; bilingual `bn-BD`/`en-BD`; responsive/PWA shell; secure user/tenant UI personalization; accessibility; server-side authorization; audited critical changes; reliable exports; operational observability. See [UI personalization](UI_PERSONALIZATION.md) and [architecture/engineering standard](architecture/SCALABILITY_AND_QUALITY.md).

## 3. Audience and access model

- **Platform SuperAdmin:** plans, tenants, subscriptions, cross-tenant operational metadata and audited support; never implicit unrestricted private-student access.
- **Tenant Owner/Admin, Principal, Department Head:** local setup, permissions, institutional governance, review and approvals within authorized campus/resource scope.
- **Admission Officer, Exam Controller, Teacher, Accountant, HR, Librarian, Transport/Hostel operators:** role-specific create/read/approve actions; access is verified server-side.
- **Student, Guardian, Staff:** owned records, self-service, learning, fees and communication; one person can legitimately hold several institute memberships and roles.

Visibility in a menu is **not** authorization. Tenant + campus + membership + feature entitlement + role + ownership/sensitivity must be checked for API, pages, exports, files, background work and reports.

## 4. Delivery sequence and scope safety

### Release A — institution-agnostic operational core

The initial real-user pilot may focus on school/college workflows for delivery speed, **but canonical models remain generic**. Complete the minimum usable end-to-end slices for:

1. Secure signup/onboarding, tenant/campus/user/role/entitlement management.
2. Admission, student/guardian profiles, enrolment and academic structure/routines.
3. Attendance, exam marks, result verification/approval/publication and readback.
4. Fees/invoice/collection/due, balanced accounting and management reports. Do **not** enable real financial posting before reconciliation and approval tests pass.
5. Practical LMS lessons, assignments/quizzes, submissions and learner progress.
6. Essential dashboards, notices, responsive bilingual UI, permitted per-user customization and safe PWA behavior.

Complete each slice from entity/data integrity through API, service, authorization, UI, reporting and regression tests. Do not attempt 60 half-finished feature areas to claim progress.

### Release B — optional operational modules

Finish Library, Hostel, Transport, HR/Payroll, Inventory/Procurement and Asset/Facility workflows based on real institution demand and dependencies. **Preserve existing models, code, tables, migrations, historical records and working workflows.** An incomplete module stays marked *Under Construction* or securely feature-gated; it is not deleted or advertised as ready. Modules already operational in the repository must continue to pass regression tests even when hidden from a pilot cohort.

### Release C — measured advanced capabilities

Enterprise reporting/analytics, external integrations, alumni/career/research, advanced workflow configuration and additional scale infrastructure only when requirements and measured load justify them.

## 5. Status honesty and source of truth

Use these statuses **per end-to-end workflow**, not per folder or model:

| Status | Evidence |
| --- | --- |
| Implemented slice | Relevant persisted behavior, scoped API/UI, business rules and automated checks exist; identify limitations |
| Under Construction | Partial implementation; gated when unsafe, work/data preserved |
| Planned | Specified capability with no verified operational implementation |
| Production Ready | Implemented + migration/deployment review, security testing, production-like load, backup/restore, external-provider and user acceptance evidence |

`README.md` provides the broad product catalogue. `docs/DELIVERY_STATUS.md` and `docs/ROADMAP_STATUS.md` contain **historical, branch/baseline-specific verification**, not proof that today's `Ashraful` HEAD or all of EduOS is production-ready. `docs/deployment/SCHOOL_PILOT_STAGING_RELEASE.md` describes a guarded **staging pilot**, not general-release readiness. Re-read the current code and CI before making status claims. Never infer operational status from a checklist alone.

## 6. Acceptance rules for every new module

- One authoritative data owner; no duplicate editable facts or speculative entities/services.
- Record tenant, campus and resource ownership, allowed lifecycle states and historical reporting semantics before the UI.
- Complete model -> mapping/migration if justified -> DTO -> query/service -> API -> view/JS -> report/export -> tests, with all consumers reconciled.
- Validate ownership, permission, status, amount/quantity and duplicate submission on the server; make critical accounting/result/stock changes atomic, audited and concurrency-safe.
- Paginate/filter/project large lists in SQL; include tenant scope and necessary indexes; avoid N+1 reads and unbounded GetAll.
- Clear, compact bilingual responsive UI with one concise page-header help entry and no redundant per-field prose; preserve accessible labels and errors.
- Explicitly test unauthorized, cross-tenant, stale edit, retry, partial failure, historical edit, mobile and report paths.
- Preserve deployed data and compatibility; reviewed additive migrations, rollback/restore notes and CI checks precede release.

## 7. Decisions currently in force

- **One existing SQL Server database and `EduOSDbContext` by default.** Do not implement sharding, a database registry or tenant moves now. A future multi-database migration is possible but will need a separate approved design, code/schema work and migration rehearsal.
- **No database/schema or feature-code rewrite authorized by this documentation update.** Existing source names remain domain-based rather than being renamed for the first pilot market.
- Keep a simple modular monolith. Do not introduce speculative microservices, distributed queues/caches or extra layers because of a hypothetical user count.
- UX customization is a constrained, secure and fast product capability; it cannot bypass permissions, validations or tenant branding boundaries.
- Automation tasks remain paused until the owner approves a new, prioritized execution plan. This document is a specification, not a background-work authorization.

