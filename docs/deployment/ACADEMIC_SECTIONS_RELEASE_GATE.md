# EduOS Academic Batch / Academic Section rollout gate

**Branch:** `Ashraful`. **Schema migration:** `20261113010000_AcademicSectionFoundation`.

## Contract

- `AcademicBatches` remains the cohort/master record and retains existing `AcademicBatchId` in all dependent modules.
- `AcademicSections` stores an explicit, tenant-scoped Section within the Batch.
- Historic `AcademicBatchId` relationships are not rewritten or dropped. One read-only `IsLegacyDefault` section is inserted per existing batch.
- New Section placement is explicit for a multi-section batch; batch-only legacy clients without configured sections remain supported.
- Enrollment and stock/financial actions are never changed by a Section edit.
- `StudentEnrollments`, `SubjectOfferings`, `AttendanceSessions`, `ResultPublications` and `FeeStructures` have nullable additive `AcademicSectionId`.

## Pre-deployment and release sequence

1. Take and verify a restorable SQL Server backup. Record the exact commit being deployed, previous migration state, and tenant count.
2. Run .NET restore/build and **the full solution tests** against the exact commit. For example:
   `dotnet test EduOS.sln --configuration Release` (use the repository's actual solution filename if different).
3. Run the JavaScript regression tests:
   `node --test EduOS.Tests/JavaScript/academic-section-contract.test.cjs EduOS.Tests/JavaScript/academic-section-integrations.test.cjs`.
4. Apply EF SQL Server migrations in staging first. Inspect generated SQL/EF migration history, duplicate indexes and index locks. Rehearse against a restored production-size database.
5. Execute `scripts/migrations/backfill-academic-sections.sql` after the migration and **before enabling new section writes**. Run again until all five Unmapped counts are zero for rows with a legacy Batch ID. Reconcile representative tenants; Section mapping is deterministic, never guessed by Class/Name.
6. Release the server application and frontend together; clear/version static asset caches. Inspect `/AcademicManagement/Batches`, `/AcademicManagement/Sections`, Section CRUD and permissions. Check concurrent client versions against the additive API.
7. Repeat the exact-commit SQL Server integration, permission and UI/browser smoke suites before general enablement.

## Minimum acceptance and negative tests

| Scenario | Required outcome |
| --- | --- |
| Batch and Section creation | Separate table IDs; Section validates active parent Batch, tenant and normalized unique code |
| Repeated create request | Same key and body returns prior Section; different body conflicts |
| Stale update/delete | Conflict without loss of current values |
| Delete used Section | Blocked; audited deletion allowed only when no dependent work references it |
| Cross-tenant Section ID | 403/404/409, never appears in another tenant's list or changes another tenant's data |
| Admission / running entry / enrollment | Explicit selected Section persisted; mismatched Section blocked |
| Promotion / readmission | Section validated under target Batch, capacity serialized; previous Enrollment remains immutable |
| Student Import | Multi-section Batch without selected placement blocks instead of silently assigning |
| Attendance | Section-specific roster and sessions, no posting of another Section's student |
| Routine | Separate Sections can run simultaneously; teacher/room collisions still blocked |
| Exam / Results | Subject offering shows Section, mark roster resolves its original enrollment; published history not rewritten |
| Legacy history | Backfilled Section identity, no altered historic Batch IDs, counts and reports unchanged |
| Forms | No duplicate fields, clear empty/error/success states, mobile and bilingual checks |
| Performance | Server paging, tenant/index filtering and no unbounded lookups |
| Operations | Migration/rollback rehearsed; latest commit's workflow runs and integration results recorded |

## Rollback safety

The migration is additive. **Never run Down or delete `AcademicSections` after users have entered data**; that would destroy new Sections and references. For a post-write issue, disable the new UI/API, roll back application code only after checking its compatibility with the retained schema, and reconcile data before resuming. No automatic destructive rollback is authorized.

## QA evidence

Source-contract checks and JavaScript syntax checks are useful preliminary signals, **not** proof of successful .NET compilation, SQL migration, browser acceptance, tenant-level integration, or production readiness. No deployment is authorized without those recorded results.
