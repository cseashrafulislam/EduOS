# AI product language release gate — QA evidence

This gate scans active marketing HTML, Razor views, browser JS and locale resources for fixed numeric AI-agent claims. It deliberately excludes archived documents, historical logs, unrelated numerical quotas and the **five scheduled development tasks**. The assistant is described by capabilities and permissions, not by a marketed agent count.

Run: `python3 scripts/check_ai_product_language.py --self-test`. The test includes forbidden and allowed fixture cases and emits file/line positions only, not potentially sensitive source excerpts. The workflow `.github/workflows/ai-product-language-gate.yml` runs on Ashraful push, PR and manual dispatch. This is a **product-language** regression gate, not proof of a dynamic backend registry or model-free runtime.

**Not yet release-certified:** runtime capability registration/extensibility; ModelOff zero-provider-call tests; voice vendor egress; two-tenant/role/campus IDOR; SQL injection and query-plan benchmarks; full .NET build and EF migration checks; authenticated Chromium UAT; dependency/credential-history owner review; disposable SQL Server backup/restore. The other EduOS owners must implement their respective capabilities before independent QA can certify them. No synthetic p95/p99 is asserted.
