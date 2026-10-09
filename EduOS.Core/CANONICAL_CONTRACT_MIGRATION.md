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
