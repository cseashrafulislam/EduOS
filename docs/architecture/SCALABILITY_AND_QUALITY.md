# EduOS — Scalability, Architecture Decisions and Engineering Quality Bar

**Decision baseline:** 2026-10-27 | **Status:** product engineering standard, not a deployment-capacity certification.

## 1. Current architecture decision

- Retain the **existing single SQL Server database** and **existing `EduOSDbContext`** for all institutions now. Continue modular-monolith separation using the current solution structure and contracts.
- Keep the application publish/runtime logically separate from SQL Server deployment; production may use separate application and database hosts based on operational needs. Network latency, CPU, IO and connection limits must be measured rather than assumed.
- Every tenant-owned query, mutation, export, report, background operation, cache entry and file access is scoped and authorized. Identity-wide/platform information remains governed separately from institution records.
- **No multi-database routing, sharding, new registry, physical partitioning or migration is required now.** Future dedicated/shared databases are an optional capacity/tenant-isolation decision after metrics show a need. Moving an existing institute will require audited migration, validation, access freeze/cutover, rollback and source-of-truth control; it is not a simple connection-string edit.
- New database/model fields are allowed in future only for justified business behavior under a reviewed additive migration; this document itself authorizes **no** schema change.

## 2. What 'millions of learners' actually means

Scale is a **target and design constraint**, not a current performance claim. The important dimensions are distinct: total accounts, active institutes, students per institute, concurrent logged-in users, peak requests/sec, rows per tenant/year, file volume and report/export concurrency. Do not equate one million registered students with one million simultaneous requests.

Before claiming any capacity, publish representative anonymized/seeded load scenarios and measured throughput, latency percentiles (p50/p95/p99), SQL CPU/IO/locks/pool wait, app resource usage, error rate, recovery behavior and cost. Include peaks for login, attendance start/end, marks publishing, fee payment and report-generation windows. No invented performance percentage or hardware capacity claim.

### Practical scalability order

1. **Correctness first:** strict tenant/role/resource isolation, atomic important writes, idempotency and historical financial/academic invariants.
2. **Efficient SQL:** explicit projection, server filtering/paging, bounded export, appropriate tenant-leading indexes, query-plan review, avoid N+1 and unbounded table materialization.
3. **Stable request execution:** async I/O and cancellation; input limits; connection pooling; bounded timeouts; safe retry rules; no synchronous heavy report generation in interactive requests when measured load requires asynchronous processing.
4. **Fairness:** per-tenant/per-actor rate and concurrency controls for expensive queries and abuse-sensitive workflows so one institute does not starve others.
5. **Horizontal app scale when necessary:** stateless deployment, shared key protection, distributed session-safe auth and load balancer; compatibility across rolling updates.
6. **Add distributed cache/queues/object storage only when justified** by measured usage, operational reliability or actual integration throughput. Never cache authoritative finance/approval/stock state; always include tenant/security dimensions in cache keys and jobs.
7. **Database scale later:** tuned indexing, archiving/report isolation and only then evaluate shared/dedicated database choices from actual telemetry and operating cost.

## 3. Security and transactional integrity rules

- Request/user-provided `TenantId` and resource IDs are never trusted as authorization. Resolve authenticated membership, campus, feature entitlement, role and ownership server-side; fail closed.
- Cross-tenant access, including direct resource-ID guesses, exports, uploaded files, background tasks and logs, must be explicitly tested. UI hiding alone is not a control.
- Protect PII/government IDs, student/minor identity and guardian relationships with least privilege, consent and redacted logs. Use audited break-glass only with scoped authorization.
- Payments, journal vouchers, student fee posting, exam publication, attendance bulk writes, stock movement and approval transitions are short, atomic server transactions with retries/idempotency and optimistic or database-enforced concurrency as appropriate.
- Financially posted or published records use reversal/correction/amendment with audit rather than silent overwrite/delete. Historical rules and values are snapshotted only where needed for reproducible reports.
- Keep secrets out of source control and user preference documents. Backups must be encrypted, access-controlled and restore-tested.

