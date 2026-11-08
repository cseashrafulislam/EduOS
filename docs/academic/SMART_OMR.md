# Smart OMR — Issue #130, development start

**Scope:** An optional camera/image-assisted *review draft* for the existing Assessment module. `Assessment`, `AssessmentSubject`, `StudentSubjectRegistration`, `StudentAssessmentMark`, `AssessmentComponent` and their controlled mark review/publication rules remain authoritative. OMR must not create a second grading engine or overwrite a locked/published grade.

## Implemented first recognition boundary

`OmrBubbleRecognitionPolicy` classifies a **bounded, pre-aligned** answer grid of raw measured dark-pixel counts (4 or 5 options, 1–200 questions). It returns a read-only, deterministic suggestion per question: `Single`, `Blank`, `LowConfidence`, or `Ambiguous`. Thresholds require explicit calibration; all malformed, missing, duplicate, out-of-range or oversized measurements fail closed. It uses decimal ratios, no model/provider, no upload and no network access. **Even a wholly single-choice sheet remains `RequiresIndependentReview = true`.** No grade is calculated or posted, and this core cannot be presented as a complete photo scanner.

## Required next full-stack vertical slice

1. Authorize a tenant/campus/assessment-subject-scoped, versioned OMR answer-key/template source using existing academic/exam authority. One immutable template revision per issued form; printable student-safe ID and layout; no editable duplicate exam or mark facts.
2. Secure the original scanned image using existing private `FileAsset` storage with content sniffing, size, quota, retention, signed identity, tenant/campus/student/assessment enrollment checks and duplicate-content detection. Image extraction must validate fiducials, geometry, lighting, cropping and alignment before calling this classifier; poor scans need a visible recapture/manual path.
3. Persist suggestions and independent, auditable human corrections with image/template/version lineage. Enforce one reviewer transition, rowversion and retry idempotency; changes after approval use a new reviewed amendment. Never silently post a suggested option as a mark.
4. Only an authorized mark owner may translate an independently verified OMR answer sheet through the **existing** Assessment mark-entry/review API, respecting component full marks, locked/published results, roster/attempt rights and single-source accounting. Reconcile every answer, mark and correction; reject conflicting or duplicate uploads.
5. Add bilingual mobile review/camera capture, manual/help/accessible print, board-oriented PDF/Excel *preparation exports* only (no unverified direct Bangladesh board submission), normal/ambiguous/low-light/duplicate/tampered/cross-tenant tests and real SQL/browser end-to-end acceptance.

**Delivery status:** Recognition core + isolated unit tests implemented. Database/API/file/image/UI/report/posting workflows and real camera/board export remain **pending**, and Issue #130 must stay open. All future implementation goes to `Ashraful` and reuses central tenant security and existing File/Assessment services.
