# EduOS Smart Institution Setup — canonical implementation and operations

## Scope and ownership

The setup hub at `/SmartSetup/Index` is a tenant-admin-only preview / confirmation workspace.
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

### Safe seed groups

ACADEMIC: optional language Medium labels; HR: organization units and
designation labels; FEES: fee-head labels (no values, revenue accounts or invoices);
INVENTORY: asset category labels; LIBRARY: book category labels;
COMMUNICATION: notice categories; DOCUMENTS: basic document type labels.

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

- Run `node --test EduOS.Tests/JavaScript/smart-setup-guardrails.test.cjs` and .NET tests.
- Validate module mapping, tenant isolation, disabled and pilot-excluded modules,
  stale preview, replay ID, conflicting codes and rollback behavior.
- Run SQL Server Migration Smoke on the existing `Ashraful` branch.
- No migration or backfill is necessary: only existing domain and audit tables are used.
- No automatic production master-data seeding occurs at deployment. Tenant Admin
  must explicitly apply presets; active tenants may retain and edit their own data.

For auditability: opening a page or invoking readiness does not produce any
new domain or financial records. The application still validates every actual
business transaction in its owning service.
