# EduOS V2 Transport — final code QA handoff (2026-10-31)

**Branch:** `Ashraful`. **Scope:** existing Transport operations + existing Hostel/Transport Finance handoff. **Ownership:** V2 only; V1, LMS/Library frozen baselines and independently running V3 automation are unchanged.

## What the code already supports

- Campus-scoped route/stop and vehicle setup, driver/license and route-vehicle/driver scheduling; effective-dated changes and capacity forecasts.
- Student reservation, pickup/drop, stop changes, upcoming changes/cancellation, active closure, operating vehicle transfer, persistent predecessor/successor reservation history, idempotent retries and detailed exports.
- Trip scheduling, dispatch, boarding/no-show/attendance and family/student readback with tenant/role checks; fleet safety cases, manifest and operational reporting.
- Campus-selected billing policy **DailyProrated (automatic)**, **FixedMonthly (automatic)**, **Manual**, and independently reasoned manual override when enabled. An estimate is not a cash payout. Finance clearance is independently reviewed and used once; accounting remains in V1 Finance.

Existing code is authoritative: do not create a second Transport subsystem, duplicate fares/financial ledgers or restart LMS/Library. Existing migrations `20261030193510_HostelFinanceClearancePolicy` and `20261030195303_TransportFinanceClearancePolicyAndLineage` remain additive and unchanged.

## Final QA hardening in this change

1. Transport active closure and operating vehicle transfer now map SQL Server deadlock, lock timeout, snapshot-conflict and duplicate-key races to HTTP **409**; unhandled infrastructure failures still surface as genuine errors, and cancellation remains cancellable. A retry cannot falsely claim a legacy, unlinked reservation as the intended transfer successor.
2. Finance clearance creation, review and billing-policy update also return **409** for recognised SQL Server concurrency/uniqueness conflicts. Maker/checker, rowversion, tenant/campus, journal, finance-snapshot and single-use checks remain server-owned.
3. Three disposable-SQL acceptance cases now exercise: request idempotency and replay mismatch; denial of self-approval and tenant IDOR; independently approved and consumed vehicle transfer with immutable predecessor/successor and no fabricated journal; competing transfer requests against the same Finance clearance; and charged-closure rejection of an unposted journal with separately attested zero-adjustment completion.
4. A separate `SQL Server Migration Smoke` CI step runs those cases on the **same new commit** after applying the full EF migration chain, with `sql-v2-transport-finance-acceptance.trx` evidence. Routine full-solution .NET, JavaScript and existing SQL smoke gates remain enabled.

## Engineering gate — collect evidence on the final commit

| Gate | Required result | Evidence/owner |
| --- | --- | --- |
| Release solution build and full .NET unit/contract suite | 0 errors, all green | `.github/workflows/ci.yml` same commit |
| Full JavaScript regression | all green | `node --test EduOS.Tests/JavaScript/*.test.cjs` |
| EF migration/snapshot check | no pending drift, migration script valid | CI EF checks |
| Disposable SQL migrations + new Transport Finance race tests | all green, including unique lineage and tenant rejection | `.github/workflows/sqlserver-migration.yml`, `sql-v2-transport-finance-acceptance.trx` |
| Existing Transport/Hostel, LMS/Library and V1 Finance regression | all green | same-commit CI + existing SQL tests |

**These checks must not be marked passed merely because they are defined.** A GitHub push or locally green .NET/JavaScript suite does not constitute a SQL Server or production UAT pass.

## Release acceptance still required from an institution

- Rehearse migration on a **restored, anonymized production-like SQL Server backup**, including lock duration/index build, data integrity, mixed-version deployment and tested restore/forward-fix route. Never casually execute destructive `Down` on consumed Finance clearances.
- Finance independently reconcile actual posted/unposted, reversed and manually approved journals; verify student/campus and amount against immutable invoice/payment evidence, then sign off transfer and closure cases, refunds and corrections.
- Test actual wardens/transport managers, drivers, guardians and Finance checker accounts on browsers/mobile in Bengali and English; include ID enumeration, cross-tenant and cross-campus isolation, accidental double-submit and offline PWA security.
- Run representative peak-volume traffic, seat-capacity race, SQL waits/IO, report pagination and p95/p99 checks; security review, backup/restore, monitoring and a rollback/incident rehearsal.
- Record named institutional approvers, dates and an explicit go/no-go before calling **V2 100% production accepted**.

**Release label:** `V2 Transport — implementation consolidated, final code gate pending exact-commit CI; production acceptance open`. Once the three SQL cases and complete same-commit CI pass, update the engineering-gate evidence without reopening frozen LMS/Library.
