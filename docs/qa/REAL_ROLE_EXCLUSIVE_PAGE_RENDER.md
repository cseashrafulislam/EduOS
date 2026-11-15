# Complete distinct role sidebar render inventory

Alongside the TenantAdmin 177-page Chromium sweep, run
`tests/browser/real-roleexclusive-page-render.cjs` in the disposable app.
It authenticates as the **other 12 fictional system roles**, derives actual
authorized sidebar URLs, deduplicates against the previous TenantAdmin route
artifact and renders every **new distinct** page at desktop 1366×768 and
mobile 390×844. All browser-initiated writes after login are blocked.
HTTP, missing semantic main content, redirect to login, unhandled JS exceptions
and attempted automatic writes fail a page.

The exact-SHA report states **distinct navigable menu routes**, not the sum
of 13 role-route visits, and does not assert that hidden parameterized routes,
forms' CRUD, exports, approvals or financial reconciliation are tested.
Print-only views without <main> are accepted when the body is nonempty.
No private Codespaces terminal or live institution data is accessed.
