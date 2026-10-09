# EduOS.Core canonical contract migration notes

**Scope:** Core contract cleanup only. This file is an integration/migration specification, **not** a database migration or evidence that dependent assemblies compile. Before deploying, perform an additive data migration and update Persistence, Service, App, and tests in coordinated releases. Do not recreate retired aliases.

## Canonical ownership

| Fact / workflow | Single owner | Removed or retired alternative |
|---|---|---|
| Account hierarchy | `Account.ParentAccountId` | `Account.ParentId` CLR compatibility alias |
| Institution domain aliases | `TenantDomain.HostName` | `Tenant.CustomDomain` |
| Platform-issued subdomain | `Tenant.Subdomain` | Do not store another copy in TenantSetting |
| Active tenant state | `Tenant.State` | `Tenant.IsActive` |
| Email verification | `Tenant.EmailVerifiedAt` | `Tenant.IsEmailVerified` |
| Tenant onboarding completion | `Tenant.OnboardingStage` / `Tenant.OnboardingCompletedAt` | `Tenant.IsOnboardingComplete`, old `OnboardingStep` |
| Student lifecycle | `Student.StatusCode` plus audited `StudentStatusHistory` | `Student.IsActive` |
| Enrollment lifecycle | `StudentEnrollment.State` | `StudentEnrollment.IsActive` |
| Vehicle-driver ownership | `TransportDriver` / `VehicleDriverAssignment` | `Vehicle.DriverName` and `Vehicle.DriverPhone` |
| Book reservation workflow | `BookReservation.State` | `IsFulfilled` and `IsCancelled` |
| Student leave | `StudentLeaveApplication` | No duplicate attendance flag |
| Employee leave balance | entitlements + approved applications + signed adjustment rows | no independent editable balance |
| Tenant profile read/write | SaaS `TenantDto`, `UpdateTenantProfileRequestDto`, `UpdateTenantRegionalSettingsRequestDto` | DTOs.Tenants legacy TenantProfile/Edit/Branding/GeneralSettings shapes |
| Communication gateways | `CommunicationGateway` and corresponding DTOs | TenantSetting SMS/SMTP gateway DTO duplicates |
| Assessment | `Assessment`, `AssessmentSubject`, `StudentAssessmentMark`, `ResultPublication` | old Exam/Class/Section mark workflow DTOs |
| Academic organization | `AcademicLevel`, `AcademicBatch`, `AcademicTrack` | ambiguous Class/Section/Group business identifiers |

Names inside presentation strings such as **Class**, **Section**, **Group**, or **Exam** may be exposed by `TenantTerminology`; do not reintroduce them as persistence entity identifiers.

## Old onboarding values need explicit translation

The old `EduOS.Core.Enums.OnboardingStep` values are **not** numerically equivalent to `EduOS.Core.Enums.Domain.OnboardingStage`. Never reinterpret persisted old integer values as the new enum by casting.

| Old persisted value | Old label | New `OnboardingStage` value |
|---:|---|---:|
| 0 | EmailVerification | 1 |
| 1 | InstitutionProfile | 2 |
| 2 | PlanSelection | 3 |
| 3 | Payment | 4 |
| 4 | CampusSetup | 5 |
| 5 | AcademicSetup | 6 |
| 9 | ModuleSetup | 7 |
| 6 | BrandingSetup | 8 |
| 7 | GeneralSettings | 9 |
| 8 | GatewaySetup | 10 |
| 99 | Completed | 11 |

Determine whether an existing database column contains these **old wizard values** or already stores the new canonical `Tenant.OnboardingStage` values before any data rewrite. Apply a one-time mapping only to confirmed legacy data, stage by tenant, log old/new values, and reject unknown values. Old numeric identifiers are ambiguous without knowing their source.

## Deployment / integrity prerequisites

1. Inventory all persisted schema versions and existing tenant records before changing entity columns. Backup and test restore first.
2. For `Tenant.CustomDomain`, upsert non-empty normalized hostnames into `TenantDomain` with uniqueness and verification ownership checks; resolve collisions before removing the former column.
3. For each removed lifecycle boolean, reconcile historical combinations with the canonical enum/date. Stop on inconsistent records; do not silently change status or treat nulls as approvals.
4. For `BookReservation`, map valid combinations to Pending/Fulfilled/Cancelled; determine Expired from policy and expiration date. Any combination Fulfilled+Cancelled must be explicitly reconciled.
5. For vehicle-driver data, reconcile text fields with `TransportDriver` and effective-dated `VehicleDriverAssignment`; do not overwrite historical driver identity.
6. Preserve `PersonDataSnapshotAt`, historical invoice line snapshots, payment allocations and payroll snapshots: these are **intentional historical facts**, not duplicate live master values.
7. Update and build implementations **after** canonical Core contracts: Persistence → Service → API → client → tests. Ensure update/edit operations enforce tenant authorization, state transitions and optimistic concurrency; enforce tenant-scoped uniqueness and financial transaction boundaries in the database.
8. Run `dotnet build EduOS.Core/EduOS.Core.csproj -c Release`, then full solution build/tests and a staged data migration dry-run. Core PASS alone does not authorize production deployment.

