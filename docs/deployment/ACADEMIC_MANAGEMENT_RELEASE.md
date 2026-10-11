# EduOS Academic Management — Release and IIS Deployment Gate

**Branch of record:** `Ashraful`. This document does not authorize deployment of unverified code.

## Scope and data ownership

- `AcademicYear` and `AcademicTerm`: institution-scoped foundation records; dates and current flags are protected by server-side validation.
- **Class/Grade:** `AcademicProgram` + `AcademicLevel`; no duplicate Class master.
- **Section:** `AcademicBatch`, linked to campus/year/program/level and optional term.
- **Subject:** tenant-scoped `Subject`; a subject is not copied into each section.
- **Curriculum:** `AcademicCurriculum` and `CurriculumSubject`; versioned releases retain old records.
- Activation of a current curriculum is transactional. The SQL Server filtered unique index on `(TenantId, AcademicProgramId, AcademicTrackId, MediumId)` ensures at most one current curriculum per scope.

The academic setup API enforces authenticated roles, tenant ownership, anti-forgery tokens and the ACADEMIC module gate. The post-onboarding UI is at `/AcademicManagement/Index`; existing onboarding screens retain their prior routes.

**Historical safeguard:** Creating a new curriculum version is preferable to editing existing subject/grade assignments used by students, marks or attendance. New academic Programme, Class, Section and Subject screens currently expose **create + bounded list**, not unrestricted destructive update/delete of historical setup. Year and Term provide update/archive with version checks. Curriculum provides activation of an existing version.

## Automated CI (mandatory)

1. `.github/workflows/ci.yml`: net10 solution restore/build, Auth and other regressions, EF snapshot drift, idempotent migration generation, full tests and publish.
2. `.github/workflows/sqlserver-migration.yml`: fresh disposable SQL Server 2022 migrations, repeat idempotent migration, tenant Auth/Session/Campus SQL tests **and AcademicCurriculum activation SQL test**.
3. `.github/workflows/academic-browser.yml`: real Chromium test exercising page DOM, CSRF headers and forms using isolated mocked APIs; **not production API integration**.
4. `.github/workflows/academic-live-browser.yml`: manual, environment-approved **read-only authenticated** Chromium check of actual deployed HTTPS site (no test MFA bypass, no destructive writes). Requires GitHub environment variables `EDUOS_BASE_URL`, optional `EDUOS_E2E_TENANT_ID` and secrets `EDUOS_E2E_USER`, `EDUOS_E2E_PASSWORD`. Use a dedicated non-human test account with TenantAdmin role for one disposable/staging tenant and secret rotation. The test account's own MFA preference is never changed automatically.

Do not treat a fixture-based UI test as proof that login, SQL authorization or SMTP work in production.

## Windows IIS production deployment

**Prerequisite:** authorized production IIS runner/operator, .NET 10 Hosting Bundle, verified SQL Server connection, HTTPS certificate, durable Data Protection keys, persistent private upload storage, known environment secrets, backup and restore credentials.

1. Freeze the approved `Ashraful` SHA and archive the **successful** CI and SQL Server run URLs for that exact commit. Do not deploy a newer unverified commit.
2. Back up SQL Server using **tested** backup/restore procedures and preserve current IIS release/physical path, production `appsettings.Production.json`, protected secrets, upload directory, Data Protection keyring and logs. Check available disk space and app pool rights.
3. Run migration preflight and review a generated `dotnet ef migrations script --idempotent`. Require an approved SQL migration plan; never rely on IIS app startup to run automatic migrations.
4. Publish:
   ```powershell
   dotnet restore EduOS.slnx
   dotnet build EduOS.slnx -c Release --no-restore
   dotnet test EduOS.slnx -c Release --no-build
   dotnet publish EduOS.App/EduOS.App.csproj -c Release --no-restore -o ./publish
   ```
5. Deploy the **published package** to a new versioned release directory on Windows Server. Copy production configuration securely; never commit secrets. Avoid `robocopy /MIR` against an IIS directory containing files created by the application (private uploads/data loss).
6. During an approved maintenance window, point IIS to the staged release and recycle the app pool. Apply reviewed additive SQL changes in the approved order. Keep old app release for a quick code rollback. Validate health/auth/tenant checks immediately.
7. Perform authenticated browser smoke with a dedicated tenant-admin account: direct password login for MFA-disabled account, challenge for MFA-enabled accounts, institution selection, Year/Term catalog, Class/Section/Subject and Curriculum visibility; verify unauthorized and cross-tenant denial independently.
8. Verify old academic data, admission references, reporting, audit, backups and logs; monitor failures and performance. For a bad deployment, restore previous code release; only restore database from backup if a coordinated recovery plan handles data created after the backup.

## Operational sign-off conditions

- Full CI, SQL Server migration and browser smoke are **green at the same Git commit**.
- Environment-approved authenticated browser run against the actual HTTPS host succeeds.
- Operations team confirms backup recovery point, effective secrets, data protection, IIS hosting, migration safety, health monitoring and rollback.
- Tenant and campus authorization is checked with at least two institutions and non-admin test identities.
- No claims of 100% live production readiness before the operator verifies actual deployment.

No workflow in this repository has been authorized or supplied with access to modify the user's live IIS host from this chat. Pushing source and passing publish checks **does not deploy it**.
