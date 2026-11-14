# EduOS UX A + B: screenshot-driven compact layout correction (2026-11-14)

**Branch:** `Ashraful`  
**Status:** Structural source corrections pushed for **83 assigned routes**. **Final QA deferred**, not a release certification.

## Reproduction from owner screenshots
- `/Admissions/Merit`: shared layout already shows **Admission Marks & Merit**; a second white `content-header-card` repeated the same title. The unselected empty applicant table/buttons and empty Merit preview added unnecessary height.
- `/Library/Integrity` and `/Library/Conditions`: a dedicated row held only a help `ⓘ` and a navigation button, duplicating global Page Help. The `form class="card card-body row ..."` merged a vertical card and Bootstrap grid on one element; `col-md-*` controls stacked in one region leaving a broad empty area.
- Similar structural problems occurred on Library Overdue/Circulation, library catalog/resources, Students & Admissions and other B header wrappers. Prior `h1` → visually-hidden span changes were insufficient because the parent card remained visible.

## Correct implementation
1. **Single route title:** shared `_Layout.cshtml` owns the visible title and breadcrumb. For B, redundant standalone `<header>` / `content-header-card` blocks were removed. No script-generated duplicate title. A's page-specific functional card titles and finance/legal warnings remain as needed.
2. **Action placement:** essential `Add Student`, `Calendar New Event`, `Back to Attendance Reports`, `Print Library`, `Reservations` and Identity History links were moved into the relevant existing filter/card toolbar, rather than deleted or assigned their own empty row. Redundant local Info controls use existing Page Help instead.
3. **Library filter layout:** use `<form class="card mb-2"><div class="card-body"><div class="row g-2 align-items-end">...` with valid nested grid containers. Integrity/Conditions/Overdue use **4+2+2+4** desktop widths; Apply is in the narrow 2-column action area and CSV/related actions in a wide 4-column flex area. Circulation uses **4+2+2+2+2**, with no redundant sidebar shortcuts squeezed into Search.
4. **Other broken card-grid mixes:** routines, library branches, student portal and Study/Assistant filters were moved to an actual nested `.row` inside the outer card/body. Existing form IDs, JS handlers, input names, antiforgery and server operations were retained.
5. **Empty-state behavior:** Admission Merit preview remains hidden until an authorized successful merit request populates it. Applicant roster and its actions remain collapsed until an assessment is selected. Section headings use `h6`-scale while the global shell title remains the page heading.
6. **Accessibility:** orphaned `aria-labelledby` references from removed header IDs were replaced with accessible section labels; essential read-only warning content was retained in compact text, or remains in the existing contextual Help.

## Audit extent
| Lane | Canonical sidebar routes | Canonical source views |
|---|---:|---:|
| UX A | 41 | 28 Razor |
| UX B | 42 | 36 Razor + 1 standalone offline HTML |
| **Total** | **83** | **65 source entries** |

Source review finds no remaining `<h1>` or `content-header-card` in A/B Razor views. UX B views no longer render their own top `<header>`. The remaining A subscription history `<header>` is a compact financial interpretation note, not a second page title. The new `ux-ab-compact-layout.test.cjs` guards these constraints, working button placement and Library grid proportions.

## Commit evidence
- `1d33873b7d3dcc360c1522b4ef83cc685545a2dc` — screenshot targets: Admission + Library filter rebuild
- `c0049627a6bd0e776e2f437959eccb54ec926c46` — remove Library duplicate headers, put Print/Reservations in existing catalog search controls
- `0a86b2567709a6dfef13d5e05e9f244de783a3e8` — students/portal/support blank-header cleanup
- `78016f3edb76fd0933fe7ce0122f829733127c55` — identity/attendance/academic control placement
- `339bfbfcdf2814b325237236a70c55723bb03c52` — remaining card-grid nesting and accessibility
- `0469c45f753b35e7f2bd51d7a39e6be11bdb54b8` — Merit conditional empty state
- `7a778645717cfd2521ccb7e7e149ebd103571112` — final verified Library grid ratio correction
- `ed379886f7e413cc1b301a40eff5a683490ef01b` — eight targeted static UX regression contracts

## Acceptance boundary and pending verification
This correction addresses observed **source structure**, not proof that every device/role screenshot now matches. No full authenticated browser session, SQL/migration execution, role/tenant privilege test, load testing, accessibility audit or final UAT has been completed on this new head in this operation. The separate Final QA phase must verify responsive display at common viewport sizes and both locales, real Library copy lists/empty states, Merit create/search/save/publish, form keyboard focus, 83 route navigation, shared AI/Voice, restricted actions, and code generation. Repository-wide JavaScript CI was already failing, and new changes must be judged at their own Git SHA; focused-source results never waive repository-wide CI failures.

Do not present this correction as **Final QA PASS** or production deployment approval. The five previously stopped scheduled UX automations remain OFF.
