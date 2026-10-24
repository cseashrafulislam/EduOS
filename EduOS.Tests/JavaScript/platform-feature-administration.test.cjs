const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "../..");
const js = fs.readFileSync(path.join(root, "EduOS.App/wwwroot/js/platform/feature-catalog-administration.js"), "utf8");
const view = fs.readFileSync(path.join(root, "EduOS.App/Views/PlatformFeatureAdministration/Index.cshtml"), "utf8");
const nav = fs.readFileSync(path.join(root, "EduOS.App/Views/Shared/_Layout.cshtml"), "utf8");

test("feature catalog sends antiforgery, stable request ID and row version", () => {
    assert.match(js, /RequestVerificationToken/);
    assert.match(js, /requestId = crypto\.randomUUID\(\)/);
    assert.match(js, /requestId, rowVersion: selected\.rowVersion/);
    assert.match(js, /\/api\/platform\/catalog\/features\//);
    assert.doesNotMatch(js, /innerHTML/);
});
test("feature catalog workspace and SuperAdmin navigation are connected", () => {
    assert.match(view, /platformFeatureAdministration/);
    assert.match(view, /feature-catalog-administration\.js/);
    assert.match(nav, /PlatformFeatureAdministration\/Index/);
});
