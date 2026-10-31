# EduOS — Professional UI, Personalization, Localization and PWA Specification

**Product requirement (2026-10-27).** This document describes **target behavior**, not functionality verified as shipped. It complements, rather than replaces, the root README's existing bilingual/PWA baseline and the current Razor/application architecture.

## 1. Design principles

- Clean, modern and professional education ERP UI on desktop, tablet and mobile. Useful workflows take priority over decorative dashboards or unlimited custom-design controls.
- Consistent design tokens and reusable shell, buttons, inputs, selects, dialogs, tables, cards, statuses and empty/loading/error states.
- Clear field labels, required indicators, inline validation and accessible focus. No repeated explanatory notes, cluttered form footers or long text below each input.
- **One compact information icon in each page header** opens concise, context-aware page/field guidance on desktop hover or click and mobile tap; keyboard/screen-reader users can access the same help. Guidance must not obstruct the form or replace essential legal/error information.
- Appropriate defaults and dependent selects; never auto-derive financial or historical facts that could silently be wrong.

## 1.1. Non-intrusive help and truly clean screens

- Every operational page has at most **one small page-header info/help entry** for normal guidance. Never fill the form with repeated hints, instructional paragraphs, footnotes or decorative banners. Labels, required markers, genuine validation errors and legally essential explanations remain visible when necessary.
- Help is **user-initiated**, not an automatic overlay: desktop hover after a modest delay or explicit click; mobile tap; keyboard Enter/Space. It closes naturally on mouse leave, outside click, Escape or navigation and never reopens itself after every visit/save/field focus.
- Content is a compact page-specific field/action map: concise title, what the important controls mean, any genuinely surprising rule, and a short example only when helpful. It should fit a small popover or scroll-limited panel, never force a long full-screen manual or interrupt a task.
- Use ordinary inline validation *only when input is invalid*; guide text is not a substitute for useful field labels, correct defaults, server-side checks or safe error feedback.
- Optional first-run tours, if ever added, are strictly opt-in/dismissible and remember the dismissal. Avoid repeated auto tooltips, forced tutorials, sound, motion, flashing elements and confirmation popups for ordinary success.

## 1.2. Form, list, popup and report polish — common contract

| Surface | Expected professional behavior |
| --- | --- |
| Page header | Short meaningful title, compact help icon and only the highest-value contextual actions; no long introductory text or repeated breadcrumbs/cards. |
| Form | Related fields grouped logically, consistent alignment/width, defaults and dependent inputs; few clicks, clear required state, no permanent notes under every field. |
| List/table | Relevant columns, consistent status chips, search/filter/sort/page controls, actionable empty state, sticky essentials on large lists, responsive mobile view and permission-aware actions. |
| Dialog/popup | Open only for requested work, consequential confirmation or genuine blocking errors; concise heading/body/actions, appropriate sizing, no nested modal chains, keyboard focus management and mobile-fit layout. |
| Confirm/cancel | Name the actual consequence; confirm only destructive/high-impact operations. Unsaved edits require a targeted leave warning, not repetitive dialogs. Financial/result state transitions still require server approval gates. |
| Feedback | Quiet success toast/inline state rather than success modal; clear, accessible errors linked to affected fields; no noisy alerts for routine navigation. |
| Report | Compact title and filters, clear date/tenant/campus scope, readable totals and grouping, authorized PDF/Excel/Print actions, consistent A4/landscape print layout, pagination/async export for large histories and no misleading blank/zero totals. |
| Loading/empty/error | Stable skeleton or progress state, cancel/retry where safe, helpful empty state, clean permission-denied state and no infinite spinners or silently failed requests. |

All of the above reuse the same design system for admin, teacher, student and parent portals; changing role, institute, language, theme or device must not produce a visibly disconnected application. Quality review includes dashboard, create/edit/view, approval dialog, print/PDF, export, and error states—not only the default form screenshot.
## 2. Supported user controls

### Sidebar navigation implementation (2026-10-31)

The shared Razor sidebar now presents the existing **server-authorized** destinations as
compact, collapsible main/sub-menu groups, with Bangla/English group labels. The
current page's most-specific link is highlighted and its group expanded in left/right
navigation. A tab-only, non-sensitive `sessionStorage` entry remembers sidebar scroll
position and the last expanded group across page loads; neither permissions nor menu
destinations are persisted there. The mobile drawer retains native keyboard/summary
interaction, while top navigation uses scroll-limited overlay submenus.

