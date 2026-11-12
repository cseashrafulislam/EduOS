# EduOS Smart Institution Setup — canonical implementation and operations

## Scope and ownership

The setup hub at `/SmartSetup/Index` is a tenant-admin-only preview, confirmation
and **central initial-data workspace**. Individual Settings screens remain available
for subsequent Add/Edit and permitted Delete/Archive operations.
Existing institution-type/module entitlements and the domain-specific Academic, Finance,
HR, Payroll, Library, Facilities, Hostel, Transport, LMS and Student workflows remain the
source of truth. No shadow/editable master or parallel accounting/stock table is introduced.

The backend exposes:
- `GET /api/smart-setup/preview`: tenant-scoped safe template entries
- `GET /api/smart-setup/readiness?module=ACADEMIC`: scoped checklist; no writes
- `GET /api/smart-setup/readiness`: all available module checklists on demand
- `POST /api/smart-setup/apply`: tenant-admin-only, anti-forgery-protected confirmed safe apply

### Canonical module entitlement mapping

Setup section FEES is licensed by ProductModule FINANCE; ASSESSMENT by EXAM;
REPORTS by REPORTING. Others use their section code. All writes are entitlement
checked again **inside** the transaction; modules excluded by the pilot stay denied.

### Apply behavior

1. TenantAdmin reviews and explicitly selects enabled safe defaults.
2. Server requires nonempty request GUID, template version, institution type and confirmation.
3. SQL Server serializes per-tenant setup with a transaction-owned advisory lock
   and Serializable isolation. No MAX()+1 or new document numbering.
4. Existing and soft-deleted tenant master codes/names are checked before insert:
   exact active match is reused, name/code conflicts prevent insertion. No
   historical record is silently overwritten or resurrected.
5. Inserts use canonical tables only. One audit receipt with a request fingerprint
   persists in the same transaction, including for a no-op apply.
6. A retry with identical request ID/payload returns the original receipt.
   A reused ID with a different module set/type/version fails with HTTP 409.
7. All writes and audit receipt commit together or roll back.

### Two explicit save modes

1. **Reviewed basic defaults** (one atomic operation): `Select all missing defaults`
   or tick individual, pre-filled read-only Code/Name/Detail rows, confirm, and
   `Save Checked Records`. The server accepts at most 7 approved modules and
   200 unique catalog keys per request. Exact row keys are re-resolved against
   `SmartSetupCatalog.Version = 2026.11.2`; browser-supplied names, amounts
   and financial classifications are never persisted. The result displays
   a per-kind saved count. Existing code/name conflicts are shown for manual
   review, never overwritten.
2. **Institution-specific forms** (each form saves under its *own* domain rules):
   Under an incomplete readiness prerequisite, `Add here` opens the EXISTING
   authorized Settings route in a same-origin frame inside Smart Setup.
   The original route, antiforgery token, validation, permission and save
   API are unchanged. The separate `Full Settings` link still works.
   The frame hides only the duplicated navigation chrome. Refresh setup
   status after saving; no generic cross-module POST, fake rows or parallel
   master/finance API is allowed.

Examples of original verified forms launched inside Smart Setup:
- Academic Year and actual dates: `/AcademicManagement/Years`.
- Hostel facility, room capacity and physical beds: `/HostelOperations/Setup`.
- Finance chart of accounts, accounting periods and bank accounts:
  `/AccountingSetup/Index`. Bank accounts must reference an authorized asset GL.
- Fee structure and monetary amounts: `/FeeOperations/Index`.
- Verified accounting opening entries: `/AccountingJournals/Index`.
  Creating a balanced draft does not post a balance. Normal approval and
  posting must happen before financial reports recognize the entry.
- Other modules reuse the `SmartSetupReadinessService` `ActionUrl` for their
  canonical setup forms. Tenant/module access is checked by those original
  controllers, not granted by the iframe or URL parameter.

**Atomicity boundary:** One checked-default apply is atomic. Independent
institution-specific saves across domains are *not* falsely represented as
one all-or-nothing financial/academic transaction. Each action is validated
and committed by its existing owner. No real dates, prices, people, bank
details, balances, facilities or inventory are auto-invented.

### Safe seed groups

ACADEMIC: optional language Medium labels; HR: organization units and
designation labels; FEES: fee-head labels (no values, revenue accounts or invoices);
INVENTORY: asset category labels; LIBRARY: book category labels;
COMMUNICATION: notice categories; DOCUMENTS: basic document type labels.

The reviewed v2026.11.2 catalog supplies **12 initial choices per safe kind**:
HR OrganizationUnit (12) and Designation (12); FeeHead (12);
AssetCategory (12); BookCategory (12); NoticeCategory (12);
DocumentType (12). Academic medium intentionally has only 1–2 correct
choices based on institution and country, not 12 fabricated options.
Total maximum currently reviewed basic defaults per eligible tenant:
86 records (2 mediums in BD school/college; 85 for other types), fewer
when modules are disabled or records already exist. The user chooses
which optional heads/categories apply. FeeHead frequency comes from
the server-reviewed catalog; amounts remain unset.

No real students, parents, employees, actual course content, class dates,
curriculum allocations, grading thresholds, tax calculations, salary structures,
bank accounts, ledger postings, fee rates, physical stock, dormitory inventory,
routes or vehicles are invented. Such operational facts are linked to the
existing authoritative setup pages.

### Module readiness / contextual navigation

The setup readiness endpoint checks actual tenant-owned master records, using
SQL EXISTS queries and module entitlement checks. It is a *prerequisite checklist*,
not a certification of finance posting or academic compliance. The shared layout
loads a small nonblocking banner for tenant administrators on recognized pages
only when prerequisite masters are missing. The setup opens in a new tab so
unsaved form values are not lost; navigating a menu **never writes data**.
Readiness failures must not block normal application forms.

### QA / rollout

- Run `node --test EduOS.Tests/JavaScript/smart-setup-guardrails.test.cjs EduOS.Tests/JavaScript/smart-setup-grid.test.cjs` and .NET `SmartSetupServiceTests` including 86-entry apply.
- Validate module mapping, tenant isolation, disabled and pilot-excluded modules,
  stale preview, replay ID, conflicting codes and rollback behavior.
- Run SQL Server Migration Smoke on the existing `Ashraful` branch.
- No migration or backfill is necessary: only existing domain and audit tables are used.
- No automatic production master-data seeding occurs at deployment. Tenant Admin
  must explicitly apply presets; active tenants may retain and edit their own data.

For auditability: opening a page or invoking readiness does not produce any
new domain or financial records. The application still validates every actual
business transaction in its owning service.
