# Bangladesh Government Holiday Import — EduOS Academic Calendar

## Purpose and ownership

**Canonical owner:** Existing AcademicCalendarEvent and AcademicCalendarPolicy.
No alternate holiday tables, background job or separate posting/attendance system.

Academic Calendar now offers `Preview official holidays` and `Save selected holidays`
for BD institutions. **Nothing is written on preview or navigation.** Academic
administrators must explicitly select individual missing dates and confirm.
Only approved sources are embedded in a **versioned catalog** for 2026 and 2027;
no runtime scraping, third-party API or unauthenticated live updates.

## Sources, versions and limitations

- 2026: Bangladesh Ministry of Public Administration's **9 Nov 2025**
  notice (archived at https://mopa.gov.bd/pages/public-holiday?archived=true).
- 2026 amendment: Ministry notice dated **7 Oct 2026** declaring
  7 November a recurring public holiday, available at
  https://mopa.gov.bd/pages/go-ultimates/6ac5e76d9594596dad446b72.
- 2027: Ministry notice dated **5 Nov 2026**, see
  https://mopa.gov.bd/pages/public-holiday.
- Catalog version: `BD-MOPA-2026-2027-v2`; only full entries inside the
  selected academic year/date range are offered.
- Source snapshots are not a dynamically maintained holiday feed. Later
  notifications and moon-sighting decisions may change individual dates.
  Review the newest notice before saving, and use the existing authorized
  calendar edit workflow for corrections.
- **Region-limited leave** (e.g. Chaitra Sankranti in specified hill
  districts), **religious optional leave**, and school-specific vacations
  are excluded from automatic institution-wide selection. Authorized
  institutions may enter appropriate local closure in the original Calendar
  Editor. Calendar weekends remain configured only in Weekend Policy.
- The source is an **office/public administration closure calendar**, not
  the annual Education Ministry/NCTB/BTEB institutional academic calendar.
  This importer never claims to define school vacations, exam schedules,
  university senate decisions or campus-specific opening days.

## Workflow

1. Manager selects a real Academic Year, Campus (or institution-wide if
   TenantAdmin) and date range in `/AcademicCalendar/Index`.
2. Preview: `GET /api/academic-calendars/government-holidays/preview`.
   Tenant must have `CountryCode = BD`. Response contains source version,
   public URL, moon-dependent note, and row states `Missing`, `Existing`,
   `Conflict`. Campus grants and current academic year dates are revalidated.
3. User selects `Missing` rows, confirms the review checkbox, and saves.
4. Import: `POST /api/academic-calendars/government-holidays/import` with
   `ClientRequestId`, scope, catalog version, confirmation, and **source
   codes only**, not user-provided names or calendar dates.
5. Service uses a serializable transaction and SQL Server transaction-owned
   `sp_getapplock` keyed by tenant to prevent duplicate concurrent imports,
   with existing calendar natural-key unique index as additional protection.
   Exact existing dates are reused, conflicts are rejected, user-defined
   event records are never changed. Inserts and audit replay receipts commit
   or roll back together.
6. Imported entries are public `CalendarEventKind.Holiday` rows with
   `AcademicTermId = null`, current tenant/year/campus scope and source
   metadata in `Description`. Existing working-day and transport/calendar
   consumption continues unchanged.
7. Identical retries return the first audit receipt; reusing the same GUID
   with different scope, source version or selection returns HTTP 409.

## QA and rollout

Run `node --test EduOS.Tests/JavaScript/academic-calendar-ui-contract.test.cjs
EduOS.Tests/JavaScript/bd-government-holiday-calendar.test.cjs` and the .NET
`AcademicCalendarServiceTests`. Before deployment, run the complete .NET
build and SQL Server integration/tenant-permission/concurrency tests, verify
date-only serialization in both English/Bangla, review ministry updates, and
back up the production database. These are implementation requirements,
**not evidence that a live production deployment or CI has passed**.

No database migration is necessary because the existing
`AcademicCalendarEvents` natural-key index and `AuditLogs` are reused.
