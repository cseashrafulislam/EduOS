# Dynamic institute policy contract — owner decision (2026-10-31)

Applies to all future EduOS V1 maintenance, V2 integrations and V3 features. It is a design rule, not evidence that every existing module already conforms. Do not reopen frozen modules merely to apply cosmetic policy changes; implement it when a genuine business-policy dependency or owner-approved scope requires it.

## Non-negotiable behavior

1. **No fixed institute, student, enrolment, campus or workflow count.** Data and actual institute configuration drive the workflow. Technical safety budgets (batch size, file size, paging, precision, actual subscription quota and bounded-resource limits) are allowed, but are not product user-count assumptions.
2. **A business amount may use Automatic, Manual or Auto + Manual.** Automatic shows the calculation and basis. Manual replaces the suggested total. Auto + Manual adds/subtracts an explicitly entered correction to the automatic result. It must be impossible to submit both manual final and manual correction simultaneously.
3. **Per-institute/campus policy controls eligibility, not source-code constants.** A Manual-only policy must request an explicit approved amount; if Auto is unavailable, Auto + Manual cannot fabricate a base. The institute can permit/disable overrides through existing authorized configuration. A user may edit the suggested amount only if authorized.
4. **No silent financial mutations.** Manual or hybrid differences require a substantive reason, cent-precision and bounded signed range, tenant/campus authorization, an independently approved maker-checker action, a versioned source snapshot and immutable audit evidence. Approval and GL posting are different states. Never silently pay out cash.
5. **One Finance source of truth.** Reuse existing V1 `StudentInvoice`, `Journal`, `StudentPayment`, allocation and refund contracts. Any credit note must prove original invoice and payment lineage, preserve posted history, avoid duplicate credits, and reconcile paid versus unpaid portions. Do not invent a parallel ledger to make a UI checkbox appear complete.
6. **Professional UI.** A compact selection labelled Automatic / Manual Final / Auto + Manual, one conditional signed input and one conditional explanation. Display calculated amount, chosen amount, currency and basis; preserve server validation errors, access restrictions, bilingual/theme/accessibility standards. Don't expose raw entity IDs as the primary user selection where a safe record picker already exists.
7. **Release sequencing:** first finish the Finance-owned V2 settlement integration, then only remaining Hostel/Transport Operations work, then Final Acceptance. Do not redo completed LMS/Library or frozen V1 workflows.

## Current implemented V2 Finance increment

- Existing campus `DailyProrated` / `FixedMonthly` / `Manual` and `AllowManualOverride` settings remain authoritative.
- `ReservationQuoteSelection` accepts an optional `manualDelta` with signed-cent validation, checked sum, policy/permission gate and audit reason. It returns `Automatic`, `ManualOverride`, `AutoPlusManual` or `ManualRequired` as appropriate.
- Finance preflight, Hostel and Transport request/review APIs and screens use this selection, including the original automatic basis and a separately editable correction.
- For positive finance-cleared period adjustments, the existing automatically prepared invoice/journal flow is preserved; negative changes continue through manual posted-journal settlement pending the original-invoice credit-note architecture.

**Status:** Automatic/manual/hybrid calculation selection is implemented; it does **not** independently certify fully automated paid/unpaid credit settlement or all EduOS modules as complete.
