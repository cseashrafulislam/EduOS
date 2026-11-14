# Real authenticated sidebar route QA

Runs in the existing disposable GitHub Actions SQL Server and EduOS App environment. Logs in as synthetic TenantAdmin and Student, extracts actual authorized sidebar links, performs same-origin read-only HTTP GETs, rejects 4xx/5xx/login redirects and records pass/fail per route. Stores exact SHA and route status as JSON without response contents. Cache header warnings are reported without declaring a page PASS on that property. This is navigation coverage, not all 204 routes or write/business/UAT certification.
