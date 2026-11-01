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
- **Page Information is the page's built-in user manual, not merely a tooltip.** Desktop hover/focus may show a short summary; explicit click (or keyboard activation/mobile tap) opens a responsive, scrollable **Help Drawer** with the complete localized page guide defined in §9. Keep the single compact header icon; never dump the manual below forms, interrupt entry automatically, or use nested modal chains. Long content belongs inside the on-demand drawer, not a forced full-screen takeover.
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
3. Changing Bangla/English updates the entire authorized experience, including login, sidebar/navigation/search, headers, actions, forms, validation/toasts, reports/exports, and every Page Information user-manual section and example. Locale parity is verified; untranslated content is a tracked defect, not hidden by a false completed flag.
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

**Before claiming done:** (1) proper discoverability via navigation/search or contextual parent link; (2) consistent tokens/forms/buttons/tables and **the bilingual, example-backed Page Information manual from §9**; (3) bilingual display labels (no raw enum/entity key such as academicBatches); (4) keyboard/touch and mobile layout; (5) readable empty/loading/error/permission-denied states; (6) safe theme and density behavior; (7) server authorization, CSRF, validation and duplicate-save protection; (8) related list/edit/back filters and print/report permissions; (9) targeted component/browser/API-contract/security regressions; (10) updated route inventory and master-plan evidence. All checks must be recorded as **tested**, **not run** or **blocked**, never assumed passed.

### 8.6. High-throughput, low-duplication execution sequence

1. Inventory existing route/sidebar/theme/form assets and reproduce high-impact problems; take a small baseline of representative screens, roles and devices.
2. Repair shared navigation/search and the design token/component foundation **once**, with regression tests and compatibility.
3. Pilot with frequent flows (Admission, Student, Fees/Collection, Attendance, Payroll), including the currently overloaded Fees & Billing screen, then reuse the proven patterns across remaining modules in bounded coherent batches.
4. Extend Theme & Appearance Studio only after checking existing preference handling; verify personal/institute scope, color safety, reset and mobile/bilingual.
5. Continue module-by-module and finish independent role/tenant, accessibility, mobile, report and performance acceptance. Never claim a screenshot-only polish as business-flow completion.

**Acceptance reporting per run:** exact old/new Ashraful SHA; audit finding and owned route/template; files and explicit behavior changed; tests/CI status for that SHA; unverified UI/browser or institute acceptance gates; next high-value non-duplicate slice. Respect owner-visible task scheduling; this spec does not reschedule automations.

## 9. Page Information as a complete, bilingual, embedded user manual (owner change 2026-11-01)

The one small **Info** icon at each page header is the *entry point* to that page's complete operational guide. It must actually explain how to finish the work, with examples, rather than displaying a generic one-line purpose or listing field names. This replaces the previous short-only guidance requirement **inside the opened panel**, while preserving uncluttered screens and user-initiated help. The goal is that ordinary institute users can complete their daily work without needing a separate PDF/manual.

### 9.1. Interaction and content contract

- Desktop: hover/focus may show a **one- or two-sentence quick summary**; click, Enter or Space opens the full Help Drawer. Mobile: tap opens the same accessible drawer. Support Escape/close, focus return, a visible heading, scroll, correct RTL/LTR-neutral Bangla/English layout, readable width and no obstruction to Save/Cancel. Never auto-open after every page visit, tenant switch or save.
- Each page guide has stable **page/route identity**, title, purpose (what this page actually does), when to use it, permitted roles/required setup, step-by-step instructions in the **same order as the real UI**, field/action explanations for non-obvious controls, **one realistic fictional worked example**, expected result/where to verify it, important business rules/approvals and common error/recovery cases. Add related-page links only if actually authorized and route-resolved.
- Distinguish **Create/Edit/Approve/Cancel/Post/Reverse/Export** operations and high-risk consequences where the page supports them. Do not claim a button exists or an invoice/payment was posted unless verified from actual route, controller and current state. Guidance must not promise absent or Under Construction behavior.
- Reuse a **single centrally maintained, localized help-content contract** keyed to real routes/features with versioning/review status; no copied hardcoded long HTML inside each view and no generic AI-generated text published without verification. Use the existing localization infrastructure when possible; do not invent a second localization or markdown renderer merely for help.
- Each substantive UI/business workflow change must update its affected guide and both language variants in the same reviewed change. Unsupported/new features are clearly flagged, and missing translation keys are tested; manual content is versioned with the feature, not a detached stale document.
- Keep page text concise. The longer instructions and examples are **on demand inside Help Drawer**. Real validation, safety disclosures and mandatory instructions remain visible at the point of action, so help is not used to hide critical information.

### 9.2. Standard manual structure for every operational page

1. **Page overview / পেজ পরিচিতি:** task and outcome in plain language.
2. **Before you start / শুরুর আগে:** required permissions, setup, dependencies and safe prerequisites.
3. **How to use / কীভাবে করবেন:** numbered accurate steps matching buttons and screen order, including Edit/Cancel/Approval where supported.
4. **Example / উদাহরণ:** fictional but representative values, calculation/context and expected output; never expose actual student/guardian/finance personal information.
5. **Important rules / গুরুত্বপূর্ণ নিয়ম:** permission, immutable postings, duplicate prevention, cutoff, retries and what can be corrected/reversed.
6. **If something goes wrong / সমস্যা হলে:** known validation messages, missing setup, permission denied and safe recovery.
7. **Related workflows / সংশ্লিষ্ট কাজ:** links only to existing, authorized routes.

