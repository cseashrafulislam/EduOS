# Real Student onboarding acceptance on disposable SQL

Runs after the existing Year/Term + Programme/Class/Subject/Section real forms
in `real-data-entry-qa.yml`.

The actual TenantAdmin browser creates one current Curriculum and mandatory
Subject mapping, one Academic Batch referencing the already-existing Section,
one required section Subject Offering, and one Student with Guardian through
`/Students/Create`. It asserts generated internal Student code, current
Enrollment, a single required Subject Registration, parent/campus/course
consistency, and the same ClientRequestId replay producing no second Student.
A conflicting payload with the same request ID must return HTTP 409.

A separate real SQL Server xUnit test checks committed entities, tenancy,
foreign keys, uniqueness, capacity and historical dates. A failed browser
POST or SQL invariant keeps the exact-SHA QA workflow red.

**Not full student ERP acceptance**: admission application approval, fee
invoice/payment, payroll, inventory, refunds, reports and real institution
UAT require their own end-to-end checks. Only synthetic identities, disposable
2022 SQL Server, and random runner credentials are used.