The server remains the sole source of module-entitlement and role visibility, and
controllers continue enforcing actual access. Menu grouping never grants an
entitlement. Additional sidebar actions in the future must follow the same rule.

| Surface | Controlled, supported choices |
| --- | --- |
| Navigation | Left/sidebar, right/sidebar or top navigation; collapsed/expanded; favorites and allowed-menu ordering |
| Theme | Light, dark, system default, optional professional presets **and unrestricted validated HEX/RGB custom color selection** for supported semantic tokens; never rely on a fixed list of colors |
| Typography | Approved font families, bounded text size and spacing/density presets; readable Bangla and English |
| Workspace | Dashboard widget show/hide/rearrange within authorized widgets and responsive grid limits |
| Tables | Column visibility/order, density, safe page-size and default sorts; export only authorized columns/data |
| Forms | Comfortable/compact density and safe responsive layout, with required/security-critical fields preserved |
| Accessibility | Visible focus, contrast-compliant colors, reduced-motion support, keyboard interaction, touch target sizing |
| Language | Global `bn-BD` / `en-BD` choice, with system-wide navigation/forms/messages/reports localized where supported |
| Reset | Preview, apply, reset page preferences and reset all to effective institute defaults |

**Not a free-form page builder.** Users cannot upload arbitrary JavaScript/CSS/HTML, change business workflows, invent protected controls, bypass entitlement-based menus or hide mandatory form fields. Custom page arrangement is limited to explicit components and whitelisted capabilities.

## 3. Preference ownership and resolution

Preference scope is a *user within an institution membership* (and optionally device layout). Resolve defaults in this order: product-safe defaults -> authorized institute branding/theme -> user's non-sensitive overrides -> runtime accessibility/system constraints. TenantAdmin controls permitted institute-wide defaults; a tenant cannot alter another tenant's configuration. User preference overrides never change tenant identity or authorization.

Implementation design when explicitly approved:

- A validated, versioned user-tenant preference document or minimal relational fields; no arbitrary schema-per-user or per-page copies of CSS.
- Store only **deltas from defaults** (theme ID, palette token, navigation placement, density, widget IDs/order, table layout). Validate maximum payload, known keys, allowed values, content type and version on the server.
- Optimistic version checks avoid lost edits from two devices. Keep preference changes separate from transactional finance/academic tables.
- Resolve at authenticated shell load or changed version; safely cache per tenant + user + preference version with bounded TTL/invalidation. Do not query the database for every widget/field or every request.
- Bootstrap only non-sensitive theme tokens early to avoid flashing the wrong theme. A local fallback may improve perceived loading but does not authorize access or become the server source of truth.
- Missing/invalid/retired preferences fall back to a documented safe theme and preserve the ability to reset; no broken page or infinite migration loop.
- Maintain consistent API/DOM contract across UI options; database/API/report names remain invariant when the display language or menu arrangement changes.

**Owner approval (2026-11-01):** UI/UX modernization and Theme & Appearance Studio are approved for scoped implementation. First inspect the current shared shell, navigation, localization, preferences, Razor/JS/CSS and tests; reuse working code. A persistence change is allowed only when actually needed, through an additive, tenant-safe, audited migration. Do not create a parallel theme engine or overwrite existing preferences. This document is the canonical UI contract for both existing and newly created pages.

## 4. Localization and tenant terminology

- Keep canonical domain/API codes language-neutral. Translate display text, validation, menus, help, notices and reports without changing model names.
- A chosen language persists across navigation/login sessions according to security/privacy policy, not just within one page. Untranslated strings are tracked, not silently presented as a complete bilingual feature.
- Institute terminology is distinct from language: Level/Class/Semester, Section/Batch/Cohort, Teacher/Trainer, etc. must be driven by supported configuration rather than branching code by institution type.
- Support Bangla typography, numbers/date presentation and PDFs; never automatically change stored financial amount or business-date semantics when UI language changes.

## 5. Mobile and PWA

- Full operational tasks remain usable through responsive screens: admission, attendance, results, collection, LMS and authorized management, with appropriately simplified navigation.
- Installable app shell over HTTPS with offline/reconnect indication. **Do not cache authenticated HTML, API responses, fee/result/student records or uploaded tenant files in service-worker offline caches.** Sensitive writes require connectivity and confirmed server acknowledgement; never promise offline financial posting.
- Touch-accessible help icon, dialogs and editable controls; avoid hover-only UI. Validate smaller screens, low bandwidth, loading/error/empty states and safe session expiration.

