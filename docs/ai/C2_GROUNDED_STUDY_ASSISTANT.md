# C2 — Grounded AI Study Assistant (Issue #126)

Status: implemented on the existing `Ashraful` branch; institution UAT and production rollout remain separate release activities.

## Ownership and source of truth

- One in-process reviewed capability, `education.study.chapter` version 1, registered beside the existing attendance metric in `ReviewedCapabilityRegistry`. There is no second agent framework, arbitrary SQL executor, prompt tools or network provider.
- `Book` owns bibliographic details (title, edition). `Document` (`LIBRARY_DIGITAL`, `LibraryBook`) owns publication version, file, chapter, rights, review/expiry and campus licence. `FileAsset` owns storage verification and private-file integrity. `StudyEvidenceExcerpt` owns only a short source locator and quotation plus independent approval state.
- No AI-generated edits to marks, library assets, enrolments, financial data or user profiles. No question text, chat history, student name or extracted documents are stored. Study answers are bounded exact quotations, not generated explanations presented as facts.

## Workflow

1. Tenant admin publishes a licensed digital library `Document` with a specific chapter and language. The file must already be private, locally stored, verified safe and malware-clean, with a reviewed document type. A revoked, expired, superseded, unsafe or unlicensed document cannot provide evidence.
2. A teacher with a current active campus assignment may submit a short source-located passage for that campus's publication; tenant admin may submit for any institution publication. The submitted passage is `Pending` and never learner-visible.
3. A **different teacher** in the same campus, or tenant admin, may `Approve` or `Reject` it. Approval is an atomic status and audit write protected by SQL `rowversion`; exactly one decision wins. Tenant admin may later `Revoke` an approved excerpt; rows and audit history are retained. The unique filtered index prevents competing pending/approved passages at the same page/locator.
4. A learner with a current active campus enrollment, assigned teacher, campus-scoped principal or tenant admin selects a published, accessible chapter. `/api/ai/study/ask` checks the current tenant, role, campus membership, document, licence, file safety and approved evidence on the server. A question matching a short grounded source quote returns that quote plus book/chapter/locator citation. Otherwise `NoEvidence` or `Invalid`; the system never fabricates an answer.

## API and page

- Page: `/Library/StudyAssistant` (also linked from `/Library/Study`).
- Reader: `GET /api/ai/study/sources` (server-side search, page size up to 40); `POST /api/ai/study/ask` (300-character question limit).
- Reviewer: `GET /api/ai/study/author-sources`, `GET /api/ai/study/reviews/{sourceReference}`, `POST /api/ai/study/excerpts`, `POST /api/ai/study/excerpts/{reference}/review`.
- Cookie authentication, LIBRARY entitlement, explicit roles, API rate limiting, antiforgery on writes, no-store responses and output-as-text prevent unauthorized access, request forgery, uncontrolled throughput and HTML injection.
- Page is bilingual (English/Bangla), keyboard/label compatible and uses existing session-transition checks. C3 will supply voice controls; C2 does not introduce an unreviewed voice pipeline.

## Isolation, integrity and reporting

- All data access is tenant-filtered; the excerpt foreign key is `(TenantId, DocumentId) → Documents(TenantId, Id)`, preventing cross-tenant row attachment. Only current active memberships are consulted; institution-wide publications still require active campus membership for non-admin readers.
- No content is returned from a draft, rejected or revoked excerpt. A passage remains tied to the versioned `Document` so a new edition must obtain its own review. Changing the currently published licence immediately hides answers from that source.
- The database enforces legal states and uniqueness. Decision updates and `AuditLog` inserts share one `SaveChangesAsync` transaction; stale rowversion and replayed transitions return HTTP 409. Never hard-delete published evidence.
- Read paths are projected, capped and paged; the `(TenantId,DocumentId,StateCode,LanguageCode,Id)` index supports question retrieval while `(TenantId,DocumentId,SourceLocator)` protects duplicates. No unbounded chapter scans.

## QA / release

- Pure engine tests in `EduOS.Tests/AI/GroundedChapterStudyTests.cs` cover prompt injection, cross-tenant/campus/edition evidence, no-evidence hallucination protection, unsafe citations and bounded copyright quotes.
- Integration tests in `EduOS.Tests/Services/GroundedStudyAssistantServiceTests.cs` cover learner scope, licensing/file revocation, positive citation, no-evidence, model-off, independent teacher review and competing locator submissions.
- Apply migration `20261109120000_GroundedStudyEvidence` **after** prior migrations; it adds only one empty table, FKs, check and indexes. No backfill of unreviewed passages is safe: teachers must opt in and verify licensed sources. Previous document and reader APIs remain compatible.
- Before production: take database backup, apply migration, verify app boot and module entitlement, run tests and permissions matrix, monitor 403/404/409 outcomes and approved evidence counts. Rollback can remove the new table **only while empty**; if any review history exists the down migration intentionally refuses destructive rollback. Disable the C2 page/endpoint or feature entitlement for emergency rollback while preserving data.
- Human UAT, content licence/legal verification, accessibility review with assistive technology and production cutover are outside C2 implementation closure.
