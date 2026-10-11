# EduOS production secrets and bootstrap

The repository's `EduOS.App/appsettings.json` intentionally contains **no** administrator bootstrap password or SMTP password. Do not commit live credentials to any tracked appsettings file, tests, screenshots, scripts, or logs.

## Required deployment configuration

Set sensitive values in your deployment's secret manager or protected environment variables (double underscores map to configuration colons in ASP.NET Core):

| Variable | Purpose |
|---|---|
| `SuperAdmin__Email` | Explicit bootstrap administrator account; leaving empty skips bootstrap |
| `SuperAdmin__Password` | One-time initial strong password for a new bootstrap administrator |
| `SuperAdmin__FullName` | Optional display name |
| `EmailSettings__SenderEmail` | Authenticated SMTP sender address |
| `EmailSettings__Password` | SMTP app password or provider credential |
| `ConnectionStrings__DefaultConnection` | SQL Server production connection string |
| `JwtSettings__Secret` | Unique, cryptographically random secret with 32+ characters |
| `DataProtection__KeysPath` | Durable and protected ASP.NET Core Data Protection key location |

Other production-specific settings, such as allowed hosts, portal domain, payment-gateway credentials, and CORS origins, must be set appropriately for the environment.

For IIS, use a protected deployment configuration or secret-management facility that does not place plaintext secrets inside the published application directory or Git repository. Restrict permission to the application identity. Do not store credentials in public `wwwroot` paths.

## Administrator bootstrap

When `SuperAdmin__Email` is empty, `SuperAdminSeeder` skips bootstrapping. On a first installation, provision both `SuperAdmin__Email` and `SuperAdmin__Password` through the secret store before running the controlled initialization. Configure email verification/sending separately.

The bootstrap seeder does not overwrite the password of an existing administrator. Remove the one-time bootstrap password from long-lived deployment configuration after successful initial provisioning, unless the operational workflow requires it; preserve `SuperAdmin__Email` only if needed for repeat initialization.

## Previously committed secrets: mandatory incident response

Removing plaintext secrets from the current branch **does not** erase Git history, forks, caches, build artifacts, or earlier deployments. Treat any previously committed administrator password and SMTP credential as compromised.

1. Rotate the administrator password for any account using the previous value, and revoke other sessions as appropriate.
2. Revoke the exposed SMTP app password/token at the provider, issue a replacement, and update the deployment secret store.
3. Check audit/login history and SMTP sending logs for suspicious activity.
4. Check other branches, deployment variables, build logs, and artifacts for additional copies.
5. Run the production smoke tests: administrator sign-in, ordinary user sign-in without MFA, MFA-enabled sign-in with challenge, recovery-code sign-in, password reset email, and account lockout.

MFA remains **user-optional**. Do not enforce MFA enrollment in login, onboarding, or middleware; only accounts with MFA enabled require a challenge on subsequent logins.
