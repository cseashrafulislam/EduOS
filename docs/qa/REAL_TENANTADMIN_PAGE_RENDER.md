# Real TenantAdmin Page Render QA (disposable SQL only)

Every visible, actual TenantAdmin menu route is opened in Chromium on the same
synthetic tenant with desktop (1366 × 768) and mobile (390 × 844) viewports.
Every route must return HTTP 200 HTML, render a nonempty `#main-content`,
avoid login/denial redirects, and have no uncaught JavaScript page errors.
All cross-origin traffic and **every GET-unrelated request after login is
blocked**. This prevents business mutations but may reveal problematic
automatic writes on page initialization.

The test emits exact commit SHA and a route-by-route JSON report
`qa-artifacts/real-tenantadmin-page-render.json`. It visits only actual
sidebar links, **not necessarily all 204 canonical route manifests**.
Functional Create/Edit/Approve/Export tests, tenant/campus negative tests,
institutional UAT, and real production performance must still be independently
accepted. The test does **not** connect to any live or private Codespace.
