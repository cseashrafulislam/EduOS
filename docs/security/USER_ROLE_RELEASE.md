# EduOS tenant user, role and Auth release gate

## Implemented in Ashraful

- Tenant-bound user directory (server-side paging and filtering)
- Explicit tenant role create/update/enable/disable; platform SuperAdmin is never tenant-assignable
- Role assignment with membership `RowVersion` optimistic concurrency and session stamp invalidation
- Permission catalog, per-role grants, new `[RequirePermission("CODE")]` policy with tenant-bound role join, explicit per-user deny precedence
- Tenant membership status management (owner and self deactivation prohibited)
- Campus access assignments with per-tenant FK validation and membership `DefaultCampusId` as canonical default
- Email invitation, one-time hashed token, invitation-selected role/campus, consent-based user creation; existing accounts require password and enrolled MFA to accept a new tenant
- Role changes recorded with auditable tenant/user/old/new references
- TenantAdmin MVC pages, member/role/permission/campus UI, invite/send/accept UX

## Compatibility and constraints

The browser uses **cookie authentication**, with revocable Identity token records. Do not introduce a second JWT authority just to satisfy unused interface contracts.

**MFA is strictly opt-in.** A user who did not enable MFA signs in directly; a user with MFA enabled completes the challenge. Accepting a new institution invitation is an account-access mutation and requires MFA from an already enrolled account.

TenantRoleResolver rejects ambiguous legacy global role grants for multi-institution users. Run `scripts/security/audit_multitenant_global_role_assignments.sql` before deployment. Review individual tenant-specific role replacements; NEVER copy legacy elevated roles to every membership.

Tenant User Administration only operates on trusted `ICurrentUserService.TenantId` from server middleware; client-supplied tenant identifiers are ignored. Member/role mutations use current membership ownership, status, concurrency tokens and server-side authorization.

The existing `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `TenantMemberships`, `UserCampusAccesses`, `RolePermissions`, `Permissions`, `TenantInvitations`, `UserPermissions` and `AuditLogs` tables are reused. New table migrations are not part of this change; **validate the actual deployed schema and pending EF migrations** before rollout.

**Important permission boundary:** Existing endpoints guarded solely by `[Authorize(Roles = "...")]` are unchanged. Newly enforced dynamic permission policy only takes effect on routes explicitly annotated with `[RequirePermission("CODE")]`. A full per-action permission migration across Academic/HR/Finance is a separate compatibility-tested rollout. Do not claim that saving a RolePermission automatically changes access to every legacy endpoint.

## Required release verification (NOT inferred from source files)

1. `dotnet restore EduOS.slnx`
2. `dotnet build EduOS.slnx -c Release --no-restore`
3. `dotnet test EduOS.Tests/EduOS.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~Auth|FullyQualifiedName~TenantUser|FullyQualifiedName~TenantPermission|FullyQualifiedName~Mfa|FullyQualifiedName~TenantMembership"`
4. `dotnet test EduOS.slnx -c Release --no-build`
5. `dotnet ef migrations has-pending-model-changes --project EduOS.Persistence --startup-project EduOS.App --context EduOSDbContext -c Release --no-build`
6. Use a disposable SQL Server database: verify cookie renewal, login/MFA, password reset, invite email, invitation accept/replay, tenant role and campus FK restrictions, lockout, antiforgery 400, bad rowversion 409, owner protections, disable/revocation and default campus access.
7. Load-test membership directory queries and role lookups; inspect SQL plans and supporting indexes by TenantId, UserId, RoleId, membership status and normalized name.
8. Verify durable Data Protection keys, SMTP secrets, JWT config, backup/restore, rollback and session invalidation notices on an IIS staging deployment.
9. Confirm the latest Ashraful GitHub Actions build, test, publish and EF snapshot jobs actually succeeded. A successful push **does not mean tests passed**.

### Production sign-off

Block release if any build/test/migration gate fails, or if multi-tenant role audit and SQL Server integration coverage are not approved. Audit includes credential rotation for any historical committed secrets.