**Example — Generate Monthly Student Invoices (illustrative, not a claim about live data):** select the permitted Campus and Batch, choose November 2026 and an active fee structure, preview eligible students and totals, review exemptions/existing invoices, then confirm Generate. Expected result: created invoices appear in the authorized student ledger; repeated requests must not create duplicates. Explain why the Generate action may be disabled (no active fee structure, permission/period validation or conflicting invoice). The English and Bangla guides cover the same behavior with localized field/button names.

### 9.3. Entire-product bilingual parity — not just a page toggle

- The global language choice **bn-BD / en-BD** must apply coherently to login/auth/error pages, header, user menu, module/submenu/sub-submenu, global feature search and breadcrumbs, page headings/help/manual/examples, tabs, filters, form labels/placeholders/actions, validation, toasts/dialogs, empty/loading/error states, notification text and supported reports/exports/print headings. Changing language should update the current view immediately where safely supported, otherwise preserve choice and switch consistently on navigation; do not silently reset on a new route.
- Keep canonical API field names, IDs, status codes and SQL data unchanged. Map user-facing labels/enum descriptions through one translation catalogue and approved institute terminology (Class/Level/Semester etc.) so bilingual output is readable and consistent. No raw technical keys (e.g. academicBatches) or mixed-language leftovers marked complete.
- A selected language follows the authenticated user/institute preference across pages and sessions; tenant switching respects scoped preferences. Avoid shared-cache leakage, unauthorized menu discovery and stale language after login/logout. Guest/login surfaces can use a safe locale choice without exposing private preferences.
- Reports, A4/PDF/Excel: localize titles, column headings, legends and messages where supported while preserving financial amounts, business dates, source filters, schema, signed approvals and official document identity. Font rendering must actually support Bangla and English; track unsupported formats explicitly.
- Build **automated translation-key coverage + representative UI/browser assertions** for each major page type and role: route, menu and help key exist in both languages; toggle persists; no forbidden raw keys; unauthorized user cannot retrieve a private guide/route; no XSS from manual examples. Test mobile keyboard/focus and text overflow in both languages.

### 9.4. Delivery and status

Task 3 owns shared Help Drawer component, localization catalogue/parity tooling and systematic retrofit of existing page guides. Each business-domain owner supplies the verified steps, preconditions, approval/financial rules and worked examples for its newly created/changed pages; Task 3 integrates the common UI and no other task creates a parallel guide component. Task 5 independently checks bilingual/help completeness and authorization. Start with highest-usage Admissions, Students, Fee Collection/Invoice Generation, Attendance and Payroll, then inventory and complete remaining routes in reviewable batches. Record **verified guide coverage by route and language**, not estimated “all pages done” claims.

### 8.7. Evidence-backed navigation inventory — initial shared-shell slice (2026-11-01)

**Inspected baseline:** Ashraful `c0f5d8c` shared `_Layout.cshtml`, `sidebar-navigation.js`, `site.css`, `WorkspacePreferencesController`, and `WorkspacePreferences/Index`. The existing 2026-10-31 collapsible sidebar, server-side role/optional-module checks, active selection and top/left/right layouts were retained. The table records **navigation evidence**, not independent controller/API/IDOR or live-browser certification.

| Existing destination / action | Server-rendered entry and visible audience | Inventory state / next audit |
| --- | --- | --- |
| `/Admissions/Index` — admission operations | Academics → Admissions & Students; TenantAdmin or AdmissionOfficer | Existing route and authorized menu entry verified; form/list/mobile flow not re-audited |
| `/Students/Index` — student register | Same group and roles | Existing route/menu verified; editor return-context and duplicated page actions pending audit |
| `/Attendance/Index` — attendance operations | Academics → Classes & Attendance; academic operations role gate | Existing route/menu verified; keyboard/mobile data-entry pilot pending |
| `/FeeOperations/Index` — fees/billing | Management → Fees & Accounting; TenantAdmin, Principal or Accountant | Existing route/menu verified; overloaded-workflow split requires focused source/contract review, not assumed broken |
| `/PayrollOperations/Index` — payroll | Management → HR & Payroll; TenantAdmin, Principal, HR, Accountant, Staff or Teacher | Existing route/menu verified; role-specific child actions and mobile audit pending |
| `/Account/Profile` — personal profile | Authenticated account dropdown | Existing destination; now included in discovery from the same server-rendered account link |
| `/workspace` — personal appearance | Authenticated non-SuperAdmin account dropdown; existing `[Authorize]` preferences controller, session-bound GET and antiforgery-protected writes | Existing appearance page/persistence verified as present; **advanced custom semantic color editing is not implemented by this search slice** |
| Optional LMS/Library/Hostel/Transport routes | Existing `OptionalModuleNavigation` and `V2RolePortalNavigation` in Razor | Search consumes only links actually rendered for the current session; detailed child-route completeness not yet audited |
| Platform administration routes | SuperAdmin-only sidebar | Existing privileged menu; no client-generated privileged destinations |

**Delivered shared navigation delta:** Before this slice, authorized users had to browse sidebar groups or account menus; there was no global feature finder in the inspected shell. The new topbar search dialog (`Ctrl/⌘+K`) indexes the **current server-rendered authorized sidebar and explicitly marked account links**, de-duplicates destinations, searches feature/group labels, offers keyboard/touch accessible favorites and recent shortcuts, and preserves deep links. The source is the rendered navigation, not a client-side entitlement list. Favorites/recent paths are intentionally **tab-session-only**, with tenant/session identity checks and current-menu intersection; query strings, private record data and labels are not persisted. No new API, database, menu permission or stock/Finance behavior is introduced.

