# Research & Accreditation — extension contract (2026-11-03)

This source-backed guide supersedes historical design-only Research statements in
`PROJECT_MASTER_PLAN.md`. Research and Accreditation are opt-in modules with live tenant,
campus and independent-role checks; existing proposal/ethics/grant/research-outcome/standards,
review panels, confidential files and reporting workflows remain unchanged.

## Configurable academic standards

- `AccreditationProgramOutcomes` is the immutable versioned source of program learning
  outcome statements. A new version uses the same `(program, code)` with a new version
  number; old mappings remain historically reproducible.
- Standard-to-outcome mapping is allowed **only while the containing framework is Draft**.
  Publishing freezes the standard and all its mappings. Creating a new framework
  version is the supported path for substantive revisions.
- Standards independently configure `RequiredReviewerCount` (1–3), `ValidityDays`,
  `RequiresOutcomeMapping` and `RequiresPublishedAcademicEvidence`. The latter two
  default to false for backward compatibility. Required evidence is enforced by the
  server within the independent approval transaction, not only by the form.
- An assessor can attach an existing, currently Published academic ResultPublication
  only if its batch and assessment belong to that same campus and program. The link
  records the publication version and publication timestamp; no marks are copied.
- The compliance gap report is scoped by campus and optional program, with a
  configurable renewal warning interval (1–365 days). States include Missing,
  Pending, Current, RenewSoon, Expired and EvidenceGap. A withdrawn academic result
  is no longer counted as currently published evidence.

## Research grant Finance reconciliation

- Grant Award/Amendment is an approved budget commitment, **not cash or a journal**.
  Source-of-truth amounts remain in canonical Finance `Journals` and `JournalLines`.
- A Research Coordinator configures each scheme's Finance account → `Disbursement`,
  `Expense` or `Refund` and its debit/credit normal direction. Accounts must be active,
  non-group, posting-enabled and in the grant scheme currency.
- A posted or reversed **Manual** journal can be linked to one grant once and only
  after a mapped ledger account is present; the link is immutable and retry-safe.
  Existing financial journals are not edited. Linked manual reversals contribute
  the signed opposite movement automatically. Ledger totals are recalculated from
  canonical posted/reversed journal lines, with no duplicate editable money fields.
- `AvailableBudget = ApprovedBudget - NetExpenses + NetRefunds` and
  `Undisbursed = ApprovedBudget - NetDisbursements + NetRefunds`. These are management
  reconciliation measures, not approval for payment or bank transfer.
- If an account classification must change after use, create a reviewed versioned
  scheme/policy and plan an audited migration; never silently rewrite historical
  journal allocations. Cross-grant journal splitting is not supported: one posted
  journal currently belongs to at most one Research grant.

## Deployment and acceptance

Apply additive `20261103190000_ResearchQualityExtensions` after prior Research/Accreditation
migrations; deploy server and bundled JS together. Back up and rehearse migrations
on a restored database; neither historic journal postings nor academic result records
are backfilled or modified. SQL regression tests cover independent review/mapping,
budget reconciliation and reversal; exact-commit CI results must be checked before
claiming Development acceptance. Real institute configuration, browser role/campus
checks, finance audit of flow/account classifications, migration rollback and capacity
tests remain distinct operational gates.
