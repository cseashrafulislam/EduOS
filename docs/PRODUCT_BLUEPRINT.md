# EduOS — Product Blueprint and Delivery Contract

**Decision baseline:** 2026-10-27; **unified owner delivery decision:** 2026-10-31 | **Working branch:** `Ashraful` | **Status:** approved product direction, not a claim of implemented features. The [Unified Project Master Plan](PROJECT_MASTER_PLAN.md) is the only active backlog; former release partitions are history.

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

### Central capabilities do not replace business module ownership

To strengthen EduOS as one ERP, reuse the **existing central authorities** for tenant/campus access, verified Person identity, Academic setup, canonical Finance GL, stock movement, private files, communications/audit and subscription policy. Admission, student, examination, Payroll, Procurement, Assets, Library, Hostel, Transport, Facilities, Career and Research retain their domain commands and historical business facts. A Central Work Center, Institutional MIS, record search and unified calendar—where justified—are **permission-scoped read compositions**, not new editable systems of record. The existing Global Feature Search already serves authorized route discovery and must not be rebuilt. Details, current code-level adoption gaps, dependencies and acceptance cases: [Central Platform Integration](architecture/CENTRAL_PLATFORM_INTEGRATION.md). Priorities remain governed only by the [Unified Project Master Plan](PROJECT_MASTER_PLAN.md).

## 3. Audience and access model

- **Platform SuperAdmin:** plans, tenants, subscriptions, cross-tenant operational metadata and audited support; never implicit unrestricted private-student access.
- **Tenant Owner/Admin, Principal, Department Head:** local setup, permissions, institutional governance, review and approvals within authorized campus/resource scope.
- **Admission Officer, Exam Controller, Teacher, Accountant, HR, Librarian, Transport/Hostel operators:** role-specific create/read/approve actions; access is verified server-side.
- **Student, Guardian, Staff:** owned records, self-service, learning, fees and communication; one person can legitimately hold several institute memberships and roles.

Visibility in a menu is **not** authorization. Tenant + campus + membership + feature entitlement + role + ownership/sensitivity must be checked for API, pages, exports, files, background work and reports.

## 4. Unified EduOS project and module ownership

**Authoritative active scope:** [Unified Project Master Plan](PROJECT_MASTER_PLAN.md) and [AGENTS.md](../AGENTS.md). Superseded two-/three-release contracts were retired from the current docs tree; their historic text is preserved in Git history for audit, **not** as active development partitions.

- **One project, all institution workflows:** SaaS/security/identity, admission/student/guardian, academic/examination/results, Fees/Finance, HR/Payroll, LMS, Library, Hostel, Transport, Inventory/Procurement, Assets/Facilities/Visitors and approved enterprise engagement/integration capabilities.
- **Preserve verified modules:** previously established Platform/Education/Finance/HR, LMS/Library/Hostel/Transport and Inventory source paths remain authoritative. Do not create duplicate models, independent payroll, a second stock ledger, a shadow GL, a new LMS/Library or repeated forms merely to change an obsolete release label.
- **Work only on verified remaining gaps:** supplier AP settlement and credits, asset capitalization/depreciation/disposal, Facilities spare-part accounting and Visitor lifecycle, Career/Research/Accreditation, whole-system integration/security/performance proof; fix proven bugs across completed domains as needed. Assign a clear **domain owner**, not a version number.
- Institute and student totals are **dynamic, not fixed to a pilot cohort**; fixed-header Excel templates and server-enforced import batches use actual uploaded records. Some people have several institution memberships with separate private records. No institution-specific model names or permanent code forks.
- **Development Final OK** means real persisted behavior plus all defined automated CI/SQL/security/applicable browser tests for the agreed scope. **Production Ready** requires distinct real-institute UAT, deployment/recovery, load and compliance evidence plus owner go/no-go. Do not use passing tests alone as institutional certification.
- Existing work/data is never deleted because its planning version changed; incomplete or unsafe actions are gated as Under Construction. Historic EF migrations, release-specific tests and evidence reports remain intact.

## 5. Status honesty and source of truth

Use these statuses **per end-to-end workflow**, not per folder or model:

| Status | Evidence |
| --- | --- |
| Implemented slice | Relevant persisted behavior, scoped API/UI, business rules and automated checks exist; identify limitations |
| Under Construction | Partial implementation; gated when unsafe, work/data preserved |
| Planned | Specified capability with no verified operational implementation |
| Production Ready | Implemented + migration/deployment review, security testing, production-like load, backup/restore, external-provider and user acceptance evidence |

`README.md` provides the broad product catalogue. Superseded early delivery/roadmap snapshots are retained in **Git history**, not as current documentation. `docs/deployment/SCHOOL_PILOT_STAGING_RELEASE.md` describes a guarded **staging pilot**, not general-release readiness. Re-read current `Ashraful` code and CI before making status claims. Never infer operational status from a checklist alone.

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
- The five owner-approved hourly development tasks are assigned to **whole-project functional domains** in [the unified plan](PROJECT_MASTER_PLAN.md), preserve their staggered schedules and follow anti-loop/QA rules. Their temporary cleanup pause and subsequent reactivation are managed by the automation scheduler, not by editing this document.

