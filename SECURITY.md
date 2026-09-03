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

TenantAdmin and SuperAdmin cookie sessions are gated by TOTP MFA. Setup requires the current password, login uses a short-lived Data Protection challenge tied to the user's security stamp, and recovery codes are issued once. Treat authenticator setup keys and recovery codes as credentials: never log, email, or screenshot them; keep recovery codes offline. A production runbook must define identity-verified MFA reset and emergency access without weakening this gate.

If a value was ever committed, deleting it from the latest file is not enough. Revoke or rotate it at the provider immediately, then purge it from Git history using a reviewed incident-response procedure.

## Tenant isolation

Tenant-owned entities implement `ITenantScopedEntity`. The database context applies a soft-delete and current-tenant filter and rejects cross-tenant writes from authenticated tenant users. Platform/background operations must specify the target tenant explicitly and remain auditable.

No-tenant requests receive no tenant-owned records by default.
