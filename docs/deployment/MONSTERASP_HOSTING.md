# EduOS on MonsterASP.NET — safe startup/deployment

The confirmed HTTP 500.30 exception was: `DataProtection:KeysPath is required in Production`. The checked-in `appsettings.json` now sets `DataProtection:KeysPath=../private/keys` as a non-secret default for a website whose content root is `wwwroot`, matching MonsterASP's documented `/private` sibling directory. Runtime `DataProtection__KeysPath` still overrides it. Verify that the hosting account grants persistent write access; no restart can fix a missing or unwritable private directory.
Restore of 360 tables does not prove migration/data correctness. Use an isolated demo,
do not put real institution data on an unverified free hosting tier.

## Protected environment settings

| Name | Host-side value |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `AllowedHosts` | `myeduos.runasp.net` |
| `Cors__AllowedOrigins__0` | `https://myeduos.runasp.net` |
| `ConnectionStrings__DefaultConnection` | MonsterASP **local** SQL credentials for `db80119` |
| `JwtSettings__Secret` | Unique random secret, 32+ characters |
| `DataProtection__KeysPath` | Optional override; default `../private/keys` means the non-public `private/keys` beside `wwwroot` |
| `FileStorage__PrivateBasePath` | Private, persistent, writable upload directory |
| `DatabaseInitialization__Enabled` | `false` |
| `DatabaseInitialization__ApplyMigrations` | `false` |
| `DemoData__Enabled` | `false` |

Confirm directory location and ACLs in hosting File Manager/support before using.
The reported IIS web root was `D:\Sites\site107961\wwwroot`. **Do not assume**
`D:\Sites\site107961\private` exists or is writable. Both keyring and
private files must be outside the public web root and independently backed up.

## Shared Windows IIS: optional certificate mode

Default dedicated Windows behavior is DPAPI. If the shared host blocks machine
DPAPI, an explicitly configured private certificate can encrypt Data Protection
keys at rest:
- `DataProtection__KeyProtectionMode=Certificate`
- `DataProtection__CertificatePath` points to a persistent **private** .pfx
- `DataProtection__CertificatePassword` is a protected hosting secret

Do not commit or publish the PFX, its password, or raw keyring XML. The PFX
must have a private key and be readable by the app. Preserve the certificate
and old keyring through deployments and key rotation; otherwise cookies and
encrypted application data can be lost. If private persistent storage or PFX
loading is unsupported on the free tier, change hosting instead of weakening
security. Never expose a plaintext/unencrypted key ring for real users.

## Verify

1. Check restored SQL backup, tenant-scoped rows and EF migration history; keep
   automatic migration OFF.
2. Restart, inspect latest exception under Websites > Logs > ASP.NET Core debug.
3. Confirm `/health/live`, `/health/ready`, login, tenant/role denial and
   private files. Disable debug/stdout logging once the problem is fixed.
4. Rotate previously shared SMTP/admin/database credentials and review history.
5. Sign off manually before hosting real financial/student data.
