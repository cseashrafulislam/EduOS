# Central Education Content Catalogue — Issue #125

## Canonical data and use

The existing `Book` owns title/author/publisher/ISBN/edition/category. The existing
`Document` owns each versioned digital publication, private `FileAsset` owns bytes,
and `CurriculumSubject` owns the validated curriculum/level/subject relationship.
No second content-file store, book catalogue, curriculum hierarchy or LMS quiz
engine is introduced. Optional `Document.Content*` columns carry version-specific
chapter/topic, language, source, rights holder, licence basis/evidence and the
canonical curriculum keys. Null metadata on pre-migration Documents is deliberate.

## Upload, approval and access

1. A tenant administrator uploads a scanned 5-MB-or-smaller private file and
   supplies provenance and rights. Institute-owned originals may have no public
   URL, but still require a rights holder and documented ownership. External
   source links must be HTTPS. Uploaded content is **Pending** and inactive.
2. A tenant administrator reviews rights and mapped academic scope, records a
   decision note, and explicitly approves or rejects. All state and audit writes
   commit atomically with rowversion validation. Review is a real server-side
   status transition, not a hidden UI button.
3. For a replacement, the old approved edition remains usable during review.
   Approval atomically retires the old version and activates its successor.
   Rejected files never become reader-visible. Version history remains attached
   to the immutable `SupersedesDocumentId` chain.
4. Every list, export and byte read applies tenant/campus/role/active-book,
   publication-review, expiry and private clean-file checks. Byte reads also
   verify SHA-256 and recheck authorization after storage access. PDF and safe
   images can be read inline; other file types use the existing download route.
5. **Owner UX correction (2026-11-12): Library → Digital Library is the sole catalogue and publication interface** for NCTB/official books, NTRCA, textbooks, reusable PDFs and approved learning resources. LMS Course Content does not render a second Library book list or upload another copy. Students/teachers browse the existing Library catalogue through its separately authorized navigation; LMS keeps course/lesson/quiz/assignment workflows and genuinely lesson-specific private worksheets. The library module's independent tenant/campus/role entitlements remain mandatory, and legacy Library book versions and access history remain intact. No library files are cached offline by this change.
6. Linked guardians/parents can read resources only through an active guardian
   record, a non-revoked student link, that student's current active enrollment,
   and the same campus licence. No guardian role alone grants tenant-wide access.
   Safe HTTPS source references open in a separate tab with `noopener noreferrer`;
   these links do not exempt an uploaded file from scanning, rights review or
   tenant-scoped authorization.

## Canonical official-book URL (owner decision 2026-11-12)

- **One page:** `/Library/OfficialBooks`. Its catalogue and official publisher-source links belong to Library, not LMS.
- Legacy `/Learning/OfficialBooks` requests use an authorized Library-controller redirect to `/Library/OfficialBooks`; it must never render a second page or bypass the `LIBRARY` module entitlement and role checks.
- New navigation links to the official-book directory appear only under Library. LMS maintains course and assignment features; do not expose a second official-book entry or fork the source directory.
- This is a route/navigation consolidation, not a deletion, licensing change, database migration or claim that third-party PDFs are copied into EduOS.

## Compatibility and rollout

- Migration `20261108223500_EducationDigitalContentReview` is additive: nullable
  columns, tenant-scoped optional FKs, and indexes; no historical rewrite or
  backfill guesses. Verify SQL smoke before rollout.
- Historical `LIBRARY_DIGITAL` Documents with `VerificationStatusCode=NotRequired`
  retain their existing access until separately audited. They **must not** be
  misrepresented as proven licensed. Administrators should reconcile their
  original legal authorization and withdraw any unlicensed content. New uploads
  cannot use the legacy unreviewed state.
- Existing publisher/file versions and campus licences are preserved. New
  submission responses now indicate **Pending**, not publicly published; update
  any external upload client relying on immediate download before rollout.
- Do not roll back the migration after metadata has been written without an
  explicit database backup/export: `Down()` drops the new evidence columns.
- Run feature-focused .NET tests, JS contract tests, SQL concurrent publish/
  review/replace tests, tenant and campus revocation checks, and real browser
  accessibility/mobile reading. Check exact-SHA CI and perform representative
  institutional UAT separately before production approval.

## Scope boundary

SSC/HSC/NTRCA study collections (#124), Teacher Studio/CPD (#129) and offline PWA
content sync (#127) remain separate backlog items. NTRCA is study content only.
