# Security policy

## Reporting a vulnerability

Do not open a public issue containing credentials, personal data, an exploit, or a cross-tenant proof of concept. Contact the repository owner privately and include only the minimum information needed to reproduce the problem.

## Secrets

Repository configuration files contain safe defaults only. Supply secrets with deployment environment variables or a managed secret store. For local development, use .NET user-secrets:

```bash
dotnet user-secrets --project EduOS.App set "ConnectionStrings:DefaultConnection" "<local-connection-string>"
dotnet user-secrets --project EduOS.App set "JwtSettings:Secret" "<at-least-32-random-characters>"
dotnet user-secrets --project EduOS.App set "LearnerIdentity:LookupKeyBase64" "<base64-encoded-random-32-byte-key>"
dotnet user-secrets --project EduOS.App set "SuperAdmin:Email" "<initial-admin-email>"
dotnet user-secrets --project EduOS.App set "SuperAdmin:Password" "<unique-random-bootstrap-password>"
dotnet user-secrets --project EduOS.App set "EmailSettings:SenderEmail" "<sender-email>"
dotnet user-secrets --project EduOS.App set "EmailSettings:Password" "<smtp-app-password>"
dotnet user-secrets --project EduOS.App set "SmsSettings:ApiKey" "<api-key>"
dotnet user-secrets --project EduOS.App set "SmsSettings:ApiSecret" "<api-secret>"
dotnet user-secrets --project EduOS.App set "Payments:AamarPay:StoreId" "<store-id>"
dotnet user-secrets --project EduOS.App set "Payments:AamarPay:SignatureKey" "<signature-key>"
```

Production instances must share a durable ASP.NET Core Data Protection key ring. Set `DataProtection__KeysPath` to a protected persistent location available to every application instance, or replace the file provider with a managed key store before horizontal scaling.

Every instance must also receive the same `LearnerIdentity__LookupKeyBase64` from a managed secret store. This keyed HMAC value is independent of Data Protection and must contain at least 32 decoded random bytes. Changing it without re-indexing protected identifiers makes existing equality matches unavailable; rotation therefore requires a reviewed migration with old/new overlap. Never log identifier plaintext, protected values, or lookup digests.

SuperAdmin creation is opt-in: no account is created unless `SuperAdmin__Email` is configured, and first-time creation also requires `SuperAdmin__Password`. There is no default privileged email or password. Remove the bootstrap password from runtime configuration immediately after the first account is created, then enable MFA before production administration.

MFA is optional for every role, including TenantAdmin, SuperAdmin and AdmissionOfficer. Users sign in with a verified, active username/email and password without an MFA setup gate unless they previously enabled TOTP in Profile > Two-Factor Authentication. Users who enabled MFA must complete a short-lived, security-stamp-bound Data Protection challenge at subsequent sign-in. Enrollment requires the current password and verified authenticator code; recovery codes are displayed once. Never log or transmit authenticator secrets and recovery codes. A production runbook must cover identity-verified MFA recovery, credential resets and revocation of existing sessions after security changes.

Admission applications contain tenant-owned personal data. Access is restricted to TenantAdmin and AdmissionOfficer and also requires the tenant's ADMISSION entitlement. List responses mask mobile numbers; full contact is returned only by the authorized details endpoint. Applicant values are not copied into general audit JSON. NID, birth registration and passport values must never be added to `AdmissionApplicant`; they belong only in the protected learner-identity workflow. Minor applications require guardian contact, but this is not proof of legal authority and cannot approve cross-tenant history access.

If a value was ever committed, deleting it from the latest file is not enough. Revoke or rotate it at the provider immediately, then purge it from Git history using a reviewed incident-response procedure.

## Tenant isolation

Tenant-owned entities implement `ITenantScopedEntity`. The database context applies a soft-delete and current-tenant filter and rejects cross-tenant writes from authenticated tenant users. Platform/background operations must specify the target tenant explicitly and remain auditable.

No-tenant requests receive no tenant-owned records by default.

## Admission intake migration

`20260907130000_AddAdmissionIntakeWorkflow` is additive: it creates only `AdmissionApplicants`, its indexes and restrictive foreign keys. Rollback drops that table and permanently removes any collected applications, so production rollback requires a reviewed encrypted export or an explicit decision that the data is disposable. Schema migration remains a controlled deployment step and is not run automatically in production.

`20260908010000_AddAdmissionEnrollmentWorkflow` is also additive. It adds stable public references, optimistic-concurrency tokens, admission linkage and enrollment campus/term metadata to existing Student, Guardian and Enrollment tables. Conversion executes inside one retry-aware database transaction; student, guardian and enrollment PII is excluded from general audit JSON. Rollback removes the added linkage metadata, so export and review admitted records before any production rollback.