**Scope and verification ledger:** `EduOS.Tests/JavaScript/feature-discovery.test.cjs` covers safe relative routes, deduplication, search, bounded shortcuts, session/tenant isolation, account-menu discoverability, bilingual shell, keyboard and theme selectors. Test file added; exact-SHA CI, actual authenticated multi-role browser navigation, screen-reader/mobile interaction, institute UAT and full route coverage must be recorded separately from code existence. Next non-overlapping slice: inspect and pilot the Fees & Billing route/action hierarchy with its existing authorized APIs, or extend the existing preference contract for validated custom semantic colors after persistence/contrast review. Never infer page completion from this inventory alone.


### 9.5. Shared Help Drawer pilot — reviewed bilingual route inventory (2026-11-01)

**Source inspection:** Ashraful `c3754a0a0e2d`; existing `page-context-help.js` and shared topbar popover, role-gated controllers, Razor views and operational JS for Fees, Students, Attendance and Payroll. The prior popover auto-opened on pointer hover and exposed only a short summary. It was replaced **in place** by one click/keyboard/tap-activated native-dialog Help Drawer. The existing server culture selection (`en-BD`/`bn-BD`) selects all sections; no second localization service or endpoint was added. The drawer uses safe DOM `textContent` rather than untrusted HTML, and related links are intersected with the current server-rendered authorized menu. Unknown routes explicitly report **guide not verified**; generic snippets are not counted as complete manuals.

| Audited route | Actual controller role gate | Verified guide en-BD | Verified guide bn-BD | Existing page Info |
| --- | --- | --- | --- | --- |
| `/FeeOperations/Index` | TenantAdmin, Principal, Accountant | Reviewed v1 | Reviewed v1 | Shared header; legacy mini-popover removed |
| `/Students/Index` | TenantAdmin, AdmissionOfficer | Reviewed v1 | Reviewed v1 | Shared header; legacy student popover removed |
| `/Attendance/Index` | TenantAdmin, Principal, VicePrincipal, Teacher | Reviewed v1 | Reviewed v1 | Shared header; legacy mini-popover removed |
| `/PayrollOperations/Index` | TenantAdmin, Principal, HR, Accountant, Staff, Teacher (run management TenantAdmin only) | Reviewed v1 | Reviewed v1 | Shared header; section-specific cancellation/request notes remain contextual |

**Coverage denominator:** 4 inspected operational routes; 4/4 reviewed English guides and 4/4 reviewed Bangla guides in this pilot. This is **not** whole-project route or translation coverage. Both languages include purpose, prerequisites, numbered UI steps, fictional example, expected result, financial/approval rules, recovery and authorized related routes. The source is versioned and validated by `page-context-help.test.cjs`. Missing/unsupported routes do not receive invented instructions. No finance/attendance/payroll endpoints or data changed.

**QA boundary:** Automated catalogue/route/parity/DOM contract checks are included in the existing Node CI test glob. Real authenticated role/mobile/screen-reader browser QA, report/export localization and full site translation coverage remain **NOT RUN / NOT COMPLETE** until independently evidenced. Next reviewed batches: Admission applications, invoice GL posting and high-traffic list/detail routes, after their actual actions and authorization are inspected. Keep the one shared renderer; do not create a parallel page help engine.

### 9.6. Facilities work-order route manual and authorized discovery (2026-11-01)

