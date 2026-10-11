# Auth security release: tenant roles, opt-in MFA and sessions

## What changed

- Login accepts `TenantId` optionally. For multiple valid memberships without a selected institution, a password-verified request returns HTTP 409 with `data.requiresTenantSelection` and a list of valid institutions; the browser explicitly resubmits that choice with credentials.
- Encrypted MFA login challenges include the selected tenant. Users who did not enable MFA still use direct password-to-dashboard login.
- Role claims are derived only from approved tenant-scoped role assignments, institution ownership, or an unambiguous legacy global role when the user has exactly one active institution. Platform SuperAdmin requires the global system-owned role, never a tenant-local role of the same name.
- Tenant middleware validates the exact role set against current DB assignments on each request; stale claims are denied.
- Identity security-stamp principal renewals preserve only signed session-specific claims. Tenant membership and roles remain subject to server revalidation.
- Browser cookies have server-side session IDs in `AspNetUserTokens` with the dedicated `EduOS.CookieSession` provider. Logout, selective revoke and revoke-all delete registry records, so a stolen old cookie cannot bypass revocation.
- MFA disable and recovery-code rotation require an MFA-verified session, current password, and authenticator/recovery code; successful changes invalidate sessions.
- Active sessions are capped to 20 per user and expired sessions for active users are pruned at login.

## Compatibility and deployment

**Expected security change:** pre-release cookies without a session ID will be rejected on deployment. Users must sign in again. This does not reset passwords, disable MFA, or delete any historical login record.

**Schema:** no new table is required; the existing ASP.NET Core Identity `AspNetUserTokens` table stores server-side cookie-session metadata. Do not conflate these records with `RefreshToken`, which remains a separate planned JWT lifecycle.

**Legacy role audit:** run `scripts/security/audit_multitenant_global_role_assignments.sql`. Every multi-institution account with a global legacy role requires deliberate tenant-specific mapping. No role is automatically copied across institutions.

**Operational prerequisites:** rotate previously committed credentials, provision secret settings, preserve Data Protection keys across deployments, review permissions, and back up/restore database before release.

## Integration and operational test cases

1. Single-tenant user with MFA disabled signs in directly; old global role works only for the unambiguous single membership.
2. Multiple active memberships: institution picker appears; invalid/unauthorized institution choice is rejected without a cookie.
3. Two tenant memberships with a global privileged role: no privileged claims in another tenant; scoped grants and owner status only apply to the matching institution.
4. MFA-enabled user: password login issues challenge with selected tenant; invalid, expired, tampered and replayed challenges are rejected.
5. MFA enable/disable/recovery-code rotation: correct code succeeds, invalid code fails, other sessions become invalid after stamp rotation.
6. Sessions list shows current and other devices; revoke-one denies its next request; revoke-all denies all cookies while another user's sessions remain valid.
7. Change/reset password revokes active server sessions; suspended membership or suspended tenant denies access.
8. Identity stamp validation refresh preserves session ID, tenant and scoped role claims; middleware still compares claims with the DB.
9. Anti-forgery, lockout and endpoint rate limits remain enforced; check cross-tenant API responses after role changes.
10. Run `dotnet test EduOS.slnx --configuration Release` plus SQL Server-hosted login/session tests. The in-memory tests are NOT substitutes for actual SQL Server transactional/concurrency testing.

## Remaining release gates

Production must not be declared verified until the remote CI job and SQL Server-backed tests are green, deployed Data Protection key persistence and secret provisioning are verified, and the legacy role audit has been reviewed. A periodic retention policy for inactive accounts' stale Identity session-token records is recommended when production login volume warrants it.
