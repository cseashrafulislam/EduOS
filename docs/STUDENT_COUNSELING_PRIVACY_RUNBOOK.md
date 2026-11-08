# EduOS A4 — Student Counseling & Safeguarding operations

## Scope and boundaries

- Optional, nonclinical educational support. **Never** use the record for AI diagnosis or general teacher access.
- All cases/events require trusted tenant scope. Staff require an active membership, current role and campus grant. Private narratives are read by assigned counselors, or narrowly by a safeguarding officer for an urgent requested/escalated case. Every private view is a POST that creates an audited access event.
- An original linked guardian can explicitly grant or withdraw guardian-contact consent. A notification is delivered **only** to that same active guardian with a live guardian role and active tenant membership, after server-side consent revalidation. The in-app message includes no student, case or health data. No SMS/email/outbox copy is created.
- Only monthly UTC campus-level counts of at least five cases can be reported. Smaller groups are suppressed; no case, age, category, urgency, notes or per-day breakouts are exposed.

## Policy approval and retention

1. A live TenantAdmin selects an active campus and proposes a retention duration (30–3650 days), legal-hold flag and bilingual emergency guidance. Reason and idempotency key are mandatory.
2. A **different** live TenantAdmin approves or rejects the proposal. Approval applies in one serializable database transaction. Every campus has one effective policy; only one pending proposal per campus may exist.
3. Retention is measured from case closure in UTC. Without an approved campus policy, no case can be redacted. Legal hold immediately blocks redaction even after expiration.
4. An active safeguarding officer or the case's assigned counselor with an active campus grant requests redaction of an eligible, closed, retention-expired case. The protected reason is stored only in the encrypted case event.
5. A different live TenantAdmin reviews the request. The DB transaction rechecks retention, legal hold, and rowversion; on approval it clears the case's protected summary and every protected event note. Minimal event metadata, independent decision and dates remain. Redaction is irreversible in the active database and the private-view endpoint returns HTTP 410.

## Privacy/incident operations

- Safeguarding intake cannot accept a student/guardian submission unless a currently staffed safeguarding officer has active campus access.
- Emergency guidance is displayed in English or Bangla from the independently approved campus policy. A generic warning tells the user the form is **not** an emergency response channel.
- Do **not** include confidential narrative text in application logs, exception traces, notifications, generic audit payloads, exports, analytics or model prompts.
- Data Protection keys must be **persisted across restarts and shared across instances**. Maintain secure, backed-up keys and a documented key-rotation recovery plan; losing them makes protected notes unreadable. Keys should not be checked into source control.
- The legal-hold decision and redaction approval are subject to institution-specific policy, Bangladesh law and authorized counsel. Clearing active database columns does not retroactively remove older backup copies. Backup retention, restoration access and incident response must be separately controlled.

## Deployment and verification

- Backup and verify restore of the existing database and Data Protection key ring.
- Deploy additive EF migration `20261108152000_CounselingPrivacySafeguarding` before enabling A4 policy UI. Existing counseling records remain untouched; no automatic purge is scheduled. Rollback of the migration deliberately fails if any policy or redaction evidence exists.
- CI must pass .NET build, EF model-diff check, fresh and repeat SQL Server 2022 migrations, A4 real SQL role/maker-checker/retention/hold/redaction/guardian tests and JavaScript contract tests.
- Institution UAT: confirm Bangla and English, mobile/keyboard access, two distinct TenantAdmin accounts, student/guardian consent, urgent campus officer routing, assigned counselor private-view audit, staff role revocation, cross-tenant access denial, expired-case review, and backup recovery procedures. Production key storage and emergency response contacts must be configured before rollout.
