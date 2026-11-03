# Facilities Visitor Privacy: controlled retention and redaction

**Scope:** existing `Ashraful` visitor workflow. This procedure is opt-in, privacy-sensitive and **not** automatic cleanup. Do not enable it for an institute before its retention/legal policy has been approved and staging SQL/browser evidence is accepted.

## Current data ownership and controls

- `FacilityVisitorVisits` is the current appointment source of truth. It contains only minimal visitor name, purpose and optional badge; no national ID, phone, photo or uploaded document.
- `FacilityVisitorEvents` retains the source-linked action, actor, time, state transition and command ID. Badge and free-text reasons are **private event payload**, not immutable proof that must be retained forever.
- General `AuditLogs` retains actor, entity, action and time but suppresses old/new payload for both visitor entities. This prevents **new** visitor identities and incident narratives from being copied into generic audit history. It does **not** retroactively clean historical audit rows, backups, downstream exports or archived files.
- Read access uses active tenant/campus/role/membership validation; private API responses require the original cookie-session reference. Exports are bounded, PII-free and access audited.

## Campus policy keys: defaults deny redaction

The existing canonical `TenantSettings` table is the effective policy source; no second editable copy or shadow visitor ledger.

| Key | Value | Behavior |
| --- | --- | --- |
| `Facilities.Visitor.RetentionDays.{CampusId}` | integer **30–3650** | **Required** for redaction; missing, sensitive, invalid or out-of-range values block the operation |
| `Facilities.Visitor.LegalHold.{CampusId}` | `true` or `false` | `true` blocks redaction; malformed or sensitive values also block; absent means no *campus-wide* hold is configured |
| `Facilities.Visitor.MaxConcurrent.{CampusId}` | integer **1–5000** | Existing admission ceiling; not modified by privacy commands |
| `Facilities.Visitor.MaxVisitMinutes.{CampusId}` | existing bounded minutes | Existing appointment duration; not modified |
| `Facilities.Visitor.EarlyCheckInMinutes.{CampusId}` | existing bounded minutes | Existing early-entry policy; not modified |

### Independent campus privacy-policy changes

RetentionDays and LegalHold can no longer be created or updated from generic Tenant Settings. Use the Facilities Visitor page's policy request/review card or its API:

1. An active FacilitiesManager/Principal/TenantAdmin with a valid live campus grant submits a UUID-bound request containing campus, policy, proposed value and a 10–200 character institutional basis. The application snapshots the **existing** canonical setting value and SQL rowversion (or its absence). Only one submitted change per campus/policy is allowed.
2. A **different** currently authorized Principal/TenantAdmin selects Approve or Reject with a 10–200 character review reason and the exact request rowversion. Serializable locking, live membership/role/campus validation, approved policy-value bounds, uniqueness and optimistic rowversions prevent stale/self/duplicate approvals.
3. Only approval updates the canonical `TenantSettings` value; rejection preserves it. Both outcomes persist a versioned review row, reviewer identity/time and a PII-free access audit. Replays use the same command UUID and may not alter a previous outcome.

Only the two privacy-sensitive keys above use this new workflow; general Visitor occupancy, early-check-in and duration policy settings retain their existing management path. Existing campus retention/legal-hold values are left untouched during deployment. **Before enabling use**, an institution still must authorize the retention schedule and legal-hold decisions under its own legal policy.

## Manual two-person redaction

1. An authenticated active Facilities manager with campus access selects a terminal **Rejected** or **CheckedOut** visitor appointment and requests redaction. Only the exact fixed reason `Visitor retention period expired` is accepted. The appointment's `RowVersion`, a new command UUID and the source visit ID bind the request.
2. Under the same short SQL Server `SERIALIZABLE` transaction, the server locks/checks the current user's membership, role and campus grants, validates an explicit campus retention setting and elapsed end date, checks campus-wide legal hold and denies any visit with incident evidence. Active/approved/check-in visits are never eligible. A `redactrequest` event records maker and time; no PII is changed on request.
3. A **different** active Principal or TenantAdmin with current authorization and the latest exact rowversion approves using `redactapprove` and fixed reason `Independent retention approval`. A stale approval, self-approval, event supersession or changed policy returns 403/409.
4. The approved transaction replaces the visitor name and purpose with `[redacted]`, clears current badge, scrubs prior visitor-event **BadgeCode/Reason** private payloads and records a new source-linked approval event. Action code, actor, timestamp, state transition, check-in/out times, occupancy/report counts and original posted Finance/stock records remain intact.
5. The visitor UI exposes request and eligible reviewer buttons in both supported locales. Privacy status is derived from a bounded batch query over existing event history, not a redundant persisted editable flag. Retries reuse the same command UUID; the existing event uniqueness and optimistic rowversion protect against duplicate approval.
6. No background purge, automatic checkout, hard delete, cross-tenant redaction, altered ledger, or unapproved retroactive repair is performed.

## Non-negotiable release blockers and operational limits

- **Historical generic audit payloads:** older records created before the audit-suppression fix may already contain visitor PII. Privately inventory affected `AuditLogs` and backups and obtain security/legal sign-off for a controlled remediation plan. Do not silently truncate the audit table or run unreviewed destructive SQL.
- **Incident/legal holds:** any visit with an incident remains ineligible. Campus hold is a conservative barrier, not a full per-case retention/hold-resolution register. The business must establish a separate authorized legal-hold release and case closure procedure before redacting incident visits.
- **Independent site-policy change approval:** the application now has a versioned, independently reviewed workflow for the two privacy-sensitive campus keys; full legal-policy sign-off and SQL/browser acceptance remain separately required. General visitor operational policy keys have not been migrated into this approval system.
- **Verification:** run final-SHA Release CI, `SqlServerFacilityVisitorTests`, full disposable SQL migration/rollback/backup-restore, source-privacy JavaScript contracts, and cookie-authenticated SQL-backed multi-tenant/campus Chromium (desktop/mobile, both locales). Review denied policy, rejected vs active, supersession, two simultaneous approvals, role revocation, replay, report totals and old audit-history privacy. Tests require disposable SQL, never production tenant data.
- **Deployment:** record effective retention policies, existing visit/event counts, legal holds and prior backups; release behind the existing FACILITIES entitlement. Roll back application code if needed **without** restoring already approved PII or rewriting action history. Distinguish code QA from real institutional/legal UAT and owner go/no-go.

This implementation does **not** by itself close Facilities #107 or whole-system #108.