## Removed Core DTO / interface families

- Hostel/Transport read/write DTO alias subclasses: use `StudentHostelAllocationDto`, `AllocateStudentHostelRequestDto`, `RouteDto`, `VehicleDto` and their canonical request DTOs.
- Old attendance `ClassId`/`SectionId` roster and string status contract: use `AcademicBatchId`, `AttendanceSessionId`, `AttendanceState` and a single attendance register contract.
- Old `IExamWorkflowService`: use `IAssessmentAdministrationService` with `Assessment` workflow read models.
- Upload transport `IFormFile`: map web-layer uploads to `PrivateFileUploadDto` only at the boundary. Validate and stream safely, then authorize storage and downloads.
- Redundant tenant-profile and OnboardingStep contracts: use `TenantDto`, `OnboardingStage`, domain setting and communication administration services.

## Canonical repository contracts (October 2026 audit)

Repository contracts were consolidated around the **actual identity of each business record**, rather than legacy display terminology. Update dependent implementations in a coordinated migration; no compatibility alias should be recreated in Core.

| Area | Legacy API / ambiguity | Canonical contract |
|---|---|---|
| Subjects | GetByClassId / GetByClassAndGroup | GetByAcademicLevel / GetByCurriculumAndLevel |
| Student directory | GetWithGuardians returning a bare Student; GetByClassSection | Guardian membership via StudentGuardian; GetByAcademicBatch |
| Roll uniqueness | IsRollExistsInSection | IsRollAssignedInBatch using AcademicBatchId and excludeEnrollmentId |
| Student code | GenerateStudentCode on repository | NumberSeries service with idempotency key |
| Enrollment | GetCurrent(student, academicYear), GetByAcademicLevelAndBatch | GetCurrent(student), GetByAcademicBatch(page,size) |
| Student attendance | IsAlreadyMarked(student,date) | IsAlreadyMarked(AttendanceSessionId, StudentEnrollmentId) |
| Marks | GetExisting(assessment,student,subject) | GetExisting(AssessmentSubjectId,StudentSubjectRegistrationId) |
| Published result | GetByAssessmentAndStudent | GetByPublicationAndEnrollment (historical publication version) |
| Assessment schedule | GetByDate(DateTime), GetByAssessmentAndLevel | GetByDate(DateOnly), GetByAssessmentSubject |
| Fee structure | GetByClass, GetTotalMonthlyFee (ignores frequency) | GetApplicable(Campus,Program,Level,Year,Batch,EffectiveOn) |
| Invoice queries | status string, unbounded date lists, GenerateInvoiceNo | InvoiceState + DateOnly + paging + NumberSeries |
| Payment queries | assumed direct StudentPayment.InvoiceId | follow PaymentAllocation to StudentInvoice |
| Receipt numbers | GenerateReceiptNo | NumberSeries |
| Admission | status string, GenerateApplicationNo | AdmissionApplicantState + paging + NumberSeries |
| Grading | GetByMark(tenant) | ResolveGrade(GradeSchemeId, normalizedMarks) |
| Employee identity | GetByDepartment, GetTeachers, GenerateEmployeeCode | GetByOrganizationUnit, GetTeachingStaff(page,size), NumberSeries |
| Employee attendance | DateTime-based employee date ranges | DateOnly business dates, bounded queries |
| Employee leave | GetByUser, integer used days | GetByEmployee, decimal approved used days, paged states |
| Academic year | GetCurrent(tenant) | GetCurrent(tenant,campus), campus-scope current uniqueness |

### Required database invariants / server revalidation