## 6. Performance and isolation acceptance

- Render the shared shell and stylesheet bundle independently of each user's individual layout; use design tokens plus compact preference payloads, not newly generated per-user CSS bundles.
- UI customization must not cause N+1 database calls, global cache leaks or persistent session locks. Cache keys include user and tenant where values differ.
- Cross-tenant users with identical IDs, similar widget names or shared browsers must never receive each other's appearance data or menu permissions.
- Aim for web-vital and device targets defined in the scalability standard; confirm with actual mobile network and concurrent-user testing.
- Local user choice cannot reveal hidden modules, unowned dashboards, financial/student data or restricted exports.

## 7. Definition of done

1. User can select and save theme/navigation/density preferences, refresh or sign in on another device and get the same authorized experience.
2. Two users in the same institute may have different designs; the same person in two institutes has independently scoped preferences.
3. Changing Bangla/English updates shell, pages, validation and supported reports consistently; institution terminology remains correct.
4. The page-header help icon works via mouse, keyboard and mobile tap; forms remain uncluttered.
5. Narrow phones and large desktops support admission, attendance, result, payment and LMS workflows without horizontal form breakage.
6. Adversarial payloads, stale-version updates, tenant switching and unauthorized widget IDs fail safely.
7. Load tests show customization overhead remains bounded and causes no cross-user/cross-tenant cache leakage.


## 8. Owner-approved unified UI/UX modernization workstream (2026-11-01)

**Single UI owner:** EduOS Full Project Task 3/5 owns shared layout, discoverability, reusable components, theme/preferences, visual and form interaction refinements, localization presentation and UI regression. Domain owners (Tasks 1, 2 and 4) keep their actual business entities, APIs, transactional rules, reporting amounts and workflows; Task 5 owns independent whole-system integration, security and QA. A cross-domain API or schema fix requires evidence and owner coordination, not a second implementation. Development work is **not** complete merely because these requirements are documented.

### 8.1. Route and page inventory before redesign

- Build an evidence-backed route/page/menu map from the **current Ashraful HEAD**: route, controller/view/API, module, user action, role/module-entitlement, tenant/campus scope, current entrypoint, existing tests, mobile/localization/print dependencies, and observed issue. Mark verified existing, defect, missing navigation, needs workflow split, duplicate link or unverified; do not infer a missing feature from a screenshot.
- Preserve previously implemented collapsible server-authorized navigation and its existing left/right/top modes. Do not recreate it. Audit links embedded in page-top button rows; route discoverable authorized destinations through the sidebar or global feature search. Do not add every Add/Edit/Details action as a permanent menu item.
- Prefer **Module → Operations / Reports / Settings → Feature**, with up to three navigational levels. Omit a category when it only adds a useless click; keep module Overview as a direct destination. Use short bilingual labels, current-page highlighting, breadcrumb or context heading where useful, role-aware feature search, favorites/recent pages and stable deep links. Menu search is a discovery tool, **never** an authorization mechanism.
- Maintain one authoritative route/menu metadata source shared by navigation/search/breadcrumbs where practical, referencing existing server-side role/entitlement checks. No duplicate menu destinations, unauthorized deep-link access, or changes to route/API identity without a compatibility plan.

### 8.2. Task-focused page patterns — not one enormous form or one page per field

| Pattern | Required behavior |
| --- | --- |
| Overview/landing | Small summary and direct actions; no unrelated entry forms or giant link-button forest. |
| Master list + editor | Server-filtered list and Add/Edit (appropriate drawer or separate page), with edit/return context retained. |
| Transaction | One principal action, sensible grouped inputs, review/post/confirm where materially risky, idempotent server processing and meaningful completion action. |
| Profile/detail | One record context with related tabs/sections and explicit permission boundaries. |
| Reports | Scoped filters, SQL-backed paging/sorting where growing, accurate totals, authorized export/print and readable empty results. |
| Settings | Coherent policy/configuration sections; don't mix business setup with daily transaction forms. |

**Fees & Billing exemplar:** Fee Structures (register + editor), Monthly Invoice Generation, Student Ledger, Fee Collection, Discounts/GL Setup and Collection/Due/Aging Reports are separate discoverable workflows. Keep existing financial endpoint semantics, preview/idempotency, posting and reversal contracts. Do not invent a duplicate fee ledger or GL.

