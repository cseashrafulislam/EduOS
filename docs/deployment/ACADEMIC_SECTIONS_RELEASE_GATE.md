# EduOS Academic Batches vs Academic Sections — clean model and rollout

Branch: `Ashraful`. Required latest migration: `20261113050000_DecoupleSectionsFromBatches`.

## Canonical ownership

- `AcademicLevels` is the Class/Grade master, e.g. Class One.
- `AcademicSections` is a permanent group under **Tenant + Campus + Class/Grade**.
  Example: Class One → A, B, C. **No Academic Batch is required to create a Section.**
- `AcademicBatches` is a separately managed admission cohort/intake for workflows that
  use it, e.g. CSE intake 2026 or HSC 2026–2028.
- Never store `AcademicBatchId` in `AcademicSections`; class/campus is its single
  authoritative scope.
- `AcademicSectionId` in Enrollment, Offering, Attendance, Result and Fee records
  denotes the class group. Existing cohort-based transactions also store their
  `AcademicBatchId` for intake history, **not** to define a Section.
- `IsLegacyDefault` is not part of the current entity, schema or API.

## Database migration and empty development database

The older migration files have already been committed to the branch. They must remain
versioned to prevent a migration-history mismatch on any database that has run them.
The final migration adds CampusId/AcademicLevelId, verifies the scoped data, drops
Section.AcademicBatchId and adds unique index `TenantId, CampusId, AcademicLevelId, Code`
for non-deleted sections. It blocks ambiguous duplicate codes and unresolvable class
ownership rather than guessing.

**The database has no previous business data per current requirement.** That simplifies
reconciliation, but do not infer whether EF migration history is empty. If resetting
the *development* database is explicitly intended, back up any useful configuration,
users, subscriptions or auth data before dropping it; otherwise apply outstanding
migrations in order. Never reset a production tenant database automatically.

## UI and API

- `/AcademicManagement/Batches`: admission cohort maintenance.
- `/AcademicManagement/Sections`: **Campus → Class/Grade → Section name/code,
  capacity, Active, display order**; independent CRUD.
- GET `/api/academic-sections?campusId=...&academicLevelId=...` lists scoped sections.
- GET with `academicBatchId` is a compatibility search shortcut: it resolves the
  batch's Campus/Class, but does not imply section ownership.
- Student Add displays Batch (current enrollment cohort context) and a separate
  class Section selector and posts both identifiers.

## Important remaining scope: batch-optional enrollment

The *Section master is independent*, but the older Student Enrollment, Admission,
Attendance, Routine, Fee and Exam APIs still use a nonnullable cohort/batch context.
Making the **entire institution** work with no Academic Batch requires a separate
integrity-first change to StudentEnrollment/SubjectOffering and downstream endpoints.
Do **not** solve this by inventing a batch automatically or by renaming Batch to Section.
Until that change is completed, use an actual cohort where the current enrollment
workflow requires it; Section CRUD itself never requires one.

## Verification gate

1. `dotnet restore EduOS.slnx`
2. `dotnet build EduOS.slnx -c Release`
3. `dotnet test EduOS.Tests/EduOS.Tests.csproj -c Release`
4. `node --test EduOS.Tests/JavaScript/academic-section-contract.test.cjs
   EduOS.Tests/JavaScript/academic-section-integrations.test.cjs
   EduOS.Tests/JavaScript/academic-batches-sections-ux.test.cjs`
5. Check migration parity with `dotnet ef migrations has-pending-model-changes
   --project EduOS.Persistence --startup-project EduOS.App --context EduOSDbContext`.
6. Run `scripts/migrations/validate-academic-section-scope.sql` read-only in staging.
7. Browser checks: campus/class dropdown, Class One Sections A/B/C without batch,
   duplicate-code conflict, create/edit/delete guard, mobile, Bengali, cross-tenant
   permission, student/attendance Section selection, concurrency, stale forms.
8. Do not claim production QA until all checks are recorded for the exact commit.

The assistant-side source/JS checks are **not** evidence of successful .NET build,
SQL migration, or real-browser testing.
