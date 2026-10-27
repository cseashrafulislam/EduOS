# Academic foundation reference-integrity contract

Owner: Platform / Institution Foundation. Branch: `Ashraful`.

## Canonical aggregates and service API

Keep the existing `AcademicYear`, `AcademicTerm`, `AcademicBatch`,
`AdmissionIntakeForm`, `StudentEnrollment`, `SubjectOffering` and
`AcademicCalendarEvent` entities, tables, EF mappings and public DTOs.
No replacement aggregates, database migration, new controller, or duplicate
idempotency receipts are introduced.

The existing `IInstitutionFoundationService` operations remain:

- `SaveAcademicYearAsync(long? id, SaveAcademicYearRequestDto request, CancellationToken)`
- `ArchiveAcademicYearAsync(long id, string rowVersion, CancellationToken)`
- `SaveAcademicTermAsync(long? id, SaveAcademicTermRequestDto request, CancellationToken)`
- `ArchiveAcademicTermAsync(long id, string rowVersion, CancellationToken)`

All commands remain TenantAdmin-only, tenant-scoped and RowVersion-checked.
An invalid mutation returns HTTP 409 through the existing authorized API and
Razor/JavaScript editor; the client must reload rather than retry with a
fabricated version.

## Referential policy for dependent workstreams

1. **Academic year archival** is blocked while non-deleted terms, batches,
   admission intake forms, student enrollments, subject offerings or calendar
   events reference the year. The same direct operational references block
   reassignment of a used academic year to a different campus; existing
   terms/batches/intakes also block campus reassignment.
2. **Academic term archival** is blocked while non-deleted batches, admission
   intakes, student enrollments, subject offerings or calendar events reference
   the term. Reparenting a term to another academic year is blocked while any
   of these references exist.
3. Editing a year cannot exclude existing term/batch dates, calendar event
   dates or recorded enrollment dates. Editing a term cannot exclude its
   existing batch dates or calendar event dates.
4. A command does not rewrite, delete or migrate another module's records.
   Academic/Student/Admissions owners must provide a separately authorized
   historical migration workflow if an institution truly needs to move
   already-used years or terms.
5. The dependency checks and mutations run inside short **SQL Server
   SERIALIZABLE** transactions. These checks protect application commands;
   other writers must honor the same aggregate lock/transaction policy.
   Database FK and index contracts remain unchanged.

## Regression contract

`EduOS.Tests/Services/InstitutionFoundationReferenceIntegrityTests.cs`
covers direct year/term dependencies, cross-campus/year reparenting,
date-range shrinkage, and valid edits of unreferenced aggregates.

CI must run the .NET suite against the exact pushed SHA. SQL Server migration
smoke is still required for changes to mappings or migrations; this batch
does not change either. Do not declare browser end-to-end behavior verified
without a browser test.
