# V3 Track 4 — Facilities service request and work-order workflow

Owner: Facilities & Visitors; canonical branch: Ashraful. Scope: independent campus maintenance, **not** Academic Room, Hostel, AssetMaintenance, Inventory physical count, or a duplicate stock/GL ledger.

## Implemented slice

- Campus-scoped FacilitySpace registry and default-off FACILITIES entitlement already existed.
- FacilitiesRequester/FacilitiesTechnician/FacilitiesManager/FacilitiesApprover/Principal/TenantAdmin can submit an idempotent request against an active, authorized facility space.
- Manager triages with an explicitly entered UTC SLA deadline (no invented default SLA); an independent approver approves or rejects. Approval creates exactly one work order, enforced by a unique (TenantId, FacilityServiceRequestId) index.
- Manager schedules an active, tenant-member, campus-granted technician; assigned technician starts, pauses with reason, resumes and submits completion evidence; independent approver verifies closure. Every accepted command appends a tenant-owned transition event and uses rowversion/command ID checks.
- Authorized bounded campus list, paginated event history, bounded CSV with spreadsheet formula neutralization, responsive bilingual Razor/JS UI. No Inventory or Finance posting, no pretend spare-part button.
- SLA classification uses the captured due timestamp and actual completion time; overdue/open state is not conflated with verified closed breach.

## Remaining before Issue #107 closure

- Secure vendor assignment and reschedule/cancel conflict history, optional authorized Asset linkage, explicit correction/reopen policy.
- Track 5 review of the existing InventoryMovement source type and exactly-once approved spare-part issue; transaction rollback/stock contention and canonical cost read-through. No shadow stock, no Finance writes without approved V1 contract.
- Tenant-configurable SLA policy source and escalation, richer operational dashboard and validated reports.
- Real SQL parallel approval/duplicate work-order/IDOR test, fresh/repeat migration, exact-SHA CI and browser tests; restored-data rehearsal, performance, institutional UAT, owner go/no-go.
- Visitor preregistration, host approval, privacy-minimizing check-in/out, incident/retention/export workflow.

Never mark the entire Facilities module production-ready from a passing CI run alone.

## Request ownership and SLA dashboard delta (2026-10-31)

- Requesters see only their own requests and history; assigned technicians see their own requests and assigned work orders. Managers and independent approvers see authorized-campus requests. SQL scopes list, event history, dashboard counts and bounded CSV export before pagination or aggregation.
- Responsive bilingual operational cards report persisted triage, approvals, scheduled/active work, overdue-open and closed-late, without estimating or posting stock/Finance costs.
- Dedicated SQL integration regression checks same-campus cross-request IDOR and requester/technician/manager dashboard visibility. This slice needs no EF migration and does not alter V1/V2 or Inventory source contracts.

## Technician scheduling conflict control

- Scheduling/rescheduling an approved work order validates overlapping UTC windows against active Scheduled/InProgress/Paused work for the same tenant technician, excluding the current order. SQL Server obtains a tenant+technician transaction-owned application lock before conflict query and retains it through the work-order update and append-only audit; duplicate concurrent scheduling cannot silently double-book. Busy/overlap returns HTTP 409 and preserves the existing schedule. No schema or Inventory changes.
- Dedicated disposable SQL acceptance includes an overlapping second approved work order and verifies it stays unassigned. Real parallel-worker contention and browser/UAT remain separate acceptance gates.

## Schedule release / reschedule correction (2026-10-31)

- A campus-authorized manager can release an **unstarted Scheduled** work order to Approved with a required 5–500-character reason. The technician and UTC window are cleared atomically with an append-only event capturing the previous assignment and time window; no completed/in-progress/paused order can be silently cancelled.
- Release uses the existing rowversion and idempotent command contract; the technician cannot release their own assignment. Manager may then reschedule with overlap locking and a history snapshot of old/new assignment. UI exposes the action in bn-BD/en-BD, preserving CSRF and request ownership checks.
- Focused disposable SQL regression covers denied technician release, manager release, idempotent replay, stale-version 409, audit and reschedule. Inventory/Finance remain untouched. Real parallel-worker and browser acceptance are separately pending.