- Unique effective attendance record by `(TenantId, AttendanceSessionId, StudentEnrollmentId)`. Check session, enrolled batch, permitted status and attendance finalization **under transaction**.
- Unique student mark by `(TenantId, AssessmentSubjectId, StudentSubjectRegistrationId)`. Validate subject registration belongs to the assessment subject's `SubjectOffering`. Published/locked results require controlled correction and republishing.
- Unique published result by `(TenantId, ResultPublicationId, StudentEnrollmentId)`; preserve `PublicationVersionNo` and previous releases. Rankings must apply within a single publication and controlled scope.
- Unique active/current enrollment roll within `(TenantId, AcademicBatchId, normalized RollNo)` as defined by institution rules. Student code must be tenant-unique even under concurrent create requests.
- Invoice number and receipt number are unique **per tenant**. ClientRequestId/idempotency keys prevent duplicate invoices, payments and journal postings after retries. Revalidate status and allocations transactionally.
- A payment links to an invoice via `PaymentAllocation`; refund reversals and failed/refunded payments must not be counted as successful collections. `PaidAmount` and `DueAmount` are at most *transaction-maintained projections*, never independent editable sources of truth.
- `FeeStructureLine.Frequency` determines billing; a default monthly amount cannot be inferred simply from the existence of a FeeStructure. Account for effective dates, batch specificity, overlapping rules and preexisting issued invoice snapshots.
- `AcademicYear.IsCurrent` has an explicit campus dimension; nullable CampusId must have defined institution-wide precedence. Enforce a tenant/campus uniqueness invariant and change the current year atomically.
- Grade rules are valid only under a particular `GradeSchemeId`. Define whether thresholds use percentages or normalized marks; reject overlap and gaps according to grading policy.
- Repository queries must include tenant scope or be protected by verified tenant filters. Require paging on large lists; do not expose unbounded history or cross-tenant keys.
- Issued numbers must come from a transactional `NumberSeries` allocator, never `MAX()+1`; store an idempotency record for the request and allocate the number at most once.

### Compatibility guidance

Existing Persistence classes implement **old** signatures. This document does **not** claim those implementations were updated. The correct order is: finalize Core contracts, update repository implementations and registrations, update Service, update API mapping, run schema/data migration, then verify tests and publish. Failing downstream builds are expected during this Core-only phase and should not be suppressed by reintroducing legacy interfaces.

## Follow-up contracts: subscriptions, communication, scheduling and transactions

- Subscription Core Repository contracts are now one interface per file: `ISubscriptionPlanRepository`, `ITenantSubscriptionRepository`, `ISubscriptionInvoiceRepository`, `ISubscriptionPaymentRepository`. Do not restore the old multi-interface `ISubscriptionRepositories.cs` file. Invoice-number allocation remains solely with `INumberSeriesService`.
- Subscription expiration and manual-payment verification queries are explicitly *platform-only*. Require host-administrator authorization before resolving tenants and use paging to avoid loading all subscribers/payments into memory.
- `INotificationRepository.MarkAsReadAsync` and `MarkAllAsReadAsync` now receive tenant/user ownership and UTC read timestamp. Marking another user's notification must fail without changing state. Read receipt insertion should be retry-safe.
- `INoticeRepository.GetVisibleToUserAsync` must enforce `NoticeAudience` scope; publishing a notice does not make it visible to everyone. `NoticeReadReceipt` is the read source of truth. Verify campus/program/batch membership from server-side records, not client-selected IDs.
- `IAuditLogRepository` now filters canonical `AuditLog.EntityName` and numeric `EntityId` rather than legacy table/record strings; every tenant query is paged. Records with null `TenantId` are host data and require privileged access.
- `IRoutineEntryRepository` conflict checks must consider effective-date overlap, the exact `RoutineTimeSlot` time range, day of week, and teacher/room. Prevent overlaps transactionally and enforce uniqueness where the schema permits; `TimeOnly` is used for clock times and `DateOnly` for business dates.
- `IInstructorAssignmentRepository` has a primary assignment only **per subject offering**. Do not treat it as an unrelated academic batch adviser. `IAcademicBatchRepository` code uniqueness must consider campus and year, not merely a level.
- `IFeeHeadRepository.GetByDefaultFrequencyAsync` uses `FeeFrequencyType` because `FeeHead` has no string `Type`. Invoice generation must still read the frequency of each `FeeStructureLine`.
- `IAssessmentRepository` state queries are paged and type-safe; the authoritative published result version remains `ResultPublication`.
- `IUnitOfWork` now exposes one persistence-neutral `ExecuteInTransactionAsync` operation. The Persistence implementation **must** open, commit and rollback a transaction, and use EF/provider retry strategy internally when appropriate. The delegate is replayable only if all retry-sensitive actions have reliable idempotency keys and transactional uniqueness. Do not invoke email, SMS, webhooks, uploads, or external payments within a retryable database delegate.
- The generic repository still exposes historical unbounded `GetAllAsync` / `FindAsync` methods and `IQueryable<T>`, inherited by existing callers. Treat those as **remaining architecture debt**: large-list features should exclusively use bounded, sorted SQL queries. A separate migration should remove unrestricted methods only after locating all consumers to avoid silently breaking maintenance jobs or reference lookups.

