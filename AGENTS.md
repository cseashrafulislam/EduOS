# EduOS agent working rules

EduOS is a configurable, multi-tenant Bangladesh education SaaS. Changes must preserve tenant isolation, privacy, auditability, and backward compatibility.

## Delivery rules

- Work in a feature branch and open a pull request; do not commit directly to `master`.
- Keep each implementation batch small: normally two or three related tables or one end-to-end workflow.
- Include tests for every security boundary, business rule, and bug fix.
- Do not add placeholder controllers, services, entities, or UI that are not wired end to end.
- Do not silently change existing migrations after they may have been deployed. Add a new migration and document rollback impact.
- Never run destructive data commands or force-update a shared branch.

## Security rules

- Never commit credentials, tokens, connection passwords, private keys, or production personal data.
- Use environment variables or a managed secret store. Local development uses .NET user-secrets.
- Every tenant-owned `BaseEntity` must implement `ITenantScopedEntity` (normally by inheriting `BaseTenantEntity`).
- Tenant-owned queries must use the global filter. `IgnoreQueryFilters()` is allowed only in an explicitly reviewed platform-admin or background process with an explicit target tenant and audit trail.
- Never accept `TenantId` from a normal request body as authorization. Resolve it from authenticated tenant context.
- Do not expose NID, birth registration number, guardian data, health data, or academic history in logs or API error messages.
- Government identifiers are external identifiers, never database primary keys. Store encrypted values and use a keyed lookup index.

## Architecture rules

- Prefer a modular monolith with clear bounded contexts before extracting microservices.
- Keep platform-global identity separate from institution-owned enrollment and academic records.
- Core transactions stay relational. Institution-specific fields and workflows use validated configuration/schema definitions.
- Background work must be idempotent. Cross-module side effects use an outbox/event pattern.
- Production schema migrations run as a controlled deployment step, not automatically on every application startup.

## Pull request quality gate

- `dotnet restore EduOS.slnx`
- `dotnet build EduOS.slnx --configuration Release --no-restore`
- `dotnet test EduOS.slnx --configuration Release --no-build`
- Confirm no secret-like value was introduced.
- Document tenant/privacy impact and migration/rollback steps.
