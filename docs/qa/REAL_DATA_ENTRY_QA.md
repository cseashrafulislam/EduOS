# Automatic real data-entry QA — disposable GitHub Actions environment

Workflow: .github/workflows/real-data-entry-qa.yml
Only works on Ashraful and a runner-local disposable SQL Server database
named EduOS_Demo_AutoQa. No production server, real student data or live
transaction is touched. Github Actions generates one-run random SQL, JWT and
demo user credentials. Application startup runs existing Staging-only demo
identity bootstrap (13 roles) and real EF migrations in SQL Server 2022.

Playwright performs actual TenantAdmin form entry to create Academic Year and
Academic Term, reloads both pages to verify persistence and server-generated
codes, then signs in as Student to confirm a protected API is forbidden.
A separate SQL Server integration test verifies tenant, persisted records,
uniqueness, parent FK and business dates. Artifact retention: 7 days, synthetic
identifiers only; the isolated SQL container is stopped after the run.

This is an authenticated real form-data-entry QA slice and DOES NOT certify
204 routes, financial GL postings, full ERP acceptance or institutional UAT.
Use issue #139 for the remaining release blockers. A github.dev Codespace
editor URL is not the application URL or a remotely controllable terminal.
