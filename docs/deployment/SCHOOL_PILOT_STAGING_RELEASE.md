# School/College pilot staging release — safe feature boundary

## Scope and switch

The first school/college pilot is limited to existing onboarding, campus, user/role,
admissions, students/enrolments, academic setup, attendance, assessment and authorized
fee workflows. **Do not treat this as a production readiness certificate.**

Set `PilotRelease__Enabled=true` **only in the isolated pilot staging deployment**.
The committed default is `false` so existing customers retain their configured
module entitlements. When enabled, the server authorization handler rejects
`HOSTEL`, `TRANSPORT` and `INVENTORY` (which also owns Asset Lifecycle)
**before** checking tenant entitlements. The sidebar hides their navigation and
Operations Insights. Direct API/MVC requests still fail authorization; hiding a
link is not a security boundary. No database records or tenant selections change.
Do not use this switch as a substitute for ordinary tenant/plan/role checks.

The pilot switch is **deployment-wide**, not tenant-specific. Use a separate
staging deployment/database; do not enable it in a shared production instance
with existing optional-module customers. Procurement remains unimplemented and
must not be exposed as a working workflow.

## Before staging access

1. Provision a separate SQL Server database, application hostname and HTTPS
   certificate; never point staging at a live customer database.
2. Use a dedicated least-privilege application SQL login. Put connection strings,
   JWT signing secret, SMTP credentials and any payment secrets in a secret store
   or deployment environment, never source control.
3. Set `DatabaseInitialization__Enabled=false` and
   `DatabaseInitialization__ApplyMigrations=false`. Review migrations and apply
   them **as a separate controlled step** against a disposable restored copy first.
4. Configure durable protected `DataProtection__KeysPath`, trusted `AllowedHosts`
   and explicit HTTPS `Cors__AllowedOrigins`. Disable real payment callbacks,
   live SMS/email destinations and real customer imports for staging exercises.
5. Take a SQL Server full backup; record backup timestamp, checksum, encryption
   custody and recovery point. Rehearse restore into a different database and
   verify tenant-scoped row counts and schema version before granting pilot access.
6. Verify `/health/live` and `/health/ready` after migration, and confirm
   no automatic schema changes occur at startup.

## Real-user acceptance checklist (staging only)

- [ ] Tenant signup/verification, plan/trial and onboarding complete without
      access to another institution.
- [ ] TenantAdmin creates campus and academic year/term, invites a Principal
      and Teacher, and can revoke membership; inactive/unauthorized roles fail.
- [ ] Admission officer reviews and approves an application; Student record
      and active enrolment appear only in the correct campus.
- [ ] Teacher records attendance; Principal sees campus-scoped report/readback.
- [ ] Exam officer publishes a result; unauthorized modification is rejected.
- [ ] Finance officer verifies fee readback in test-only data; **no real posting**.
- [ ] Direct `/HostelOperations`, `/TransportOperations`,
      `/AssetLifecycle`, `/api/hostel`, `/api/transport` and
      `/api/inventory/assets` requests are denied even for previously entitled
      users when the staging switch is enabled; links are hidden.
- [ ] With the switch off in a disposable test environment, existing tenant
      module entitlements and role checks still work.
- [ ] Invalid, duplicate, cross-tenant and simultaneous requests are exercised;
      record screenshots, request IDs, CI SHA and SQL smoke evidence.
- [ ] Rehearse rollback to the previous application image (no schema rollback
      assumed) and restore the isolated staging backup; validate login and data.

## Release decision

A release owner must review security, tenant isolation, database restore,
migration rehearsal, data protection, access recovery, browser E2E, and actual
school user acceptance. No live deployment, customer data migration or
financial posting is authorized by this checklist.
