const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "../..");
const js = fs.readFileSync(path.join(root, "EduOS.App/wwwroot/js/platform/institution-type-administration.js"), "utf8");
const view = fs.readFileSync(path.join(root, "EduOS.App/Views/PlatformCatalogAdministration/Index.cshtml"), "utf8");
const nav = fs.readFileSync(path.join(root, "EduOS.App/Views/Shared/_Layout.cshtml"), "utf8");

test("host catalog activation uses antiforgery and idempotency request ID", () => {
    assert.match(js, /RequestVerificationToken/);
    assert.match(js, /activationRequests = new Map\(\)/);
    assert.match(js, /activationRequests\.get\(activationKey\) \|\| crypto\.randomUUID\(\)/);
    assert.match(js, /requestId: activationRequestId/);
    assert.match(js, /activationRequests\.delete\(activationKey\)/);
    assert.match(js, /rowVersion:\s*item\.rowVersion/);
    assert.match(js, /\/api\/platform\/catalog\/institution-types\//);
    assert.doesNotMatch(js, /innerHTML/);
});
test("catalog workspace and host navigation are connected", () => {
    assert.match(view, /institutionTypeAdministration/);
    assert.match(view, /institution-type-administration\.js/);
    assert.match(nav, /PlatformCatalogAdministration\/Index/);
});

test("catalog editor is bilingual, single-title, retry-only and preserves immutable Code", () => {
    assert.doesNotMatch(view, /<h1|id="catalogRefresh"|>Refresh</);
    assert.match(view, /data-bangla=/);
    assert.match(view, /id="catalogRetry"/);
    assert.match(view, /id="catalogEditCode"/);
    assert.doesNotMatch(view, /id="catalogEditCode"[^>]*<input/);
    assert.match(js, /loadSequence/);
    assert.match(js, /form\.addEventListener\("input"/);
});