### Core-only completion boundary

`EduOS.Core` Release Build PASS proves only that the contracts compile. It does **not** prove that 215 entities are covered by working services, that database migrations have been applied, that old implementations match the new repository signatures, or that tenant and concurrency invariants hold in persisted data. All of those must be verified before calling the entire EduOS solution production-ready.

## Audit and academic DTO consolidation (Core-only)

### Audit history

- `IAuditLogService` is read-only: paged `SearchAsync` and bounded `GetStatisticsAsync`. No user-facing `DeleteOldLogsAsync` or unbounded `byte[]` export endpoint. Implement retention with a separately authorized, audited policy after checking legal retention and backup obligations.
- `AuditLogFilterDto.EntityName` and `EntityId` align with `AuditLog.EntityName`/`EntityId`. Tenant scope comes from the authenticated server context, **never** from an untrusted filter value. `FromUtc`/`ToUtc` are UTC instants with validated chronological order and a server-defined maximum range.
- `AuditLogStatisticsDto` uses 64-bit counters; list view is paged, and `CorrelationId`/`RequestId` are carried for diagnostic investigation. Restrict old/new value exposure to appropriately privileged users and redact personal secrets.
- `IGenericRepository` retired unused `FindFirstOrDefaultAsync`, `ExistsAsync` and `DeleteByIdAsync` duplicates. Existing `GetAllAsync`/`FindAsync` and generic `Delete`/`DeleteRange` methods are marked obsolete rather than silently removed, because existing downstream code still calls them. Migrate callers to bounded projections and controlled domain operations before removal.

### Academic authority

- `AcademicSetupDtos.cs` was removed: `IAcademicSetupService` now consumes `SaveAcademicProgramRequestDto`, `SaveAcademicLevelRequestDto`, `SaveAcademicTrackRequestDto`, `SaveSubjectRequestDto`, `SaveAcademicCurriculumRequestDto`, `SaveCurriculumSubjectRequestDto`, `SaveAcademicBatchRequestDto`, and `SaveRoomRequestDto`. Setup option results have a configurable bounded `take` value; enforce a maximum on the server.
- `AcademicEnrollmentDtos.cs` was removed. `StudentEnrollmentDto.State` is the lifecycle owner (do not reconstruct `IsActive`); `CreateStudentEnrollmentRequestDto` now accepts student reference, batch, curriculum, roll, enrollment business date and idempotency key. Resolve and snapshot campus, academic year/term, program, level, track, medium and shift **from the selected batch** and validate the curriculum relationship under the enrollment transaction.
- `AcademicRoutineDtos.cs` and `AcademicInstructionDtos.cs` were retired: assignments, routine entries, substitutions and lesson plans use the canonical DTO family in `AcademicDtos.cs`. For routine entries, optional `InstructorAssignmentId` must match the specified `SubjectOffering`, tenant and effective date.
- Lesson plans have `LessonDate` (`DateOnly`) and workflow `State`, not a parallel ChapterName/StartDate/EndDate or an independent editable progress percentage. Server-enforced state transitions and row version validation govern submit/review.
- `AcademicCalendarDtos.cs` legacy date-typed write DTOs were removed. Campus calendar policy does **not** own `AcademicYearId`; events do. All business dates in calendar read/write requests are `DateOnly`. `AcademicWorkingDayDto` is a derived calendar projection, not a persistent entity.
- The old institution onboarding service duplicated canonical tenant profile, campus, academic year and term operations. Only `IInstitutionRegistrationService` owns institution registration/email verification; `ITenantProfileService`, `IAcademicSetupService`, and `IOnboardingService` own the rest. The obsolete seven SaaS setup/list DTO files were removed.
- `InstitutionSignupRequestDto.ClientRequestId` is a server-validated retry key. Normalize email, enforce tenant/user uniqueness, verify `AgreeTerms` against a versioned terms document and store secure password hashes, never plaintext. Public verification origin must come from trusted configuration, not a caller-supplied `baseUrl`. Do not disclose sequential tenant/user IDs in public signup responses.

### Release requirements

Any consumers of removed DTOs/interfaces must migrate in coordinated later work, rather than restoring legacy aliases in Core. Preserve issued invoices, historical enrollments and published results across migrations. Backfill and verify existing data before enforcing new constraints, and dry-run migrations against a restored production backup. A passing Core compile does not mean Persistence/Service/API are compatible.
