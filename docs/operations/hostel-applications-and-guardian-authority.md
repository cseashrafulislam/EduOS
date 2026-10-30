# Hostel application and non-binding offer lifecycle (V2)

Scope: EduOS `Ashraful` branch, canonical Hostel module, shared SQL Server `EduOSDbContext`. This is an engineering contract and test record, **not** a production acceptance certificate.

## Ownership and state

- `HostelApplication`: tenant/campus/enrollment/hostel-scoped request with immutable submission time, student/hostel display snapshots, client idempotency key, application status and optional offer/linked allocation. This does **not** reserve a bed, invoice a student, or modify Student/Finance records.
- `HostelApplicationEvent`: append-only submission and decision history. `HostelApplicationAuthority`: separately verified, dated guardian permission backed by an existing `StudentGuardian` relationship and active `Guardian.UserId`. Pickup, emergency and financial-contact attributes **do not** authorize hostel applications.
- Canonical physical booking remains `StudentHostelAllocation`, created only by `HostelService.AllocateValidatedAsync` and the existing `AllocateAsync` path. No new allocation table or second inventory writer.

| From | Action and actor | To | Guard |
| --- | --- | --- | --- |
| New | Submit (linked student, authorized guardian, scoped manager) | Submitted | Active current enrollment, matching campus, active hostel, valid period, no overlapping open request/stay, unique tenant/client key |
| Submitted | Waitlist (scoped warden/manager) | Waitlisted | Rowversion, tenant/campus |
| Submitted/Waitlisted | Offer (scoped warden/manager) | Offered | Matching active bed, earliest waitlist precedence, UTC expiry 1–168 hours |
| Submitted/Waitlisted/Offered | Reject (scoped warden/manager) | Rejected | Mandatory reason, rowversion |
| Submitted/Waitlisted/Offered | Withdraw (authorized applicant/delegate) | Withdrawn | Rowversion, active authority |
| Offered | Accept (authorized student or explicitly delegated guardian only) | Accepted | Database UTC expiry, rowversion, all canonical eligibility/overlap/peak-capacity checks, same SERIALIZABLE transaction for allocation + application + history |
| Offered and expired | Any write or accept | Expired | Enforced on request; no periodic job required, no bed committed |

`Offered` is explicitly **non-binding**: a direct allocation by a different authorized manager can take the bed first. Acceptance rechecks live capacity and may return HTTP 409. A future capacity-hold policy requires changing **all** canonical allocation and availability writers; do not add a private offer hold.

The waitlist view is tenant/campus-filtered, paged and ordered by `SubmittedAtUtc, Id` (immutable). Earlier waiting applications for the same hostel cannot be skipped when issuing another offer. Application snapshots remain readable even if the current hostel or student name changes.

## Authorization

- All routes require authentication, the `HOSTEL` module entitlement and standard CSRF/rate-limit middleware. Tenant comes from the authenticated context, not submitted DTOs. Campus restriction is checked in the service on every manager read/mutation.
- Students use `Student.UserId`; guardians require a non-revoked, unexpired, recorded `HostelApplicationAuthority` for an existing tenant-owned guardian relationship and verified `Guardian.UserId`. Principal/TenantAdmin can grant/revoke with evidence/expiry; wardens cannot grant guardian authority.
- Applicant and guardian readbacks only disclose their authorized records; administrative queue/export only discloses campus-authorized records. Every direct application ID and CSV route revalidates ownership.

## API and UI

- `GET /api/hostel/applications/options`, `POST /api/hostel/applications`, `GET /api/hostel/applications/my`.
- `GET /api/hostel/applications/queue` (bounded filters), `GET /api/hostel/applications/queue/export` (current 100-row page).
- `GET /api/hostel/applications/{id}/events`; `POST /api/hostel/applications/{id}/{waitlist|offer|reject|withdraw|accept}`.
- `GET /api/hostel/applications/authority/guardians`, `GET|POST /api/hostel/applications/authority`, `POST /api/hostel/applications/authority/{id}/revoke` (Principal/TenantAdmin).
- Hostel Operations workspace shows applicant form/history, compact manager queue/actions, and verified guardian-grant/revocation panel. Existing room/bed/occupancy UI and direct allocation API are retained.

## Migration, deployment and rollback

1. Back up/restored-data rehearsal, inspect latest `Ashraful` migration history. Apply **additive** `HostelApplicationsAndOffers`, then `HostelGuardianApplicationAuthority`; neither backfills historical allocations or overwrites Finance rows.
2. Keep the new UI/route behind the existing `HOSTEL` entitlement. After deployment verify all migrations, compile/CI, SQL Server transaction tests, IDOR/tenant/campus checks, date/expiry races and mobile/bilingual browser workflows.
3. Before production activation, run representative concurrency on two SQL DbContexts (offer vs direct allocation; competing accepts), p95/p99 queue/report load, campus UAT, backup/restore and rollback rehearsal. A green compiler alone is insufficient.
4. Roll back application code by disabling the new flow while preserving its tables. **Do not drop the new tables** if they already contain applications, decisions or guardian authority; destructive migration `Down` is suitable only for disposable test databases or proven-empty fresh deployment.

## Release boundaries

No invoice, refund, rent/deposit posting, occupied-stay transfer, incident or physical check-in was silently invented here. Existing Finance source-of-truth and occupied-transfer Issue #45 remain separately owned; their financially sensitive behavior requires the approved Finance integration contract and independent tests before Hostel can be declared 100% development-complete. Remaining browser/SQL concurrency/live-institute acceptance must also be signed off.