The existing shared `page-context-help.js` catalogue now includes reviewed `/FacilityWorkOrders/Index` content in en-BD and bn-BD, covering submit, triage, independent approval, technician schedule/start/pause/complete, independent verification or reasoned return for correction, fictional example, expected history, role/409 recovery and the explicit absence of Inventory/Finance posting. The same shared on-demand drawer renders it; no new renderer or route is created. FACILITIES remains separately entitled and pilot-excluded; the existing navigation only shows its Work Orders and Facility Spaces links when the current tenant is entitled and the role is authorized. At exact code SHA [6d79b81](https://github.com/cseashrafulislam/EduOS/commit/6d79b81b04ce97a25b232ff96361d55e7260b086), [CI #41237445295](https://github.com/cseashrafulislam/EduOS/actions/runs/41237445295) **PASS** validates **5 inspected routes, 5 English and 5 Bangla manuals**. [SQL #41237445341](https://github.com/cseashrafulislam/EduOS/actions/runs/41237445341) **PASS** validates the Facilities return/correction workflow on disposable SQL. This is not whole-site localization certification. Actual authenticated multi-role/mobile/browser, keyboard/screen-reader and report/export parity remain **NOT RUN** until evidenced.


The same reviewed Facilities manual now also documents unassigned Approved → manager cancellation request → independent approver approval/rejection, reason limits, scheduled-release prerequisite and terminal cancellation. Both language variants and the existing shared drawer are updated with the workflow; no duplicate help component is introduced. Cancellation implementation [38c8dd7](https://github.com/cseashrafulislam/EduOS/commit/38c8dd7bdf5eaa2cb281e1854be4417c44c88111) and nullable-flow correction [b120f95](https://github.com/cseashrafulislam/EduOS/commit/b120f95804077626fad9d41224d441308b00370d) passed exact-code-SHA [SQL #41242209548](https://github.com/cseashrafulislam/EduOS/actions/runs/41242209548) and descendant QA-only [CI #41242404955](https://github.com/cseashrafulislam/EduOS/actions/runs/41242404955). Automated bilingual/manual and contract tests passed; real authenticated mobile/browser/screen-reader UAT remains NOT RUN.

### 9.7. Independent route/role and Page Information acceptance (2026-11-01)

**Independent QA parser gate:** [code `7d72c2c`](https://github.com/cseashrafulislam/EduOS/commit/7d72c2cb36afd22d55a46713e8fbac347ad29e43) corrects one unescaped apostrophe in the existing bilingual Facilities Help Drawer catalogue after [CI #41263556128](https://github.com/cseashrafulislam/EduOS/actions/runs/41263556128) failed with `SyntaxError` at `page-context-help.js:374`. The same shared renderer, verified guide structure, route permissions and both locales remain unchanged. [Exact-code-SHA CI #41263970770](https://github.com/cseashrafulislam/EduOS/actions/runs/41263970770) **PASS** and [disposable SQL #41263970914](https://github.com/cseashrafulislam/EduOS/actions/runs/41263970914) **PASS**. Task 3 retains shared catalogue ownership; this narrowly repaired a whole-project CI blocker. Real authenticated mobile/tenant-switch/screen-reader and complete route parity remain **NOT RUN / NOT COMPLETE**.

**Code SHA:** [`8a1a78c`](https://github.com/cseashrafulislam/EduOS/commit/8a1a78c6d048e49b85ec96865493feb2a6b77e4a). [Exact-SHA Application CI](https://github.com/cseashrafulislam/EduOS/actions/runs/41238043210) **PASS**; [disposable SQL Smoke](https://github.com/cseashrafulislam/EduOS/actions/runs/41238043154) **PASS**. Independent test: `EduOS.Tests/JavaScript/page-manual-independent-acceptance.test.cjs`. This is a QA-only change, not a new Help Drawer implementation.

**Verified reviewed-route matrix (both en-BD and bn-BD):** `/FeeOperations/Index` (FINANCE; TenantAdmin/Principal/Accountant); `/Students/Index` (STUDENT; TenantAdmin/AdmissionOfficer); `/Attendance/Index` (ATTENDANCE; TenantAdmin/Principal/VicePrincipal/Teacher); `/PayrollOperations/Index` (PAYROLL; TenantAdmin/Principal/HR/Accountant/Staff/Teacher); `/FacilityWorkOrders/Index` (FACILITIES; TenantAdmin/Principal/FacilitiesManager/FacilitiesApprover/FacilitiesTechnician/FacilitiesRequester). Tests inspect actual controller authorization attributes and execute the shared drawer in a DOM simulation. They assert click-only rendering, bilingual title/manual, close/focus return, fallback Escape, allowed related routes only and no private manual for unknown paths. The reviewed **5/5** is a pilot denominator, not all-route coverage.

**Confirmed existing Index routes without a reviewed manual in this pilot:** `/Admissions/Index`, `/StudentInvoicePosting/Index`, `/FeeReceivablesAging/Index`, `/StudentLifecycle/Index`, `/FacilitySpaces/Index`, `/PayrollJournalPosting/Index`, `/PayrollSalaryStatement/Index`. These are route IDs for Task 3 and the respective domain content owners to review; do not publish guessed instructions. Locale cookie, antiforgery and local redirect have source-contract assertions, but actual multi-tenant locale persistence, browser/mobile/screen-reader interaction, complete translation-key parity across all routes and report/export localization remain **NOT RUN / NOT COMPLETE**. Task 5 will independently verify follow-up coverage without taking over shared shell/theme development.

**Independent QA expansion (code SHA [`078cf59`](https://github.com/cseashrafulislam/EduOS/commit/078cf59bc6498411405ceccf62a25433ef985bbf)):** the separate Task 5 route/role matrix now covers **all 11 currently reviewed guide routes** in both en-BD and bn-BD, including Alumni, Fee Receivables Aging, Payroll Journal Posting and Payroll Salary Statement. It checks the existing controller role/module requirements (including FINANCE+PAYROLL), sensitive API role/CSRF and bounded CSV contracts, bilingual manual sections and the existing shared drawer's authorized-related-link filtering, close/focus restoration. Local source/DOM simulation **8/8 PASS**; [exact-SHA Application CI #41247407312](https://github.com/cseashrafulislam/EduOS/actions/runs/41247407312) **PASS**; [SQL #41247407342](https://github.com/cseashrafulislam/EduOS/actions/runs/41247407342) **PASS** (fresh/repeat SQL migrations and disposable backup/restore). Existing `/StudentLifecycle/Index` and `/FacilitySpaces/Index` still have no reviewed catalogue guide. This is a reviewed-catalogue denominator, **not all application routes**; authenticated tenant-switch language persistence, actual mobile/browser/screen-reader, full form/report/CSV translation and institutional UAT are **NOT RUN**. No shared UI, domain logic, migration or tenant data was changed.

**Independent workspace cross-boundary QA extension (code SHA [`4d31cf2`](https://github.com/cseashrafulislam/EduOS/commit/4d31cf2c5ed8714f9f5b83965cd20a4c927b34ba); [exact-SHA Application CI PASS](https://github.com/cseashrafulislam/EduOS/actions/runs/41251487287), [disposable SQL PASS](https://github.com/cseashrafulislam/EduOS/actions/runs/41251487316)):** `/workspace` joins the independently reviewed matrix as the twelfth route, with both en-BD/bn-BD full guides, on-demand drawer/focus and permission-filtered related links, controller authorization/CSRF, tenant+session-bound appearance reads and serializable writes, user/tenant preference lookup, and TenantAdmin-only table controls. This checks source and simulated DOM contracts; it does **not** prove actual authenticated mobile/tenant-switch locale persistence or production security. `/StudentLifecycle/Index` and `/FacilitySpaces/Index` remain uncovered.

### 9.8. Reviewed Admission and institutional invoice-posting manuals (2026-11-01)

**Baseline:** Ashraful `cb9dec35cece7a2ffb9ad655ba0b5d17f9747c32`. This shared-UI change extends the existing route-keyed `page-context-help.js` catalogue, not the renderer or finance/admission APIs. Reviewed actual `AdmissionsController`, `AdmissionApplicationsController`, `AdmissionIntakeController`, `Admissions/Index.cshtml`, `admissions/index.js`, `StudentInvoicePostingController` (page and API), `StudentInvoicePosting/Index.cshtml`, `finance/student-invoice-posting.js` and the canonical Finance posting service before writing instructions.

| Reviewed route | Actual access | en-BD | bn-BD | Documented actions and boundaries |
| --- | --- | --- | --- | --- |
| `/Admissions/Index` | ADMISSION; TenantAdmin/AdmissionOfficer | Reviewed v1 | Reviewed v1 | Draft/publish intake, published-form submission, guardian requirement, search/review, verified offer/acceptance before eligible enrollment; idempotent create and rowversion recovery |
| `/StudentInvoicePosting/Index` | FINANCE; TenantAdmin/Principal/Accountant read, TenantAdmin/Principal post | Reviewed v1 | Reviewed v1 | Search, open-period selection, eligible BDT invoice posting, immutable canonical journal; explicitly no cash receipt or Platform SaaS posting |

**Coverage:** 7 inspected reviewed routes, 7 English and 7 Bangla manuals, with matched step counts, safe text rendering and authorized related-link filtering. `EduOS.Tests/JavaScript/page-context-help.test.cjs` now asserts both new manuals against actual Razor/controller/API/JS contracts and translation parity. The earlier five-route independent QA fixture is preserved for Task 5. This is **not** the whole route denominator. UI-wide bilingual parity remains incomplete: these existing Admission and Invoice Posting pages still contain mixed-language/raw labels; do not call the pages fully localized based on their manuals. Authenticated browser/mobile/screen-reader, institute UAT, report/print localization and production go/no-go are **NOT RUN** here. Exact new commit and CI status must be read from GitHub, not inferred from this plan.

**Independent whole-system QA follow-up:** [`3777efad`](https://github.com/cseashrafulislam/EduOS/commit/3777efad94f89ffe63506a11a32df904f256566c) extends `page-manual-independent-acceptance.test.cjs` to all **7 reviewed routes in both languages**, explicitly checks actual Admissions and Student Invoice GL controller role/module gates, detects missing/untranslated manual sections and simulated unauthorized related-link exposure, and verifies drawer close/focus behavior. Local source/DOM simulation **7/7 PASS**; [exact-SHA CI #41242404955](https://github.com/cseashrafulislam/EduOS/actions/runs/41242404955) **PASS**; [SQL #41242404946](https://github.com/cseashrafulislam/EduOS/actions/runs/41242404946) **PASS** (fresh/repeat SQL migrations, 173 combined SQL tests, other targeted SQL suites and disposable backup/restore). Five route IDs remain uncovered as recorded in the unified master plan; this is not authenticated mobile/browser, tenant-switch locale persistence or full-site localization certification.


### 9.9. Alumni contextual manual — domain-authored addition (2026-11-01)

The education domain reviewed the existing `/Alumni/Index` controller, ALUMNI entitlement, optional-module navigation, Razor controls, client JavaScript, consent service, event/RSVP service, attendance service and CSV API. At [code SHA `2071d8cc`](https://github.com/cseashrafulislam/EduOS/commit/2071d8cc90dc1aae1081cddae76779436153baa9) it added **one route entry** to the existing `page-context-help.js` catalogue; the shared Task 3 renderer, shell, theme, authorization and navigation were not redesigned. Both en-BD and bn-BD guides have six matching numbered steps, role prerequisites, a fictional reunion example, consent revocation, independent event publication, RSVP/check-in/undo, bounded CSV rules, duplicate/409 recovery and a related Student Directory route that the renderer exposes **only when present in the authorized menu**. No outbound invitation is claimed or enabled.

The reviewed catalogue denominator becomes **8 routes / 8 English / 8 Bangla** under `page-context-help.test.cjs`, including new source-contract assertions for the Alumni UI, API, consent and attendance. Pre-push JavaScript syntax and guide-structure validation **PASS**; [exact-SHA CI](https://github.com/cseashrafulislam/EduOS/actions/runs/41244267321) and [SQL smoke](https://github.com/cseashrafulislam/EduOS/actions/runs/41244267330) are linked for independent status verification. This is not a claim that the whole Alumni page, report headings or whole-site shell has full bilingual parity. Task 5's independent **seven-route** acceptance remains a separate historical pilot until expanded. Real authenticated multi-role/mobile/screen-reader browser, campus-grant policy acceptance, institution UAT, load and production go/no-go remain **NOT RUN**.


**Alumni export locale follow-up (2026-11-01; verified code SHA `32bc8e2`):** The domain owner localized only the existing Alumni directory/engagement CSV presentation headers and event status labels to en-BD/bn-BD using the server-resolved UI culture. UTC timestamps and canonical API statuses remain stable; the reviewed Alumni Help Drawer result in both languages explains this. Focused C# and JavaScript contract assertions passed in [exact-SHA CI #41248836275](https://github.com/cseashrafulislam/EduOS/actions/runs/41248836275); [SQL #41248836263](https://github.com/cseashrafulislam/EduOS/actions/runs/41248836263) **PASS** with fresh/repeat migrations and Alumni real-SQL. This is **not** full application/export translation parity or real spreadsheet-client/mobile browser UAT.


**Alumni event/attendance request-order acceptance (2026-11-01):** At [code `f99e2b8`](https://github.com/cseashrafulislam/EduOS/commit/f99e2b8177cea4cd6f8beaf6f75b71fef0eff78c), the existing `/Alumni/Index` event and attendance views now invalidate prior actionable cards, page controls and CSV links during refresh and ignore late responses to superseded pages or detached attendee rosters. Bilingual loading/error/retry states use the existing page components and reviewed shared Help Drawer; no second renderer, new route or speculative theme change. Two JavaScript behavioral regressions verify out-of-order responses and no re-exposure of old attendee identities. [Exact-code-SHA CI #41261650111](https://github.com/cseashrafulislam/EduOS/actions/runs/41261650111) **PASS**; [SQL #41261650205](https://github.com/cseashrafulislam/EduOS/actions/runs/41261650205) was running at recording time. This is source/DOM-simulation evidence, not authenticated browser/mobile/screen-reader, tenant-switch locale, spreadsheet-client or institutional UAT.

### 9.10. Reviewed Finance/Payroll report and posting manuals (2026-11-01)

**Inspected code baseline:** Ashraful `e60d6a4e1d0b031a5bf631563ca24549fe062421`; source: actual FeeReceivablesAging, PayrollJournalPosting and PayrollSalaryStatement page/API/controller/JS/service contracts. The existing shared `page-context-help.js` catalogue gains reviewed full en-BD and bn-BD manuals for `/FeeReceivablesAging/Index`, `/PayrollJournalPosting/Index` and `/PayrollSalaryStatement/Index`; no second renderer, new route, migration, Finance GL, stock or payroll posting implementation. The two old duplicate inline mini-help controls (cash report and salary statement) are removed; the global header Info drawer now explains their operations.

| Route | Verified role and module | Manual coverage and workflow boundary |
| --- | --- | --- |
| `/FeeReceivablesAging/Index` | TenantAdmin/Principal/Accountant; FINANCE | en-BD + bn-BD; current unpaid aging, page CSV, invoice/receipt/payment reconciliation, daily cash; not historical as-of or GL posting |
| `/PayrollJournalPosting/Index` | TenantAdmin/Principal/Accountant read, TenantAdmin write; FINANCE + PAYROLL | en-BD + bn-BD; independent approval, mapped canonical GL, request/review/post contra reversal, no bank disbursement |
| `/PayrollSalaryStatement/Index` | TenantAdmin; PAYROLL | en-BD + bn-BD; stored snapshot, 25-row pages, page-only totals, bounded selected-month CSV (1,000 maximum), browser print |

**Catalogue denominator:** 11 reviewed route manuals in each language (previously 8), checked by extended `EduOS.Tests/JavaScript/page-context-help.test.cjs` source-contract assertions. This is NOT complete page-label, dynamic JS message, CSV/print heading or whole-project translation parity. Exact-SHA CI and disposable SQL results must be inspected separately after push. Real authenticated mobile/keyboard/screen-reader browser, tenant-switch locale isolation, institute UAT and production go/no-go remain NOT RUN in this change.

**Verified code SHA:** [`ebb71b4930309e0328c101fa95d4ad734c5dd6bd`](https://github.com/cseashrafulislam/EduOS/commit/ebb71b4930309e0328c101fa95d4ad734c5dd6bd) (shared-branch descendant of the 5-file UI slice; intervening Facilities changes do not alter the UI manual contracts). [Exact-SHA Application CI #41246438351](https://github.com/cseashrafulislam/EduOS/actions/runs/41246438351) **PASS** (Release build, Node UI/manual source contracts, C# tests, EF snapshot, idempotent SQL script and publish). [Exact-SHA disposable SQL #41246437969](https://github.com/cseashrafulislam/EduOS/actions/runs/41246437969) **PASS** (fresh/repeat migrations and targeted SQL acceptance). Earlier descendant CI [#41246221430](https://github.com/cseashrafulislam/EduOS/actions/runs/41246221430) **FAIL** exposed an untranslated Bengali cash-report step in the new guide; the guide text was corrected without weakening the assertion and passed in the verified SHA. Browser/mobile/screen-reader and full-page/report translation parity remain **NOT RUN / INCOMPLETE**.


### 9.11. Workspace Appearance Studio — semantic-color pilot (2026-11-01)

The existing authenticated `/workspace`, per-user/per-institute versioned `TenantSettings` persistence, CSRF/session-bound controller, `site.js` theme engine and shared Page Information drawer are reused. No migration, duplicate theme table, new route or business API. The old two-paragraph page-local help is replaced by a reviewed en-BD/bn-BD manual in the shared header Info drawer.

Seven allowlisted optional HEX/RGB color surfaces (sidebar, header, page, card, button, form, table) support native picker, transient live preview, automatic readable black/white text, individual clear and versioned Save/Reset. The server normalizes `#RRGGBB`, rejects arbitrary CSS/unknown tokens and preserves existing colors when a legacy client omits the new field. Print templates, financial status semantics, GL and stock are unchanged. Focused C# tenant/legacy/invalid-input tests and JS source contracts accompany the slice. Real authenticated browser/mobile/screen-reader, screenshot contrast, cross-device UAT and institute acceptance remain **NOT RUN**; full branding, history restore and whole-product localization are **INCOMPLETE**. **Verified code SHA:** [`786efd086825ec0c67017cc0de99b7300123ede4`](https://github.com/cseashrafulislam/EduOS/commit/786efd086825ec0c67017cc0de99b7300123ede4) (initial slice [`51e5f34`](https://github.com/cseashrafulislam/EduOS/commit/51e5f3462ff4434baa9a83400fb452d586195b45), then focused session-harness correction without weakening session checks). [Exact-SHA Application CI #41250532601](https://github.com/cseashrafulislam/EduOS/actions/runs/41250532601) **PASS**: Release build/publish, 821 JavaScript tests and 3,149 .NET tests; [Exact-SHA SQL #41250532507](https://github.com/cseashrafulislam/EduOS/actions/runs/41250532507) **PASS**: fresh/repeat SQL migrations, existing domain acceptance and disposable backup/restore. An earlier code SHA [`51e5f34`](https://github.com/cseashrafulislam/EduOS/commit/51e5f3462ff4434baa9a83400fb452d586195b45) failed three **stale workspace-session DOM/test assertions** after the new safe style application and removal of the duplicate mini-help; the updated harness supplies the style API, preserves cross-tenant/session rejection and checks the shared drawer. The actual verified catalogue now has **12 reviewed route guides in each language**, including `/workspace`; this is not whole-product coverage.

### 9.12. Reversible Workspace preview (2026-11-01)

At code SHA [b6c2083](https://github.com/cseashrafulislam/EduOS/commit/b6c2083b5b20a780c092c0eb9059ed378787883c), the existing `/workspace` now distinguishes unsaved preview from persisted preferences, provides bilingual Discard preview, warns before leaving, includes panel/table reordering in dirty detection, blocks blind save after a failed load, and clears stale preview on session mismatch. The existing rowversion, tenant/session, CSRF and shared Help Drawer remain in use. Manual version 2 explains Save/Discard and recovery. No schema or business transaction changes.

[Exact-SHA CI #41254145184](https://github.com/cseashrafulislam/EduOS/actions/runs/41254145184) PASS (825 JS, 3,149 .NET, Release/publish); [SQL #41254145152](https://github.com/cseashrafulislam/EduOS/actions/runs/41254145152) PASS (fresh/repeat migrations and disposable SQL). Earlier [CI #41254000044](https://github.com/cseashrafulislam/EduOS/actions/runs/41254000044) failed one obsolete test assertion; the correction verifies the stronger loaded-session guard. Authenticated browser/mobile, tenant switching, contrast screenshots, institute UAT and owner go/no-go remain NOT RUN. Twelve reviewed bilingual manuals are not whole-project coverage.

### 9.13. Workspace saved-revision restore (2026-11-01)

The existing `/workspace` editor now offers **Previously saved appearances** under the existing Save/Discard/Reset actions. Its private, versioned `TenantSettings` row retains up to **five prior normalized appearance snapshots** (theme, custom semantic colors, navigation, density, font, dashboard panels and allowed table-column arrangement) with a monotonically increasing revision and UTC saved timestamp. The new `POST /api/workspace-preferences/restore` accepts **only a server-held revision ID and the current rowversion**, not client-supplied CSS or historical snapshot content. It reuses the current authenticated tenant/user lookup, anti-forgery, session-bound SERIALIZABLE write guard and existing validated Save path; stale rowversion returns 409, unknown revision returns 404, invalid payload returns 400. The previous current design becomes a bounded history entry after a successful restore. Legacy saved JSON remains readable and gains history on the next write. No new table, migration, global institute default, print-theme change, Finance or Inventory operation.

The same shared bilingual Page Information drawer (workspace manual v3) explains preview/Save/Discard and the restore confirmation. The editor renders history using DOM text nodes, disables restore during unsaved edits or invalidated sessions, and keeps tenant-scoped history out of browser storage. Focused C# service tests cover prior snapshot restoration, role/tenant-user isolation and stale-version rejection; JS source tests cover authorized session transaction, bounded history and the single existing drawer. **Automated acceptance evidence:** [SQL at code SHA `f81343c` #41258862687](https://github.com/cseashrafulislam/EduOS/actions/runs/41258862687) **PASS** (fresh/repeat migrations, session-revocation restore rollback, 173 combined SQL tests and backup/restore); [Application CI at descendant `2d27f4e` #41259371085](https://github.com/cseashrafulislam/EduOS/actions/runs/41259371085) **PASS** (827 JS, 3,158 .NET, Release/publish, EF snapshot and idempotent script). An earlier descendant [CI #41259078139](https://github.com/cseashrafulislam/EduOS/actions/runs/41259078139) **FAILED** one stale source-contract assertion after a safe refactor from `ToDto(row)` to `previous`; [`2d27f4e`](https://github.com/cseashrafulislam/EduOS/commit/2d27f4eb3a97a2aa116d2259b7cbb5319fd4c59b) updated the assertion to verify the same legacy-color preservation invariant without weakening it. SQL at the final code SHA is [#41259371086](https://github.com/cseashrafulislam/EduOS/actions/runs/41259371086), **PASS** (fresh/repeat SQL migrations, disposable SQL acceptance, session rollback and backup/restore). Actual authenticated browser/mobile/tenant-switch, screen-reader, restored-institute UAT and production go/no-go remain **NOT RUN**. This is scoped automated acceptance, not whole-project UI Final OK.

### 9.14. TenantAdmin institute-branding discovery in Workspace (2026-11-01)

**Owned UI delta:** [code 5fae23d9](https://github.com/cseashrafulislam/EduOS/commit/5fae23d9fbc9d8b0b3472e7b59ed1b81360efede), [focused DOM test db564a02](https://github.com/cseashrafulislam/EduOS/commit/db564a027a558de287c239fc065bcd5c39dc2ed6). The existing `/workspace` now presents a **TenantAdmin-only, read-only institute-branding summary** (institution name, safe same-origin uploaded logo, three existing primary/secondary/accent color settings), with a real link to the already authorized `/Account/BrandingSetup` editor. It reuses the current `TenantProfileController` and `TenantSettingController` GET contracts; **no new branding writer, table, theme engine, tenant policy or duplicate page**. Non-admin roles do not receive the panel or make privileged API calls.

**Session/tenant safety:** Both existing TenantAdmin-protected endpoints are read with bounded, no-store GETs; before rendering tenant data, the panel verifies the existing session-bound `/api/workspace-preferences` response against the rendered page's session and tenant reference. Session mismatch clears the panel and disables retry; read failures show localized error/retry. Logo URLs are restricted to local `/uploads/` paths and colors to valid HEX, with DOM text nodes rather than HTML injection. Underlying API authorization remains authoritative.

**Manual and QA:** The one shared `/workspace` bilingual manual advances to reviewed v4, explaining the actual TenantAdmin preview/edit route and failure recovery; reviewed catalogue remains **12 English / 12 Bangla** routes, not whole-product coverage. Pure JS and simulated DOM checks exercised unsafe colors/URLs, same-session rendering, mismatch clearing, non-admin no-request and failed-read retry. [Exact-code-SHA CI](https://github.com/cseashrafulislam/EduOS/actions/runs/41263004147) and [SQL smoke](https://github.com/cseashrafulislam/EduOS/actions/runs/41263004308) require their final GitHub conclusions before PASS may be claimed. **Authenticated browser/mobile/screen-reader, cross-device and live-institute UAT remain NOT RUN.** Institute-wide publication/edit approval, full branding preference resolution and report/print verification remain separately open; personal Save appearance does not publish institute defaults.

### 9.15. Facilities uncertain-retry recovery in the existing bilingual Page Manual (2026-11-01)

The existing `/FacilityWorkOrders/Index` operation retains its single on-demand shared Page Information drawer and unchanged tenant/role/CSRF server boundary. The domain-owned browser command helper now preserves the same idempotency ID for an identical failed/uncertain submission but blocks an edited payload until the user reloads and checks the request/history. Confirmed HTTP 400/422 input validation errors allow corrected resubmission with a new ID; ambiguous network/5xx/409 results retain the original command. This avoids silently treating a changed command as an exact retry and prevents a second maintenance request being created as a workaround. Both en-BD and bn-BD recovery text describe the real behavior; no extra tooltip, new drawer, route, API, Finance/Inventory posting or migration was introduced.

**Evidence:** [browser helper `dc282ba`](https://github.com/cseashrafulislam/EduOS/commit/dc282bae19628123c70a8a023b73f99ac48e7231), [focused Node VM contract `cf45806`](https://github.com/cseashrafulislam/EduOS/commit/cf45806673186e98276949aed15f6177e0a791a9), [bilingual manual `e3372e9`](https://github.com/cseashrafulislam/EduOS/commit/e3372e9d4135716ddc3c51f3a52d1439183253c6). Local standalone helper checks **PASS**; complete repository JS/CI/SQL and authenticated browser/mobile parity **NOT VERIFIED / NOT RUN**. Task 3 continues to own shared drawer infrastructure; Task 5 independently checks route/locale parity.

### 9.16. Branding setup antiforgery and contextual manual audit (2026-11-01)

**Source finding:** Existing `/Account/BrandingSetup` is a TenantAdmin-only onboarding route. Its `branding-setup.js` issued subdomain PUT, logo/favicon POST/DELETE, brand-color POST/PUT and onboarding completion POST without a request-verification token, although `TenantProfileController` and `TenantSettingController` require `[AutoValidateAntiforgeryToken]`. This is a reproducible source-contract mismatch, not evidence of successful live browser writes. The existing form now emits the framework antiforgery token and all five write sites fail closed when it is missing, sending the standard `RequestVerificationToken` header otherwise. All three brand colors are validated before the first separate settings write; no new transaction, API, publication/approval or shadow settings engine was introduced. The shared route-keyed Help Drawer now includes the verified en-BD/bn-BD Branding Setup manual, including independent-save/partial-failure recovery. Reviewed guide inventory becomes **13/13 English and 13/13 Bangla audited routes**, not whole-product coverage.

**Acceptance boundary:** Node source/guide contract test and exact-SHA CI must pass before the implementation is called verified; exact-SHA disposable SQL, authenticated TenantAdmin browser upload/save, tenant-switch, mobile/keyboard and institute UAT remain separate gates. The three existing branding settings are independent rows: interrupted saves may require reload and retry, and there is no invented institute-wide approval or atomic publication. No migration, stock ledger, Finance GL or `master` changes.
