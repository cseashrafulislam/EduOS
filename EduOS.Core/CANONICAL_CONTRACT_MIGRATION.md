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
