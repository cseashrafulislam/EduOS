const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "../..");
const js = fs.readFileSync(path.join(root, "EduOS.App/wwwroot/js/platform/institution-type-administration.js"), "utf8");
const view = fs.readFileSync(path.join(root, "EduOS.App/Views/PlatformCatalogAdministration/Index.cshtml"), "utf8");
const nav = fs.readFileSync(path.join(root, "EduOS.App/Views/Shared/_Layout.cshtml"), "utf8");

test("institution metadata editor posts antiforgery and stable idempotency key", () => {
    assert.match(js, /RequestVerificationToken/);
    assert.match(js, /requestId = crypto\.randomUUID\(\)/);
    assert.match(js, /requestId, rowVersion: selected\.rowVersion/);
    assert.match(js, /\/metadata", "POST"/);
    assert.doesNotMatch(js, /innerHTML/);
});
test("institution type editor and SuperAdmin navigation remain connected", () => {
    assert.match(view, /catalogEditNameBangla/);
    assert.match(view, /catalogEditCycle/);
    assert.match(view, /institution-type-administration\.js/);
    assert.match(nav, /PlatformCatalogAdministration\/Index/);
    assert.match(js, /\/activation", "POST"/);
});
