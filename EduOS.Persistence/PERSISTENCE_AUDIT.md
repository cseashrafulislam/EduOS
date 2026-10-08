# EduOS.Persistence / EduOS.Core compatibility audit

Reviewed branch: `Ashraful`. Scope: `EduOS.Persistence/` only.

## Static contract inventory

| Check | Result |
|---|---|
| Concrete entity classes in 20 Core entity-model source files | 215 |
| Distinct `EduOSDbContext.DbSet<T>` declarations | 215 |
| Core entity classes without a corresponding DbSet | 0 |
| Concrete specialized repository contracts in Core | 39 |
| Specialized repository DI registrations | 39 |
| Specialized repository DI registrations missing | 0 |
| Generic repository registration | `IGenericRepository<>` -> `GenericRepository<>` |
| Unit of work | `IUnitOfWork` resolves the scoped `EduOSDbContext` |
| Committed EF Core migrations | **0 - blocking** |

Every mapped entity **does not** need a specialized repository. For most entities, the generic repository and EF Core query/projection are sufficient; create a dedicated repository only for meaningful domain-specific queries.

This is a **source-level** cross-check. It does not establish that the complete EF Core model can be finalized, migrated, or queried successfully against SQL Server.

## Corrected Persistence defects

- Changed generic ID lookup to execute global tenant/soft-delete filters, including when a conflicting entity is tracked.
- Added deterministic order and bounded page sizes; oversized page offsets retain the correct result count.
- Completed specialized repository DI registrations, including audit, login history, refresh token, and tenant membership.
- Filtered role-to-permission resolution through the tenant-aware role query.
- Avoided `FindAsync` for notification read updates, which could bypass tenant filters via the change tracker.
- Added checked role-bootstrap results and repaired existing global SuperAdmin role metadata.
- Validated academic-year ownership before changing current-year flags and avoided loading every historical year.
- Compared exact expected assessment/registration pairs for mark-completeness checks.
- Used date ranges instead of extracting `Month` and `Year` on payment dates.
- Fixed single-evaluation invoice number generation and compatibility with nonrelational test database transactions.
- Fail-fast when database initialization requests migrations but the assembly contains no migrations.

## Remaining blocking conditions

1. **No migrations are committed.** Do not assume an existing database is empty, run `EnsureCreated`, or hand-write a 215-table migration. First establish the target schema, existing records, deployment strategy, and migration baseline; scaffold a migration using the actual final EF model; review SQL and indexes before applying it to a backed-up environment.
2. **Solution CI does not currently validate Persistence runtime behavior.** The latest workflow fails in `EduOS.Core` before migrations, unit tests, and publish validation run. Fix those errors in their owning project before interpreting CI as a Persistence pass.
3. **Legacy compatibility method names exist** (for example, `GetWithFeaturesAsync` and `GetWithGuardiansAsync`) while their current result entities have no equivalent owned navigation collections. Avoid assuming these methods materialize child objects; change their consumer contract and implementation together in a future separately scoped change.
4. Core repository list endpoints that return all rows remain unbounded by contract. Large-history endpoints need paging at the application/API boundary with server-side filters.
5. The academic-year current flag has a DB uniqueness constraint. Verify cross-row switch order, concurrency, and rollback in SQL Server before production; changing the method to independently save midway through a caller's unit of work would be unsafe.

## Required validation before production

- Compile `EduOS.Core` and `EduOS.Persistence` independently and then the full solution.
- Generate and review EF migration SQL against an isolated copy of the target database; verify all 215 entities and constraints.
- Test cross-tenant lookup, tracked entity lookup, soft delete, audit transaction atomicity, concurrent status transitions, repeated startup/seed operations, and retried submissions.
- Run CI tests, SQL Server integration checks, EF model snapshot validation, and application publish.
- Backup and rehearse restore and rollback before deployment.

**Do not mark Persistence deployment-ready until migration/model validation and targeted integration tests pass.**
