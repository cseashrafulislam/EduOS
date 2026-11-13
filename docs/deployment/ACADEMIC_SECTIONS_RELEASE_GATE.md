# EduOS Academic Batch / Academic Section rollout gate

**Branch:** `Ashraful`. **Schema migration:** `20261113010000_AcademicSectionFoundation` + `20261113030000_PruneUnusedBatchGeneratedSections` + `20261113040000_RemoveAcademicSectionLegacyDefault`.

## Contract

- `AcademicBatches` remains the cohort/master record and retains existing `AcademicBatchId` in all dependent modules.
- `AcademicSections` stores an explicit, tenant-scoped Section within the Batch.
- Historical migrations are retained unchanged. The old foundation migration may have generated placeholder sections. The next cleanup deletes only unreferenced placeholders; the final migration **refuses to drop `IsLegacyDefault`** if any generated section remains referenced. Reconcile those records explicitly rather than lose historical links.
- `IsLegacyDefault` does not exist in the active Section entity, API, DTO, EF model, or final database schema. Batches and Sections are permanent first-class settings. One active Section can be selected unambiguously; multiple require a specific Section selection, and zero Sections require an explicit setup step.
- Enrollment and stock/financial actions are never changed by a Section edit.
- `StudentEnrollments`, `SubjectOfferings`, `AttendanceSessions`, `ResultPublications` and `FeeStructures` have nullable additive `AcademicSectionId`.

## Pre-deployment and release sequence

1. Take and verify a restorable SQL Server backup. Record the exact commit being deployed, previous migration state, and tenant count.
2. Run .NET restore/build and **the full solution tests** against the exact commit. For example:
   `dotnet test EduOS.sln --configuration Release` (use the repository's actual solution filename if different).
3. Run the JavaScript regression tests:
   `node --test EduOS.Tests/JavaScript/academic-section-contract.test.cjs EduOS.Tests/JavaScript/academic-section-integrations.test.cjs EduOS.Tests/JavaScript/academic-batches-sections-ux.test.cjs`.
4. Apply EF SQL Server migrations in staging first. Inspect generated SQL/EF migration history, duplicate indexes and index locks. Rehearse against a restored production-size database.
5. Run `scripts/migrations/validate-academic-section-scope.sql` in staging and production read-only. Investigate any missing/mismatched section IDs. Never automatically infer a Section from Batch. The final migration aborts if referenced generated placeholders remain; reconcile historical data with a reviewed plan, not a blind delete.
6. Release the server application and frontend together; clear/version static asset caches. Inspect `/AcademicManagement/Batches`, `/AcademicManagement/Sections`, Section CRUD and permissions. Check concurrent client versions against the additive API. Confirm `/AcademicManagement/Batches` and `/AcademicManagement/Sections` are distinct, neither UI displays a “Legacy” label, the batch register links to its own sections, and admission roles can read tenant-scoped section choices but cannot mutate section settings.
7. Repeat the exact-commit SQL Server integration, permission and UI/browser smoke suites before general enablement.

## Minimum acceptance and negative tests

| Scenario | Required outcome |
| --- | --- |
| Batch and Section creation | Separate table IDs; Section validates active parent Batch, tenant and normalized unique code |
| Batch capacity correction | Lower only if the new capacity covers active enrollments and every independent section capacity |
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
| Existing academic history | Backfilled Section identity, no altered historic Batch IDs, counts and reports unchanged |
| Forms | No duplicate fields, clear empty/error/success states, mobile and bilingual checks |
| Performance | Server paging, tenant/index filtering and no unbounded lookups |
| Operations | Migration/rollback rehearsed; latest commit's workflow runs and integration results recorded |

## Rollback safety

The foundation migration is additive; the final marker removal is intentionally destructive **only to the obsolete column**, and aborts if referenced generated sections remain. **Never run Down or delete `AcademicSections` after users have entered data**; that would destroy new Sections and references. For a post-write issue, disable the new UI/API, roll back application code only after checking its compatibility with the retained schema, and reconcile data before resuming. No automatic destructive rollback is authorized.

## QA evidence

Source-contract checks and JavaScript syntax checks are useful preliminary signals, **not** proof of successful .NET compilation, SQL migration, browser acceptance, tenant-level integration, or production readiness. No deployment is authorized without those recorded results.