## 4. Engineering quality — prevent 'garbage code' accumulation

For any new feature, bug fix or cleanup:

1. **Understand current code first:** inspect canonical entities, EF mappings, migrations/snapshot, actual registered services, controllers, views/JS, reports and consumers. Do not invent parallel DTOs, fake APIs or duplicate managers because searching is inconvenient.
2. **Trace the complete feature:** entity -> DTO -> application service/query -> permission-aware API -> responsive UI -> reporting/export -> test. A generated model, API stub or pretty page without working business behavior is not a completed feature.
3. **Preserve stable ownership and naming:** common education concepts stay generic for all institute types. Rename only proven duplicate/legacy naming through one dependency-complete reviewed change; preserve historical data/contracts or supply a safe migration.
4. **One source of truth:** avoid duplicate editable values, shadow state, unnecessary background workers, magic status values, speculative tables and high-layer coupling. Follow the existing architecture unless verified defects require change.
5. **Bounded queries and auditable mutations:** enforce tenant/campus access, anti-forgery/input validation, concurrency, idempotent retries, indexes and projection. No unbounded `GetAll().ToList()` for operational lists.
6. **Avoid fake completion:** no mock dashboard numbers, nonfunctional buttons, hidden failed requests, placeholder financial posting or TODO paths presented as production.
7. **Safe evolution:** no deletion of existing working modules/data simply because they are not in the current pilot. Under Construction must be role/entitlement gated; database changes require data/backfill/index/mixed-version/rollback review.
8. **Small coherent change sets:** complete all active consumers and tests of a change; avoid renaming layers repeatedly or creating adapters that cause permanent compatibility loops.
9. **Peer-review and evidence:** Release build, EF snapshot/migration consistency, unit/integration/authorization regressions and critical browser workflows must pass on exact commit. Document what was tested vs not tested.

## 5. Release and operations quality gates

| Gate | Minimum evidence before real institutional production |
| --- | --- |
| Product | Main user roles can finish agreed workflows end to end; no critical broken action or dishonest menu |
| Integrity | Double submission, races, duplicate numbers, payment reconciliation, result immutability and transaction rollback checked |
| Security | Cross-tenant/campus/ownership/permission tests; session/key/secret review; independent security review for sensitive data |
| Data | Migration rehearsal on anonymized/restored data; row counts, financial totals and rollback procedure verified |
| Performance | Realistic peak concurrency, data volume and slow-query test evidence with documented p95/p99, errors and resource headroom |
| Resilience | Backup/restore rehearsal, retry/idempotency verification, health/readiness, logs/alerts and incident recovery plan |
| UX | Bilingual/mobile/PWA, keyboard/touch help, accessibility and consistent valid form errors; no tenant-specific theme leak |
| Deployment | CI at exact SHA, staged release, controlled schema apply, monitoring and owner-approved go/no-go |

Pilot is **not** a production certification. See `docs/deployment/SCHOOL_PILOT_STAGING_RELEASE.md` for the current guarded staging boundary. Old roadmap/delivery reports are historical evidence tied to their named branches/commits, not proof of the latest HEAD.

## 6. Change approval and documentation discipline

- Maintain `README.md` as navigation/product catalogue, `docs/PRODUCT_BLUEPRINT.md` as scope and workflow acceptance, `docs/UI_PERSONALIZATION.md` as UX contract, this file as architecture/quality contract, and existing module-specific documents as detailed implementation evidence.
- Document each future architecture reversal as a short decision: observed problem, alternatives, impact on tenants/history/APIs, migration, testing and rollback. Never silently overwrite a prior decision's rationale.
- Don't schedule development tasks, modify source, migrate databases or activate features because these specifications exist. Implementation requires a prioritized approved task and exact branch scope.

