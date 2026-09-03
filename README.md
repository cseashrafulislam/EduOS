# EduOS — Bangladesh Education Operating System

[![CI](https://github.com/cseashrafulislam/EduOS/actions/workflows/ci.yml/badge.svg)](https://github.com/cseashrafulislam/EduOS/actions/workflows/ci.yml)

EduOS is a configurable, multi-tenant SaaS platform for the Bangladesh education ecosystem. The target is one operating system that can support a primary school, high school, college, university, madrasa, polytechnic, coaching centre, training institute, private tutor, or LMS provider without creating a separate codebase for each customer.

একটি প্রতিষ্ঠান signup করবে, plan/trial বেছে নেবে, payment করবে, নিজের campus, academic structure, branding, terminology, workflow ও enabled modules configure করবে এবং ব্যবহার শুরু করবে। কোনো নির্দিষ্ট প্রতিষ্ঠানের নাম, class structure, fee rule, grading rule বা approval flow shared code-এ hard-code করা যাবে না।

> **Current status:** foundation under active development. Phase 0 security work and the Phase 1 institution/module entitlement catalogue are implemented and tested. The shared/public shells, account pages, public pricing, tenant dashboard, SuperAdmin operations landing page, and onboarding progress support responsive desktop/mobile use, installable PWA behaviour, and English/Bangla UI resources. Institution profile, campus/branch, academic year/term, plan selection, and subscription payment now have bilingual responsive workflows with TenantAdmin authorization and anti-forgery protection. Billing rejects hidden plans and duplicate current subscriptions/payments, includes setup fees in invoice totals, verifies online payment before activation, and keeps manual deposit receipts outside public web storage. Many education modules currently have domain entities only; their complete service, API, UI, permission, report, and test workflows are still planned.

---

## 1. Main objective

EduOS-এর মূল কাজ হলো:

1. বাংলাদেশের সব ধরনের শিক্ষা প্রতিষ্ঠানকে একই configurable SaaS platform দেওয়া।
2. Student, guardian, teacher, staff এবং institution management-কে একটি connected ecosystem-এ আনা।
3. একটি learner-এর জন্য একবার platform identity তৈরি করা, কিন্তু প্রতিটি প্রতিষ্ঠানের enrolment ও private records আলাদা রাখা।
4. Institution signup থেকে daily operation, accounts, learning, result, certificate এবং transfer পর্যন্ত end-to-end workflow দেওয়া।
5. কোটি user-এর target মাথায় রেখে privacy, auditability, tenant isolation এবং horizontal scale-এর foundation তৈরি করা।
6. User যেন নিজেরাই setup করতে পারে; সাধারণ configuration-এর জন্য developer বা extra training প্রয়োজন না হয়।

### Product promise

| Goal | Expected behaviour |
|---|---|
| Self-service SaaS | Signup → verify → select plan → pay/trial → configure → invite → operate |
| Configurable institution | Preset দিয়ে শুরু হবে, পরে terminology, modules, fields, policies ও workflows বদলানো যাবে |
| One learner identity | একই person-এর duplicate identity কমবে; institution records আলাদা থাকবে |
| Privacy by default | Identifier জানলেই অন্য প্রতিষ্ঠানের private history দেখা যাবে না |
| Complete operations | Academic, student, finance, HR, LMS এবং support operations একই platform-এ থাকবে |
| Scalable delivery | Stateless application, indexed relational data, cache, queue, object storage ও observability ব্যবহার হবে |

### Non-goals

- একই release-এ সব module একসাথে অর্ধেক করে বানানো হবে না।
- Institution-specific requirement-এর জন্য core code fork করা হবে না।
- NID বা birth registration number database primary key হবে না।
- SuperAdmin হলেও কারণ ছাড়া silent access দেওয়া হবে না।
- Performance benchmark ছাড়া “কোটি user ready” দাবি করা হবে না।

---

## 2. Delivery status legend

এই document-এ status বোঝাতে:

| Status | Meaning |
|---|---|
| ✅ Implemented | Repository-তে usable controller/service/data path আছে এবং CI build/test চলে |
| 🟡 Foundation | Entity বা partial service আছে, কিন্তু complete end-to-end workflow নেই |
| 🧭 Planned | Target architecture/documentation আছে; implementation এখনো শুরু হয়নি |
| ⚠️ Refactor required | Existing structure আছে, কিন্তু production-এর আগে redesign/consolidation দরকার |

---

## 3. Supported institution presets

Institution type code-এ branching logic হিসেবে hard-code করা হবে না। Preset database configuration হিসেবে academic cycle, terminology, default modules, roles, reports এবং workflows তৈরি করবে। TenantAdmin পরে permitted settings পরিবর্তন করতে পারবে।

| Institution preset | Typical structure | Default capabilities |
|---|---|---|
| Pre-primary | Play, Nursery, KG; annual cycle | Admission, attendance, guardian communication, fees, progress report |
| Primary school | Class 1–5; annual cycle | Class/section, attendance, exams, promotion, fees, guardian portal |
| Secondary school | Class 6–10; annual cycle; groups | Subject assignment, routines, exams, board preparation, SSC history |
| School & college | Multiple programmes/campuses | Shared users plus separated school/college academic structures |
| College | XI–XII or degree programmes | Group/subject choice, registration, attendance, exams, HSC/degree records |
| University | Department → programme → semester → credit | Course registration, credits, GPA/CGPA, advising, transcript |
| Madrasa | Ebtedayee/Dakhil/Alim/Fazil/Kamil | Configurable levels, subjects, exams, certificates |
| Polytechnic/vocational | Semester/module/practical | Credit/practical assessment, workshop, industrial attachment |
| Coaching centre | Course → batch → class/test | Lead/admission, batch scheduling, test result, instalment fees |
| Training institute | Course → cohort → module | Trainer assignment, attendance, assessment, competency certificate |
| Private tutor | Learner/group/session | Simple attendance, homework, fees, result/progress |
| LMS-only provider | Course → lesson → quiz/assignment | Enrolment, content, progress, assessment, certificate |
| Hybrid provider | On-campus + online delivery | Campus operations combined with LMS and live classes |

### Preset configuration must define

- Academic cycle: annual, semester, trimester, quarterly, modular, or batch-based.
- Terminology: Class/Level/Semester/Module; Section/Batch/Cohort; Teacher/Trainer/Instructor.
- Default roles and permission templates.
- Default module catalogue.
- Grading, attendance, promotion, fee, admission, and certificate policies.
- Recommended forms, custom fields, numbering formats, dashboards, and reports.
- Optional Bangladesh board, accreditation, or regulatory mappings.

---

## 4. Users, roles, and portals

One physical person may hold multiple roles. Permission comes from tenant, campus, role, resource, action, ownership, and data-sensitivity context—not only from a visible menu.

| Actor | Main responsibilities |
|---|---|
| Platform SuperAdmin | Plans, platform features, tenant lifecycle, verified payment review, platform health, compliance operations |
| Platform SupportAdmin 🧭 | Limited support tools; metadata by default; audited break-glass access only |
| Tenant Owner/TenantAdmin | Institution setup, campuses, users, roles, branding, gateways, subscriptions, policies |
| Principal/Vice Principal | Academic governance, approvals, institution dashboards, results publication |
| Department Head/Coordinator 🧭 | Programme, instructors, routine, curriculum, departmental approvals |
| Admission Officer | Applications, verification, admission tests, merit list, enrolment |
| Teacher/Instructor | Routine, attendance, lesson plan, marks, assignments, communication |
| Exam Controller | Assessment setup, schedules, admit cards, marks lock, result publish, transcripts |
| Accountant | Fee setup, invoices, collection, refunds, expenses, vouchers, reconciliation |
| HR/Payroll Officer | Employees, shifts, attendance, leave, salary, bonus, loan, payroll |
| Librarian | Catalogue, copies, issue/return, fine, reservation |
| Transport Manager 🧭 | Vehicle, route, stop, assignment, fee, maintenance |
| Hostel Warden 🧭 | Hostel, room/seat, allocation, fee, visitor/incident records |
| Student | Profile, routine, attendance, learning, fees, results, documents, consent |
| Parent/Guardian | Linked learners, attendance, fees, notices, progress, consent |
| Staff | Role-specific institution operations |

Existing role constants include SuperAdmin, TenantAdmin, Principal, VicePrincipal, Teacher, Student, Parent, Accountant, HR, Librarian, Staff, AdmissionOfficer, and ExamController. Additional operational roles will be configuration-driven.

---

## 5. End-to-end platform journey

~~~mermaid
flowchart TD
    Signup["Institution signup"] --> Verify["Email verification"]
    Verify --> Profile["Profile and institution preset"]
    Profile --> Plan["Plan or trial selection"]
    Plan --> Pay["Payment or trial activation"]
    Pay --> Setup["Campus and academic setup"]
    Setup --> Configure["Branding, terminology, policies, gateways"]
    Configure --> Invite["Invite admins, teachers and staff"]
    Invite --> Operate["Admission, learning and operations"]
    Operate --> Renew["Usage, billing and renewal"]
~~~

### Implemented onboarding steps

The current OnboardingStep lifecycle is:

1. **EmailVerification** — signup completed; owner verifies email.
2. **InstitutionProfile** — institution identity, type, address, and owner details.
3. **PlanSelection** — subscription plan or trial selection.
4. **Payment** — online payment, manual payment, or free trial.
5. **CampusSetup** — head office and additional campus setup.
6. **AcademicSetup** — academic year and optional terms.
7. **ModuleSetup** — preset-aware, plan-entitled module selection.
8. **BrandingSetup** — logo, favicon, colours, subdomain.
9. **GeneralSettings** — currency, timezone, language, date format.
10. **GatewaySetup** — optional tenant email/SMS gateway.
11. **Completed** — dashboard access unlocked.

The profile, plan/payment, campus/branch, academic year/term, and module-selection screens are implemented as mobile-first bilingual forms. Institution types come from the platform catalogue; campus codes are tenant-unique; the first campus becomes head office; deleting a head office promotes a remaining campus; active plan campus capacity is enforced; and term dates cannot escape their academic year. Plan/payment progress is advanced from verified server state: trials move directly to campus setup, paid plans wait for gateway verification or manual review, and a submitted receipt cannot unlock setup. Module selection combines institution presets with active-plan entitlements, protects required modules, uses optimistic concurrency, and cannot be used to unlock paid features. Onboarding completion accepts only the current step, validates campus, academic year, required modules, subscription, and subdomain on the server, and clears the guard cache after state changes. Onboarding pages and APIs are restricted to TenantAdmin, while same-origin browser writes carry anti-forgery tokens.

### Onboarding requirements still to build

- Resume/recovery for the remaining branding, general-settings, and gateway steps; plan/payment recovery is implemented.
- Terms/privacy-policy version acceptance.
- Domain verification and custom-domain workflow.
- Guided sample data, checklist, contextual help, and first-run tours.
- Owner MFA setup and recovery codes.
- Idempotent signup completion and provider-side payment reconciliation jobs.

---

## 6. Complete module catalogue

### 6.1 SaaS platform and subscription

| Models | Responsibility | Status |
|---|---|---|
| Tenant | Institution identity, owner, domain, branding, locale, status, onboarding, plan-limit cache | ✅ Core workflow |
| Campus | Institution branch/campus information | ✅ Onboarding CRUD |
| TenantSetting | Per-tenant typed settings; sensitive values are encrypted/masked | ✅ Core workflow |
| SubscriptionPlan | Stable plan code, English/Bangla display text, billing prices, trial, limits, public visibility | ✅ Bilingual public read workflow |
| Feature | Stable feature code, English/Bangla display text, category, visibility and platform kill switch | ✅ Entitlement catalogue |
| PlanFeature | Plan-to-feature entitlement and optional limit | 🟡 Foundation |
| TenantSubscription | Tenant plan instance, billing cycle, price snapshot, dates, limits, renewal/cancellation | ✅ Core workflow |
| SubscriptionInvoice | Subscription bill, amount, currency, due/payment state | ✅ Core workflow |
| SubscriptionPayment | Gateway/manual payment attempt and verification state | ✅ Core workflow |
| TrialAccount | Trial tracking | 🟡 Foundation |
| UsageStatistics | Tenant usage/quota tracking | 🟡 Foundation |

Required completed behaviour:

- Monthly, quarterly, half-yearly, yearly, and optional lifetime plans.
- Price snapshot so later plan-price changes do not corrupt historical invoices.
- Feature entitlement check in API, menu, job, import, and report paths.
- Student, teacher, campus, admin, storage, SMS, and email quota enforcement.
- Upgrade, downgrade, prorating, grace period, renewal, cancellation, refund, and tax/VAT rules.
- AamarPay initiation and verified callback handling plus manual bank-transfer review are implemented; production merchant certification, reconciliation/refund operations, SSLCommerz, direct bKash/Nagad merchant, and international adapters remain planned.
- Every callback must validate provider authenticity, tenant, invoice, amount, currency, duplicate event, and final state.

### 6.2 Authentication, authorization, and accounts

| Models | Responsibility | Status |
|---|---|---|
| ApplicationUser | Login identity, tenant link, profile, user type, activity and status | ✅ Auth workflow |
| ApplicationRole | System or tenant role | 🟡 Foundation |
| TenantUser | User membership and ownership inside a tenant | 🟡 Foundation |
| Permission / RolePermission | Resource-action permission catalogue and role grants | 🟡 Foundation |
| AppPage / RolePagePermission / UserPagePermission | Page/menu authorization | 🟡 Foundation |
| LoginHistory | Successful/failed login and logout audit | ✅ Auth workflow |
| RefreshToken | API token lifecycle | 🟡 Foundation |
| TwoFactorAuth | MFA setup and verification data | 🟡 Foundation |

Target capabilities:

- Cookie and JWT authentication with secure refresh-token rotation.
- MFA for privileged roles.
- Tenant and campus-scoped RBAC plus resource/ownership policies.
- Invitation, account activation, password reset, lockout, session/device view, and forced logout.
- One person account linked to multiple permitted institution memberships.
- Maker-checker approval for high-risk finance, result, transfer, and privacy operations.

### 6.3 Institution profile and configuration engine

Current models: Tenant, Campus, TenantSetting, Medium, Shift, CustomField, CustomFieldValue, Language, Currency, DocumentTemplate, Dashboard.

Target configuration models to add:

| Target model | Purpose |
|---|---|
| InstitutionTypeDefinition | Data-driven school/college/university/coaching/training presets |
| ModuleDefinition | Master catalogue of product modules |
| TenantModule | Enabled module, local settings, effective dates, and entitlement source |
| TerminologySet | Tenant labels such as Class vs Semester vs Module |
| WorkflowDefinition / WorkflowStep | Configurable approval and state transition rules |
| NumberingSequence | Admission, student, invoice, voucher, certificate, and document numbering |
| PolicyDefinition | Attendance, grading, promotion, fee, refund, and late-fine policy |
| FormDefinition / FieldDefinition | Configurable forms and validated custom fields |
| FeatureOverride | Reviewed per-tenant exception with expiry and audit |

Configuration rules:

- JSON can store validated flexible definitions, but core transactional relationships remain relational.
- A setting must have schema, type, validation, default, visibility, and change audit.
- Sensitive configuration must be encrypted and never returned as plaintext after save.
- Tenant configuration changes should support draft, preview, publish, version, and rollback.

### 6.4 Academic structure and curriculum

Current/next-generation models include AcademicYear, AcademicTerm, Medium, Shift, AcademicProgram, AcademicLevel, AcademicTrack, Subject, AcademicBatch, CurriculumSubject, StudentSubjectRegistration, Department, Group, Class, Section, SubjectTeacher, Instructor, InstructorAssignment, RoutineTimeSlot, RoutineEntry, ClassRoutine, Substitution, LessonPlan, AcademicCalendarEvent, Holiday, and Event.

| Function | Expected behaviour | Status |
|---|---|---|
| Academic calendar | Year, term/semester, holidays, events, working days | 🟡 Foundation |
| Programme structure | Department → programme → level/semester/module | 🟡 Foundation |
| Batch structure | Campus, programme, level, medium, shift, track, batch/section | 🟡 Foundation |
| Curriculum | Subjects/courses, credits, full/pass marks, optional/practical rules | 🟡 Foundation |
| Subject registration | Compulsory/elective registration with approval | 🟡 Foundation |
| Instructor assignment | Subject/batch/campus/term assignment | 🟡 Foundation |
| Routine | Time slots, rooms, instructor collision and substitution | 🟡 Foundation |
| Lesson plan | Syllabus coverage, resources, progress, approval | 🟡 Foundation |

Must support:

- Annual school classes, university credits/semesters, and coaching/training batches using one configurable model.
- Multiple campus, medium, shift, group/track, delivery mode, and programme structures.
- Capacity control and student/instructor/room schedule conflict detection.
- Curriculum versioning so old results retain their original rules.

### 6.5 Admission and applicant management

Models: AdmissionForm, AdmissionApplicant, AdmissionTest, AdmissionResult, Admission, Student, Guardian, StudentGuardianLink, Document, Payment.

Target flow:

~~~mermaid
flowchart LR
    Apply["Application"] --> Verify["Document verification"]
    Verify --> Assess["Test / interview"]
    Assess --> Decide["Merit / approval"]
    Decide --> Pay["Admission payment"]
    Pay --> Enrol["Student and enrolment"]
~~~

Required functions:

- Configurable application form, eligibility, session/programme choices, quota and application fee.
- Applicant account, document checklist, duplicate-candidate review, test/interview schedule.
- Merit/waiting list, admission offer, expiry, acceptance, payment, and enrolment conversion.
- Bulk import with preview, row-level validation, error export, idempotency, and audit.
- Online and counter admission workflows.
- No applicant/student record should be created twice because a request was retried.

Status: 🟡 entities exist; full end-to-end admission workflow is incomplete.

### 6.6 Global learner identity and institution enrolment

The final model must separate a platform person from an institution-owned student record.

~~~mermaid
erDiagram
    PERSON ||--o{ PERSON_IDENTIFIER : has
    PERSON ||--o{ INSTITUTION_MEMBERSHIP : joins
    INSTITUTION ||--o{ INSTITUTION_MEMBERSHIP : owns
    INSTITUTION_MEMBERSHIP ||--o{ ENROLLMENT : contains
    ENROLLMENT ||--o{ ACADEMIC_RECORD : produces
    PERSON ||--o{ CONSENT_GRANT : controls
~~~

#### Platform-global target models

| Model | Purpose |
|---|---|
| Person | Stable internal identity for a human; never institution-owned |
| PersonIdentifier | Encrypted birth registration/NID/passport and keyed lookup digest |
| PersonContact | Verified phone/email with ownership and verification history |
| LearnerIdentity | Learner-specific platform identity and lifecycle metadata |
| GuardianRelationship | Relationship between two platform persons |
| IdentityMatchCase | Possible duplicate review; no unsafe automatic merge |
| ConsentGrant | Who may access which data, for what purpose and period |
| DataAccessGrant | Explicit institution/user access scope and expiry |
| BreakGlassAccess | Exceptional privileged access request, approval, reason, and expiry |
| PersonAuditEvent | Append-only identity and privacy activity trail |

#### Institution-owned models

| Model | Purpose |
|---|---|
| StudentProfile / InstitutionMembership | Institution-specific student code, local profile and status |
| Enrollment | Campus, academic year/term, programme, level, batch, roll, dates, and status |
| SubjectRegistration | Selected curriculum subjects/courses |
| PromotionRecord | Movement to the next level, batch, year, or term |
| TransferRecord | Release, destination request, verification, and transfer state |
| AcademicCredential | Result, transcript, certificate, issuing institution, and verification |
| HealthRecord / BehaviorRecord | Sensitive institution-owned records with stricter policy |

#### Safe identifier matching

1. User enters an identifier over a protected channel.
2. System normalizes it in memory and computes a server-keyed lookup digest.
3. Candidate match returns only a neutral response such as “an identity may exist”.
4. Student/guardian verifies account ownership and grants purpose-specific consent, or an authorized legal workflow is used.
5. Institution receives only the approved minimum data.
6. Every lookup, match decision, consent, access, denial, and export is audited.

Birth/NID দিয়ে search করলেই অন্য school-এর student name, photo, guardian, result বা history দেখানো হবে না। Minor student-এর public profile defaultভাবে বন্ধ থাকবে।

Status: 🧭 target architecture. Existing Student.BirthCertNo and Guardian.NID fields require encrypted migration/replacement before production.

### 6.7 Student lifecycle

Models: Student, Guardian, Enrollment, StudentEnrollment, StudentSubjectRegistration, Promotion, PromotionRecord, TransferCertificate, HealthRecord, BehaviorRecord.

Required functions:

- Student profile, guardian links, documents, emergency contacts, and verified communication.
- Multiple academic-year enrolments without overwriting old history.
- Roll/registration sequencing, section/batch change, subject selection.
- Promotion, repeat, suspend, dropout, pass/completion, archive, readmission.
- Transfer certificate request, approvals, dues clearance, issue, verification, and destination acceptance.
- Timeline showing only data the viewer is authorized to see.
- Student/guardian self-service profile corrections with approval.
- Record retention, legal hold, correction, export, and deletion/anonymization policy.

Status: 🟡 basic entities and StudentService exist; complete lifecycle API/UI is incomplete.

### 6.8 Attendance

Models: AttendanceSession, StudentAttendance, EmployeeAttendance, HRAttendanceLog, LeaveType, LeaveApplication, Holiday, Shift.

Required functions:

- Daily, period-wise, course/session-wise, and online attendance.
- Manual, QR, device, biometric-import, and API sources.
- Late/leave/excused rules, attendance correction request, approval and lock.
- Duplicate punch/session prevention and timezone-safe processing.
- Student/guardian notifications and attendance percentage.
- Eligibility alerts and institution-configured thresholds.
- Employee shift, roster, overtime, leave and payroll integration.

Status: 🟡 entity foundation.

### 6.9 Examination, assessment, result, and certification

Models: Assessment, AssessmentSubject, AssessmentSchedule, Exam, ExamSchedule, Question, OnlineExam, OnlineExamQuestion, OnlineExamAttempt, MarkEntry, StudentAssessmentMark, GradeRule, ResultPublish, ExamResult, Tabulation, ExamHall, SeatPlan, AdmitCard, Quiz, QuizResult, CertificateTemplate, IdCard, TransferCertificate.

Required functions:

- Assessment types: exam, quiz, assignment, practical, viva, project, presentation.
- Marks/weight/components, pass rules, optional subjects, credits, GPA/CGPA, ranking.
- Exam routine, hall, capacity, seat plan, admit card, invigilator.
- Teacher marks entry → submit → reviewer lock → controller approval → result publish.
- Correction, re-scrutiny, improvement, retake, absent, withheld, and result version history.
- Tabulation, report card, transcript, certificate, QR verification, and revocation.
- Result publication must never overwrite the historical grading/curriculum snapshot.

Status: 🟡 extensive entities exist; complete guarded workflow is incomplete.

### 6.10 Fees, accounting, and institutional finance

Models: FeeHead, FeeType, FeeStructure, StudentFee, StudentInvoice, Invoice, InvoiceItem, Payment, Discount, StudentDiscount, Fine, BankAccount, Account, Ledger, Voucher, VoucherDetail, IncomeCategory, Income, ExpenseCategory, Expense.

Required functions:

- Fee structure by programme/level/batch/student with recurring and one-time charges.
- Scholarship, waiver, sibling discount, late fine, instalment, advance, due, refund.
- Invoice generation, collection, receipt, gateway reconciliation, and payment reversal.
- Cash/bank/mobile-financial-service accounts and double-entry vouchers.
- Approval, period lock, audit trail, day book, ledger, trial balance, income/expense, balance sheet.
- Subscription billing and institution student-fee accounting remain separate bounded contexts.
- Financial write operations require idempotency and immutable reference numbers.

Status: 🟡 entity foundation; SaaS subscription payment flow is more complete than institution accounting.

### 6.11 HR, employee attendance, and payroll

Models: Employee, Designation, HREmployee, HRDepartment, HRDesignation, HRShift, HRAttendanceLog, HRLeaveType, HRLeaveApplication, HRSalaryStructure, SalaryStructure, Payroll, HRPayroll, Increment, Bonus, LoanAdvance.

Required functions:

- Employee profile, appointment, contract, document, department, designation, campus.
- Shift/roster, attendance, leave, holidays, overtime, and adjustment approval.
- Salary structure, allowance, deduction, tax, loan/advance, increment and bonus.
- Payroll draft → review → approve → post → payslip → bank/mobile disbursement export.
- Separation, clearance, final settlement and experience certificate.

Status: 🟡 models exist. Duplicate legacy/new HR and payroll entities must be consolidated.

### 6.12 LMS and digital learning

Models: Course, Lesson, LiveClass, Assignment, AssignmentSubmission, Homework, HomeworkSubmission, Quiz, QuizResult, Question, Document.

Required functions:

- Course catalogue, batch/course enrolment, lessons, files, video/link content.
- Release schedule, prerequisite, completion rules and learner progress.
- Live-class provider adapters, attendance and recording link.
- Assignment/homework submission, rubric, feedback, plagiarism integration point.
- Quiz/question bank, attempt rules, randomization, timing and result.
- Certificate eligibility and issue.
- Mobile/PWA-friendly low-bandwidth experience and resumable uploads.

Status: 🟡 entity foundation.

### 6.13 Library

Models: Book, BookCategory, BookIssue, Student, Employee, Fine.

Required functions:

- Title, author, publisher, ISBN, category, physical/digital copy and barcode.
- Issue, return, renewal, reservation, lost/damaged, fine and waiver.
- Student/employee eligibility and circulation policy by user group.
- Catalogue search, stock report and overdue reminders.

Status: 🟡 entity foundation.

### 6.14 Transport

Models: Route, Stopage, Vehicle, TransportAssign, StudentTransport, Student, Campus.

Required functions:

- Vehicle, driver/helper, route, stop, schedule and capacity.
- Student/staff assignment, pickup/drop, effective dates and transport fees.
- Maintenance/fitness/insurance reminders and incident records.
- Optional GPS provider integration and guardian notification.

Status: 🟡 entity foundation; spelling and duplicate assignment models require cleanup.

### 6.15 Hostel

Models: Hostel, HostelRoom, HostelStudent, StudentHostel, Student, Visitor.

Required functions:

- Hostel/building/floor/room/bed inventory.
- Allocation, move, checkout, capacity, dues and meal/service options.
- Visitor, leave/gate pass, complaint, incident and warden workflow.
- Hostel fee integration and occupancy reports.

Status: 🟡 entity foundation; duplicate allocation models require consolidation.

### 6.16 Inventory, assets, and procurement

Models: ItemCategory, Item, Supplier, Purchase, PurchaseDetail, Stock, Asset, AssetMaintenance.

Required functions:

- Item/service catalogue, unit, store/location, supplier and opening balance.
- Requisition, approval, purchase order, receive, issue, return, transfer and adjustment.
- Batch/serial/warranty where applicable.
- Fixed asset assignment, depreciation policy, maintenance and disposal.
- Stock ledger and tenant/campus/store isolation.

Status: 🟡 entity foundation.

### 6.17 Communication and engagement

Models: NoticeCategory, Notice, Notification, NotificationPreference, DeviceToken, MessageTemplate, Message, MessageQueue, FeeReminder, SmsGateway, Complaint, Survey, SurveyQuestion, SurveyResponse.

Required functions:

- In-app, push, email and SMS notifications.
- Audience targeting by tenant, campus, programme, batch, role, student and guardian.
- Approved reusable templates, Bangla/English localization and variable validation.
- Queue, retry, deduplication, provider callback, delivery report, unsubscribe/preference.
- Notice acknowledgement, fee reminder, emergency alert, complaint and survey.
- Provider credentials remain tenant-encrypted; message content must follow privacy policy.

Status: 🟡 models exist; email/SMS auth jobs and tenant gateway settings are partially implemented.

### 6.18 Documents, reports, import/export, and verification

Models: Document, DocumentTemplate, IdCard, CertificateTemplate, ImportLog, BackupHistory, Album, AlbumPhoto.

Required functions:

- Controlled upload with MIME/signature validation, malware scanning, object storage, checksum and retention.
- Template-driven ID cards, admit cards, receipts, certificates, transcripts and letters.
- PDF/Excel export based on permission and data classification.
- Large export as an asynchronous job with expiry, encryption and audit.
- QR/public verification endpoint returns only document validity and explicitly public fields.
- Bulk import preview, mapping, validation, commit, error report and rollback strategy.

Status: 🟡 entity foundation. Production virus scanning and external object storage are not yet complete.

### 6.19 Audit, integration, automation, and system operations

Models: AuditLog, ApiKey, WebhookEndpoint, ScheduledJob, BackupHistory, ImportLog, Dashboard, Language, Currency.

Required functions:

- Immutable security/business audit events with actor, tenant, reason, target and correlation ID.
- API keys stored as hashes; scopes, expiry, rotation, revocation and usage audit.
- Signed webhooks with retry, dead-letter handling and replay protection.
- Idempotent scheduled/background jobs.
- Backup status and tested restore procedure.
- Tenant dashboards and platform operational dashboards.
- OpenTelemetry-compatible logs, metrics, traces, health and alerting.

Status: ✅ audit API and Hangfire host exist; 🟡 most operational workflows remain incomplete.

### 6.20 AI and analytics

Models: AttendancePrediction, ResultAnalytics, ChatbotConversation.

Planned functions:

- Attendance risk, result trend and learning-support insights.
- Institution knowledge assistant and guided setup.
- Explainable recommendations with confidence and source.
- Human approval for decisions that affect admission, grading, discipline or access.
- PII minimization, opt-out, prompt/output audit, provider data policy and cost quota.
- AI must never invent an official academic record or autonomously change a result.

Status: 🟡 experimental foundation; not production decision automation.

---

## 7. Current API surface

These are the meaningful API areas currently present:

| Route area | Current functions |
|---|---|
| /api/auth | Login, forgot password, reset password, logout |
| /api/platform-catalog | Public institution presets, preset detail and active product modules |
| /api/tenant-modules | Current tenant module availability and TenantAdmin activation changes |
| /api/institution-onboarding | Signup, email verification, profile, campus, academic year/term, final completion |
| /api/onboarding | Status and step completion |
| /api/subscription-plans | Public plan list, plan detail, code lookup, comparison |
| /api/subscription | Create/current/history, cancellation, auto-renew, invoices |
| /api/subscription-payment | AamarPay initiation/callback/IPN, configured manual payment, private SuperAdmin receipt download and verification |
| /api/tenant-profile | Profile, branding, logo/favicon, subdomain, general settings |
| /api/tenant-settings | TenantAdmin SMS/email gateway settings and categories |
| /api/dashboard | Authenticated dashboard data |
| /api/v1/auditlog | Filter, record/user history and export |

Every new module must add a complete vertical slice: request/response contract, validation, authorization, service, repository/query, migration, UI if required, tests, audit, documentation, and operational monitoring.

Module-owned controllers/actions must use `[RequireModule("MODULE_CODE")]`. Availability requires all three conditions: the platform module is active, the tenant selected it (or the preset requires it), and the tenant's current paid/trial plan includes a mapped granular feature. Pending payment never grants paid module access.

---

## 8. Data ownership and privacy boundaries

| Data | Owner/scope | Default visibility |
|---|---|---|
| Subscription plans/features | Platform | Public or SuperAdmin-managed |
| Tenant profile/branding | Institution | Public fields only when configured |
| Tenant settings/gateway secrets | Institution | TenantAdmin; secret values masked |
| User authentication secrets | Platform identity system | Never exposed |
| Student institution profile | Institution | Authorized institution members and linked user |
| Academic/attendance/fee records | Institution | Purpose-based institution access; student/guardian views |
| Global identity match data | Platform protected boundary | Identity service only |
| Health/behaviour data | Institution, highly sensitive | Explicit narrow permission |
| Audit data | Platform/tenant according to event | Auditors and authorized administrators |

### Cross-institution access matrix

| Viewer | Basic identity | Current institution record | Other institution history |
|---|---|---|---|
| Student | Own verified data | Own records | Own permitted timeline |
| Guardian | Linked minor/dependent | Allowed linked records | Consent/policy dependent |
| Current institution | Minimum required | Authorized local records | Not by default |
| New institution | Match confirmation only | New local record | Consent/legal transfer package only |
| Public user | No minor profile by default | None | None |
| Platform support | Metadata only | None by default | None by default |
| Authorized break-glass admin | Approved minimum | Time-limited | Time-limited, reasoned and audited |

---

## 9. Multi-tenancy rules

- Every institution-owned entity implements ITenantScopedEntity, normally through BaseTenantEntity.
- Normal requests never accept TenantId from the body as authorization.
- Tenant context comes from the authenticated and trusted request context.
- Global EF Core filters apply tenant and soft-delete boundaries.
- No valid tenant context means no tenant-owned rows are returned.
- SaveChanges rejects cross-tenant inserts and updates.
- IgnoreQueryFilters is reserved for explicit platform/background paths with a target tenant and audit.
- Unique constraints and indexes must include TenantId when uniqueness is institution-local.
- Cache keys, files, jobs, webhooks, reports and search indexes must also be tenant-scoped.
- Cross-tenant reporting belongs to a separately authorized platform analytics boundary.

---

## 10. Architecture

EduOS starts as a modular monolith. This keeps transactions and development manageable while preserving bounded contexts that can later be extracted when measurements justify it.

~~~mermaid
flowchart TD
    Clients["Web, PWA, mobile and integrations"] --> Edge["CDN / WAF / load balancer"]
    Edge --> App["Stateless EduOS.App instances"]
    App --> Service["Application services"]
    Service --> Core["Domain and contracts"]
    Service --> Persistence["EF Core persistence"]
    Persistence --> Sql[("SQL Server")]
    Service --> Cache[("Redis")]
    App --> Jobs["Hangfire / durable workers"]
    Jobs --> Sql
    App --> Files[("Object storage + CDN")]
    App --> Observe["Logs, metrics and traces"]
~~~

### Solution projects

| Project | Responsibility |
|---|---|
| EduOS.App | MVC/API host, middleware, authentication, rate limiting, Swagger and composition root |
| EduOS.Core | Domain entities, DTOs, enums, configuration models and interfaces |
| EduOS.Persistence | EF Core context, mappings, repositories, migrations and seed logic |
| EduOS.Service | Application services, validation, integrations, mapping, caching and helpers |
| EduOS.BackgroundJobs | Scheduled and asynchronous job implementations |
| EduOS.AI | Governed AI-specific application components |
| EduOS.Tests | Persistence, service, security and business-rule tests |

### Target bounded contexts

- Platform & Billing
- Identity & Access
- Institution Configuration
- Admissions
- Academic Structure
- Student Lifecycle
- Attendance
- Assessment & Credentials
- Finance
- HR & Payroll
- Learning/LMS
- Library
- Transport
- Hostel
- Inventory & Assets
- Communication
- Documents & Reporting
- Integration & Audit
- Analytics & AI

Modules communicate through application contracts and domain events. Cross-module side effects should use an outbox pattern rather than hidden multi-service calls.

### Model and schema design rules

EduOS does not add speculative columns just to appear “future-proof.” A model receives a field only when it supports a documented business rule, query, permission boundary, lifecycle, integration, or audit requirement.

- Platform identity and institution-owned records remain separate; government identifiers are encrypted external identifiers, never primary keys.
- Tenant-owned transactional models include tenant scope, stable business identifiers, lifecycle state, timestamps and audit/concurrency fields only where those controls are meaningful.
- Effective-dated rules and immutable snapshots preserve historical fees, grades, subscriptions, curriculum and credentials.
- Core relationships stay normalized and relational. Validated JSON configuration is reserved for tenant-specific optional settings that do not need joins, constraints or frequent reporting.
- Codes used by APIs and integrations are stable and language-neutral; English/Bangla display text is localized at the presentation boundary.
- Indexes follow measured query paths. Institution-local uniqueness includes TenantId and CampusId when campus-local.
- Schema evolution uses additive, reviewed migrations with rollback notes. “No future migration ever” is not a safe database goal; backward-compatible evolution is.

### Responsive PWA and localization baseline

- Supported UI cultures are `en-BD` and `bn-BD`; a validated, HttpOnly culture cookie stores the user's choice.
- The shared shell uses local Bootstrap/JavaScript assets, accessible navigation, mobile drawer behaviour, safe-area spacing and desktop layouts.
- Public sign-in, sign-up, password recovery, email/payment status and error pages use the same local responsive bilingual foundation. Sign-up institution types come from the data-driven platform catalogue rather than hard-coded HTML options.
- The authenticated institution dashboard renders real tenant/subscription/onboarding data, localized stable alert codes, safe same-origin actions and responsive capacity views. The SuperAdmin landing page is role-restricted and does not show invented platform metrics.
- The web app manifest enables installation. The service worker caches only same-origin static assets.
- API responses, uploaded tenant files, authenticated pages and personal/institution data are never added to the offline cache. Offline navigation shows a bilingual reconnect page.
- Stable onboarding step codes let web/mobile clients localize display text without changing the API state machine.

---

## 11. Scale and reliability plan

Millions of students and high requests per second require measured evolution:

### Application layer

- Stateless API nodes behind a load balancer.
- Async I/O, cancellation tokens, bounded pagination and request-size limits.
- Read/write timeouts, circuit breakers and retry only for safe transient operations.
- Idempotency keys for payment, admission, import, attendance device and webhook writes.
- Versioned APIs and backward-compatible contracts.

### Database

- SQL Server remains the source of truth for core transactions.
- Composite indexes begin with TenantId for tenant-owned access patterns.
- Query plans, slow queries, lock waits and connection-pool usage are monitored.
- Optimistic concurrency for conflicting edits.
- Keyset pagination for large timelines instead of deep offset paging.
- Archival/partitioning by tenant and date after real volume analysis.
- Read replicas/reporting store only when consistency requirements allow.

### Cache and queues

- Redis for safe derived/reference caches, distributed coordination and quotas.
- Cache keys always include tenant and configuration version.
- Transactional outbox for reliable events.
- Idempotent workers, retry with backoff, dead-letter handling and job observability.
- Heavy exports, messaging, media and analytics run outside request threads.

### Files and search

- Object storage with signed URLs, checksums, lifecycle policy and CDN.
- Search index contains only permitted, minimal fields and supports tenant deletion/reindex.
- Database/file backup plus regularly tested restore.

### Release and observability

- Health/readiness checks, structured logs, metrics, distributed traces and correlation IDs.
- Canary/rolling releases with backward-compatible migrations.
- Load tests for login, attendance burst, result publication, fee collection and report export.
- Per-tenant quotas and abuse protection prevent one tenant from exhausting the platform.

No scale claim is accepted until representative load tests define p50/p95/p99 latency, throughput, error rate, resource usage and recovery targets.

---

## 12. Security baseline

Implemented Phase 0 controls:

- Parameterized tenant and soft-delete global query filters.
- Deny-by-default tenant reads without a valid tenant.
- Cross-tenant SaveChanges protection.
- Explicit platform/system repository access for reviewed callbacks and background work.
- ASP.NET Core Data Protection encryption and response masking for tenant SMTP/SMS secrets.
- TenantAdmin-only gateway configuration endpoints.
- Payment invoice, amount and currency checks plus unpredictable transaction IDs.
- Partitioned rate limiting for authentication/onboarding/payment callback surfaces.
- Production automatic database migration disabled by default.
- Secrets removed from current public configuration.
- Tenant-isolation and tenant-secret automated tests.
- GitHub Actions CI, Dependabot, SECURITY.md and AGENTS.md.

Still required before production:

- Rotate/revoke every credential previously committed to Git history.
- Migrate existing BirthCertNo and Guardian NID data to the protected identity model.
- MFA and break-glass workflow for privileged platform operations.
- Key management strategy for encryption and keyed identifier lookup.
- CSRF/secure-cookie/API token threat review for each client mode.
- File signature validation, malware scanning and external object storage.
- Dependency/code/secret scanning and penetration testing.
- Data retention, consent, correction, export, breach response and deletion policies.
- Bangladesh legal/privacy review before national identity matching is enabled.

Read [SECURITY.md](SECURITY.md) before handling credentials, personal data, or vulnerability reports.

---

## 13. Known structural gaps

These are explicit priorities, not hidden technical debt:

1. **Duplicate academic model families:** EduOS.Core.Entities.Academic and EduOS.Core.Entities.Academics overlap. A reviewed migration must choose the canonical programme/level/batch model.
2. **Duplicate HR/payroll/hostel concepts:** legacy and newer entities must be consolidated before adding production workflows.
3. **Sensitive identity fields:** current Student.BirthCertNo and Guardian.NID shapes are not the final encrypted global-identity design.
4. **CustomFieldValue scoping:** tenant ownership and entity-reference integrity must be made explicit.
5. **Entity-heavy modules:** many modules do not yet have complete validation, permissions, APIs, UI, reports or tests.
6. **String statuses:** remaining free-text status fields should move to validated enums/state machines where compatibility allows.
7. **Audit sensitivity:** logs must redact secrets and highly sensitive personal values.
8. **Background jobs:** recurring production jobs need idempotency, locking, alerting and outbox integration.
9. **Database migrations:** existing migration history must not be edited after deployment; new reviewed migrations are required.

---

## 14. Local development

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- SQL Server 2019 or newer
- Git
- Redis when testing distributed-cache behaviour

### Setup

~~~bash
git clone https://github.com/cseashrafulislam/EduOS.git
cd EduOS
dotnet restore EduOS.slnx
~~~

Configure local secrets outside source control:

~~~bash
dotnet user-secrets --project EduOS.App set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=EduOS;Trusted_Connection=true;TrustServerCertificate=true;"
dotnet user-secrets --project EduOS.App set "JwtSettings:Secret" "replace-with-at-least-32-random-characters"
~~~

Optional email, SMS, and payment key names are documented in [.env.example](.env.example). The sample file is documentation only; ASP.NET Core does not automatically load it.

Start the application:

~~~bash
dotnet run --project EduOS.App
~~~

Current Development configuration enables reviewed migration/seed initialization at startup. Production disables automatic initialization and requires a controlled migration release step.

Development endpoints:

- **/swagger** — API documentation
- **/health** — health endpoint
- **/hangfire** — authorized background-job dashboard

PWA testing must use HTTPS (or localhost). In browser developer tools, verify the manifest and service worker, switch to offline mode, and confirm that only the bilingual offline page—not a previously authenticated page—is shown.

---

## 15. Configuration and secrets

ASP.NET Core environment-variable nesting uses double underscores:

~~~text
ConnectionStrings__DefaultConnection
DataProtection__KeysPath
JwtSettings__Secret
EmailSettings__SenderEmail
EmailSettings__Password
SmsSettings__ApiKey
SmsSettings__ApiSecret
Payments__AamarPay__StoreId
Payments__AamarPay__SignatureKey
Payments__AamarPay__CallbackBaseUrl
ManualPayment__BankName
ManualPayment__AccountName
ManualPayment__AccountNumber
ManualPayment__RoutingNumber
ManualPayment__BranchName
ManualPayment__Instructions
FileStorage__PrivateBasePath
SSLCommerz__StoreId
SSLCommerz__StorePassword
~~~

Production requirements:

- Use deployment environment variables or a managed secret store.
- Never commit credentials, tokens, private keys or production personal data.
- All application instances share a protected, durable Data Protection key ring.
- `Payments__AamarPay__CallbackBaseUrl` is the trusted public HTTPS origin; online checkout is disabled when it is missing or invalid.
- Manual bank details must come from reviewed deployment configuration. Placeholder account details are not rendered.
- `FileStorage__PrivateBasePath` must be durable, backed up, access-controlled, and outside every static web root. Production receipt uploads also require an operational malware scanner or quarantined object-storage pipeline.
- Secret rotation must support old/new overlap where provider behaviour requires it.
- Removing a value from the latest Git file does not remove it from Git history.

---

## 16. Build, test, and CI

Run the same quality checks used by GitHub Actions:

~~~bash
dotnet restore EduOS.slnx
dotnet build EduOS.slnx --configuration Release --no-restore
dotnet test EduOS.slnx --configuration Release --no-build
~~~

Minimum test groups:

- Tenant read/write isolation.
- Role and permission boundaries.
- Payment amount/currency/idempotency/callback state.
- Subscription entitlement and quota.
- Sensitive-setting encryption/masking.
- Admission and identity duplicate handling.
- Attendance duplicate/conflict rules.
- Marks approval/result publication immutability.
- Finance double-entry and reversal rules.
- Cross-institution consent and access denial.
- Migration and backward-compatibility smoke tests.

---

## 17. Development and pull-request rules

1. Work on a focused feature branch; do not commit directly to master.
2. Deliver one vertical workflow or two/three closely related tables at a time.
3. Do not add unused placeholder controllers/services/entities.
4. Add authorization, validation, tests, audit and documentation with the feature.
5. Never use request TenantId as authorization.
6. Never silently edit a migration that may already be deployed.
7. Avoid breaking existing API/database behaviour without a documented migration path.
8. Confirm no secret or personal data was introduced.
9. Wait for CI and human review before merge.
10. State tenant, privacy, migration, rollback and operational impact in the PR.

Repository agent rules are defined in [AGENTS.md](AGENTS.md).

### Definition of done

A feature is complete only when:

- Business acceptance criteria are documented.
- Tenant and campus scope are correct.
- Permission and sensitive-data policies are enforced server-side.
- Validation and state transitions are explicit.
- Database indexes/constraints/migration are reviewed.
- Retry/idempotency behaviour is defined.
- Audit events and observability exist.
- API and UI handle empty/error/loading/access-denied states.
- Automated tests cover happy path and security boundaries.
- Documentation and rollback notes are updated.
- CI passes.

---

## 18. Implementation roadmap

### Phase 0 — Security and repository foundation ✅

- Tenant isolation.
- Cross-tenant write protection.
- Secret management baseline.
- Payment validation and rate limiting.
- CI, security policy, agent rules and project documentation.

### Phase 1 — Configurable SaaS core

Progress: institution preset catalogue, top-level product module catalogue, preset-to-module and module-to-plan-feature mapping, tenant module selection, subscription entitlement evaluation, reusable module authorization policy, onboarding preset application, bilingual responsive shared/public/dashboard UI and PWA foundation, migrations and regression tests are implemented. Quota enforcement, full view-by-view localization and remaining configuration engines are pending.

Deliver:

- Canonical InstitutionTypeDefinition and preset catalogue.
- ModuleDefinition, TenantModule and plan-entitlement middleware.
- Canonical campus/academic programme/level/batch model.
- Terminology, numbering, policy and configurable form foundation.
- Complete resumable onboarding wizard.
- Subscription quota enforcement and usage counters.

Acceptance:

- A school, university and coaching centre can independently self-configure without code changes.
- Disabled/unpaid modules are inaccessible through UI, API, job and report.
- Tenant/campus isolation and configuration-version tests pass.

### Phase 2 — Global identity and student lifecycle

Deliver:

- Person, encrypted PersonIdentifier and keyed lookup.
- Learner identity, guardian relationship and verified contact.
- Consent/access/break-glass models.
- Institution membership, enrolment, promotion, transfer and timeline.
- Safe migration from existing student/guardian identity fields.

Acceptance:

- The same person can join multiple institutions without merging private records.
- Identifier search leaks no personal information.
- Every cross-institution action is consented/authorized and audited.

### Phase 3 — Academic operations

Deliver:

- Curriculum, subject registration, instructor assignment, routine and calendar.
- Admission workflow.
- Attendance session workflow.
- Assessment, marks approval, result, transcript and certificate verification.

Acceptance:

- One complete school flow and one university/training flow operate on the same configurable core.
- Historical curriculum and grading snapshots remain reproducible.

### Phase 4 — Institution operations

Deliver:

- Student fees and accounting.
- HR, attendance and payroll.
- LMS, library, transport, hostel and inventory.
- Communication, documents, import/export and operational reports.

Acceptance:

- Cross-module postings/events are traceable and idempotent.
- Each module has role, tenant, privacy, report and test coverage.

### Phase 5 — Scale, integrations, and intelligence

Deliver:

- Outbox/event processing and durable workers.
- Object storage/CDN and governed search.
- Observability, disaster recovery and security operations.
- Board/payment/device/webhook integrations.
- Analytics and governed AI.
- Load, failover and recovery testing.

Acceptance:

- Published SLOs and benchmark results support the claimed deployment scale.
- Backup restore, incident response and tenant data export/deletion are rehearsed.

---

## 19. Suggested first production pilot

Do not start with every module. A safe pilot should include:

1. One institution type preset.
2. Signup, subscription/trial and onboarding.
3. Campus, academic year, programme/level/batch.
4. Users, roles and invitations.
5. Student/guardian/enrolment.
6. Attendance.
7. Basic fee invoice/payment/receipt.
8. Notices and dashboards.
9. Audit, backup, monitoring and support process.

After real users validate the workflow, expand into examination/results, accounting, HR/payroll, LMS and operational modules.

---

## 20. Repository map

~~~text
EduOS/
├── EduOS.App/             Web/API host, middleware, controllers and UI
├── EduOS.Core/            Domain entities, DTOs, enums and interfaces
├── EduOS.Persistence/     EF Core context, mappings, migrations and repositories
├── EduOS.Service/         Application services, validation and integrations
├── EduOS.BackgroundJobs/  Background and scheduled jobs
├── EduOS.AI/              Governed AI components
├── EduOS.Tests/           Automated tests
├── .github/               CI and dependency automation
├── .env.example           Environment key reference
├── AGENTS.md              Repository implementation rules
└── SECURITY.md            Security and vulnerability guidance
~~~

---

## 21. Important credential notice

If an older revision of this public repository was cloned or deployed, assume that any SMTP, SMS, or payment credential previously committed is compromised. Revoke or rotate it at the provider. Deleting it from current configuration is not enough because Git history retains earlier values.

---

## 22. Final product success criteria

EduOS can call itself a complete education operating system only when:

- Multiple institution types operate without separate code forks.
- A new institution can self-onboard and complete first setup unaided.
- Student/guardian/teacher/admin portals cover their daily work.
- Historical enrolment and credential data are accurate and privacy-controlled.
- Cross-institution identity matching is lawful, consented and audited.
- Subscription/module/usage enforcement cannot be bypassed.
- Finance, results and certificates are reproducible and tamper-evident.
- Tenant isolation is continuously tested.
- Backup restore, security response and data-rights workflows are operational.
- Load tests and monitoring demonstrate the advertised scale.
