# EduOS V1 learner identifier verification — owner governance decision

**Decision date:** 2026-10-31  
**Owner choice:** dedicated **Platform Identity Reviewer**, independent of the institution requesting a learner link.  
**Change scope:** governance only. This decision does **not** activate an identifier-verification endpoint, migrate existing people, or authorize a production rollout.

## Non-negotiable identity boundary

- Preserve every institution's existing `Student.PersonId` and its primary identity, admission/enrolment/history snapshots, financial postings, and audit history. Never perform an implicit global `Person` merge.
- A foreign identifier can initiate cross-institution consent **only after independent verification**. A successful consent decision adds a tenant-owned **secondary** `StudentPersonLink` and purpose-/scope-/expiry-bound `LearnerDataGrant`; it does not change `Student.PersonId`.
- No matching by name, date of birth, or phone alone. Unverified, disputed, revoked, expired or unverifiable evidence must fail closed. Do not expose foreign person details or raw government identifiers in search responses, logs, exports or audit payloads.

## Reviewer authority and separation of duties

1. Establish a distinct global `IdentityReviewer` system role assigned to named, MFA-protected platform identities through audited provisioning. It is **not** an alias for `SuperAdmin`, `TenantAdmin`, `AdmissionOfficer`, or `Guardian`. A SuperAdmin may provision access but must not approve identifiers merely by holding SuperAdmin privileges.
2. A reviewer cannot verify an identifier they submitted or a case belonging to an institution where they have a conflicting assignment. Every review decision must recheck the server-side role, account status, independent assignment, MFA freshness and case ownership; a client role claim alone is not authority.
3. Reviewers see a minimal case record and protected evidence access only. They do not receive tenant Student/Finance/HR data or an all-tenant Person browser. Tenant administrators can request verification but cannot mark a global identifier as verified.

## Evidence, decision and audit contract

1. Require a consented verification case with an opaque, access-controlled evidence reference, evidence source/issuer, collection timestamp, permitted purpose, retention/expiry rule, and recorded subject authorization. Keep raw NID/birth-registration numbers and source documents in their existing protected storage, never the audit text.
2. A reviewer must record **Approve / Reject / Dispute / Revoke** with a mandatory substantive reason, evidence reference, reviewer identity, UTC timestamp, request/correlation ID, and safe outcome code. Keep the decision history append-only. Verification is not a mutable checkbox without provenance.
3. Use an atomic server-side transaction, rowversion or equivalent concurrency check, and a client request idempotency key. Conflicting or stale decisions return 409 with no partial identifier/grant write. Persist privacy-safe denial audit and test retry-after-timeout semantics.
4. A dispute or revoked/expired verification blocks **new** consent requests immediately and suspends affected active grants by a separately verified, audited policy-aware action; do not erase historical links, documents or prior approvals. Reverification requires new evidence and independent review, never automatic reactivation.
5. Provide a bounded, read-only privacy-safe case/decision register with audited privileged access, pagination, role checks, lifecycle status and no raw identifier export.

## Implementation and release guard (not yet fulfilled)

The current code persists `PersonIdentifier.IsVerified` / `VerifiedAt` but has **no complete production reviewer decision service**, independent case/evidence ledger, dispute/revocation enforcement or dedicated platform reviewer login/session authorization. The `RoleSeeder`, `TenantRoleResolver`, `AuthController` and `TenantContextMiddleware` currently recognize only `SuperAdmin` as a tenantless platform session. Simply setting `IsVerified = true` or granting all reviewers SuperAdmin would bypass least privilege.

**Do not activate verification** until a reviewed, additive implementation supplies the full role/session, evidence, immutable decision, dependent-grant revocation, API/UI and SQL/browser/IDOR/concurrency tests, with deployment/rollback rehearsal and independent reviewer/provider UAT. Default status stays unverified and no new foreign link/grant is allowed without valid verification and learner consent. This is a V1 security/acceptance exception under the frozen baseline, not permission to rewrite global identity or V2/V3 modules.

**Technical evidence already green:** consent-only link implementation [`2b56331`](https://github.com/cseashrafulislam/EduOS/commit/2b56331a9297e6cb06d91203b927d8ac6ad7b27c), corrected regression [`a36ea8a`](https://github.com/cseashrafulislam/EduOS/commit/a36ea8ac4085ec507d5fbe734900f8f0c2a83e11), exact-SHA [CI](https://github.com/cseashrafulislam/EduOS/actions/runs/41214255559) (3,133/3,133 .NET tests) and [SQL Server smoke](https://github.com/cseashrafulislam/EduOS/actions/runs/41214255467). These prove the approved consent-only path, **not** the missing production verification-provider lifecycle.
