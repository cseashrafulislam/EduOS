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

| Surface | Controlled, supported choices |
| --- | --- |
| Navigation | Left/sidebar, right/sidebar or top navigation; collapsed/expanded; favorites and allowed-menu ordering |
| Theme | Light, dark, system default, supported color palettes and branded accents |
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

**Current decision:** this is a future feature requirement. Do **not** modify the database schema or introduce a new preference model solely from this documentation commit. Audit the current reusable shell and configuration primitives before an implementation proposal.

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

