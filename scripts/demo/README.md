# EduOS isolated demo database and role logins

This is a **fictional demo only**, not a production database importer or real
customer data. Never paste the SQL into a live institution database.

## 1. Prepare a separate SQL Server database

1. Create an empty database whose **actual** name begins `EduOS_Demo_`, such
   as `EduOS_Demo_Showcase`. Never use an existing production backup or a
   connection string pointing at a live institute.
2. Apply the current reviewed EduOS EF Core migrations to that new database.
3. Configure the **Staging** app instance (not Production):

   - `ASPNETCORE_ENVIRONMENT=Staging`
   - `ConnectionStrings__DefaultConnection` = the isolated database connection
   - `DemoData__Enabled=true`
   - `DemoData__SharedPassword` = a strong unique password (16+ chars)

   Keep the password in the deployment secret store, never in a repo, SQL file
   or application log. This password is **intentionally disclosed to visitors
   on the demo login page** when enabled. The database and host must contain
   no real student, employee, bank, academic, health or financial data.

4. Start EduOS. The opt-in Identity bootstrap creates `EDUOS-DEMO`, a default
   campus, and 13 tenant-scoped demo roles and users, using `UserManager` to
   produce real salted password hashes. It also prepares a non-public free
   demo-only plan and entitled modules in this isolated database.

The login page now displays a **Demo Login** button in Staging only while
`DemoData:Enabled=true`. Select a role and sign in through the normal
rate-limited login API. Real session, membership and MFA checks still apply.
Public demo access **never includes SuperAdmin**.

| Role users | Sign-in identifier |
| --- | --- |
| TenantAdmin, Admin, AdmissionOfficer, Principal, VicePrincipal, Teacher, Student, Parent, Accountant, HR, Librarian, ExamController, Staff | `demo.<lowercase-role>@eduos.example.invalid` |

Examples: `demo.tenantadmin@eduos.example.invalid`,
`demo.student@eduos.example.invalid`. They all use the configured demo-only
shared password. If a demo account already exists, startup will **not**
overwrite its password or reset manually entered data.

## 2. Run the single demo-data SQL script

Open `01-seed-academic-demo.sql` in SSMS, select the **demo database** and
execute. The first guard refuses any other database name or any live tenants;
the script also requires the bootstrap identity records and applied migrations.
It uses a single transaction, explicit tenant references and idempotent
`DEMO-*` codes, then returns a per-table coverage report.

It creates/maintains realistic but **synthetic** samples in the academic and
student domain: 12 campuses, years, terms, departments, programs, levels,
tracks, mediums, shifts, rooms, subjects, curricula, curriculum subjects,
routine periods, program-campuses, batches, offerings, students, guardians,
student-guardian links and enrollments/subject registrations. The one Student
and Parent role user link to the first sample student and guardian.

**Do not seed 12 invented financial transactions, invoices, payroll batches,
audit events, grants, consent decisions, security sessions or payment proofs.**
There are hundreds of EF tables, many representing immutable evidence or
approval state, not independent master data. The remainder of the application
should be populated using its **real role-authorized workflows**, with
corresponding approval/ledger/stock transitions and valid integration fixtures.
The script intentionally does not claim to fill every table.

## 3. Verification and safe teardown

- Confirm the SQL output counts and open representative Academic Setup,
  Students, Enrollments and Guardian pages with the demo role logins.
- Confirm roles cannot access another tenant, SuperAdmin cannot sign in through
  the demo chooser, and direct unauthorized URLs remain denied.
- Test with fresh and repeated script runs in a **disposable** database first.
  No reset, deletion, bulk overwrite or automatic production migration is
  provided here.
- To disable public demo login, set `DemoData__Enabled=false` and restart the
  Staging app. Do not point this demo application at any production database.

The script and UI code were **not** an executed production-data load. A live
demo deployment requires a successful fresh/repeat SQL rehearsal and login UI
verification before it can be declared ready for public viewing.
