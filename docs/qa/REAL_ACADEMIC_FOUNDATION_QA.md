# EduOS — Real Academic Foundation Data-Entry QA

## What executes
Exact-SHA GitHub Actions workflow
\`.github/workflows/real-data-entry-qa.yml\` launches ASP.NET Core EduOS with
SQL Server 2022 in a *disposable GitHub Actions runner*. It uses no user
Codespace, public host, production data, or stored credentials.

In addition to actual Year and Term forms, a second real Playwright browser
recreates the ordinary TenantAdmin user journey:
1. Create Programme using the rendered form; reload and confirm backend Code.
2. Create Class/Grade One under that Programme; reload and confirm parent FK.
3. Create a Subject using rendered form; reload and confirm generated Code.
4. Create Section A under the Class and demo campus, **without any Batch**;
   reload and confirm Code.
5. Independently query SQL Server through an opt-in xUnit verifier to assert
   exact tenant, programme-to-class, class-to-section, campus, decimal credits,
   source-level uniqueness and no Academic Batch prerequisite.

The CI job fails on missing form, API error, incorrect FK, missing generated
code or missing SQL persistence. Synthetic-only evidence is retained for 7 days.

## Limitations and release decision
Passing these checks validates isolated **academic foundation** operations,
not the full school academic/admission/fees/GL or inventory workflows, and does
not certify the 204-route real institutional UAT. Full production QA still
requires actual hosted staging, realistic data, user authorization matrix,
full financial/stock reconciliation, rollback rehearsal and institution owner
sign-off. No automation can truthfully substitute the missing external
institutional acceptance.
