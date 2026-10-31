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