### 8.3. Appearance Studio without hardcoded color choices

- Provide **Basic** (preset theme, Light/Dark/System, primary brand color, sidebar/header mode, density) and **Advanced** sections (color tokens, layout/typography/accessibility and authorized institute branding) without cluttering ordinary operational pages.
- A custom color picker must accept user-selected valid colors rather than only fixed preset swatches. Expose semantic groups: sidebar background/text/icon/active/hover, header background/text/icon/border, page/card/table surfaces and text, primary/secondary button, form border/focus and status colors. Preserve contrast-compliant success/warning/error meaning; do not let cosmetic overrides falsely communicate financial status.
- Normalize/validate allowed RGB or HEX values, calculate or suggest readable foreground/hover states, preview light/dark and representative forms/tables/dialogs and block unsafe contrast combinations. Semantic design tokens are authoritative; no arbitrary CSS/JS/HTML uploads or unlimited per-control CSS persisted.
- Preview is transient and not globally published; user Save persists scoped preferences; institute default publication requires authorized admin, version check, audit and safe reset/restore. Resolve effective values from safe product defaults, institute settings, user override and accessibility constraint without tenant/user bleed. Existing print/PDF templates and official invoice identity must not accidentally inherit personal visual themes.
- Honor existing density, layout, typography, localization, reduced motion and mobile controls. Do not add duplicate theme/settings tables if existing storage can be extended safely.

### 8.4. Efficient data entry and lists

- Searchable, tenant-scoped selects and dependent campus/academic/student lookup; retain known record context and reduce duplicate typing **without silent financial or historical auto-entry**.
- Standardize keyboard/touch focus, inline field and summary validation, Save/Save & New when appropriate, unsaved-change warning, stable request loading/error state, field feedback and quiet success.
- Don't use complex nested modals or auto-show tutorials. Large forms may use a clear section index/wizard only when it materially reduces errors; short forms stay on one view.
- Tables: bounded server-side filter/sort/page/projection, consistent row actions, useful empty/loading/error states, authorized column visibility and export, mobile behavior; never fetch unbounded tenant data merely to power a rich grid.
- Role-targeted dashboards show useful work/alerts and authorized figures, not placeholder KPIs. Preserve business-date, currency and bilingual formatting and report-source accuracy.

### 8.5. New-page Definition of Ready / Done (mandatory, all future tracks)

**Before coding:** inspect the relevant shared UI component and the current route/menu registry; classify the page as Operation, Report, Settings or record detail; identify permissions, tenant/campus and module entitlement, localization keys, existing endpoint/reuse and required list/report state. The domain owner must not create a disconnected one-off UI.

**Before claiming done:** (1) proper discoverability via navigation/search or contextual parent link; (2) consistent tokens/forms/buttons/tables/help; (3) bilingual display labels (no raw enum/entity key such as academicBatches); (4) keyboard/touch and mobile layout; (5) readable empty/loading/error/permission-denied states; (6) safe theme and density behavior; (7) server authorization, CSRF, validation and duplicate-save protection; (8) related list/edit/back filters and print/report permissions; (9) targeted component/browser/API-contract/security regressions; (10) updated route inventory and master-plan evidence. All checks must be recorded as **tested**, **not run** or **blocked**, never assumed passed.

### 8.6. High-throughput, low-duplication execution sequence

1. Inventory existing route/sidebar/theme/form assets and reproduce high-impact problems; take a small baseline of representative screens, roles and devices.
2. Repair shared navigation/search and the design token/component foundation **once**, with regression tests and compatibility.
3. Pilot with frequent flows (Admission, Student, Fees/Collection, Attendance, Payroll), including the currently overloaded Fees & Billing screen, then reuse the proven patterns across remaining modules in bounded coherent batches.
4. Extend Theme & Appearance Studio only after checking existing preference handling; verify personal/institute scope, color safety, reset and mobile/bilingual.
5. Continue module-by-module and finish independent role/tenant, accessibility, mobile, report and performance acceptance. Never claim a screenshot-only polish as business-flow completion.

**Acceptance reporting per run:** exact old/new Ashraful SHA; audit finding and owned route/template; files and explicit behavior changed; tests/CI status for that SHA; unverified UI/browser or institute acceptance gates; next high-value non-duplicate slice. Respect owner-visible task scheduling; this spec does not reschedule automations.
