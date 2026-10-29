# SaaS / Platform / Security: code completion vs external acceptance

Owner scope: EduOS V1 on the `Ashraful` branch. HR/Payroll belongs to V1.
This is an engineering handoff, **not** a production certification.

## Code-owned gates (count these in engineering progress)

- Tenant/campus/resource authorization, safe account/MFA/session lifecycle and
  tenant-scoped onboarding, subscriptions, membership and user-seat enforcement.
- Student/employee active capacity across all known create/reactivation paths,
  with SQL transaction locking for competing writes.
- Private uploads, document/message storage quota and applicant ownership.
  Payment proof is deliberately permitted to exceed storage quota for renewal.
- Authentication-protected API outputs must not be stored by shared caches;
  browser/API rate-limit rejection must retain HTTP 429 (not 302).
- On production startup, the file storage path and Data Protection keys must be
  outside the public webroot. Windows Server deployments encrypt the persisted
  Data Protection key ring with machine-scoped DPAPI. Configure file-system ACLs
  and durable protected backups separately. Multi-machine hosting needs a
  separately approved shared key-protection mechanism.
- Unit/UI/JavaScript contracts, real SQL Server integration/concurrency tests,
  solution build/publish and clean migrations must be green on the exact commit.

## External/deployment acceptance (do not count as unfinished source code)

- Real payment-provider callbacks and financial/statutory refund/tax policy.
- Institution-owned staging/production credentials and domain/SMTP/DNS setup.
- Real institute and browser/mobile UAT, independent penetration assessment,
  measured production-like load, full backup/restore drill and owner go/no-go.

Automation pass does not guarantee that a production deployment or a security
review will find zero defects. Any independently found defect is V1 maintenance
and must be fixed and regression-tested without shifting the module to V2.
