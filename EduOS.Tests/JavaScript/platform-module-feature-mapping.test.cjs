const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "../..");
const js = fs.readFileSync(path.join(root, "EduOS.App/wwwroot/js/platform/module-feature-mapping.js"), "utf8");
const view = fs.readFileSync(path.join(root, "EduOS.App/Views/PlatformModuleFeatures/Index.cshtml"), "utf8");
const nav = fs.readFileSync(path.join(root, "EduOS.App/Views/Shared/_Layout.cshtml"), "utf8");
test("module feature mapping uses antiforgery, concurrency, idempotency and safe DOM", () => {
    assert.match(js, /RequestVerificationToken/);
    assert.match(js, /requestId = crypto.randomUUID\(\)/);
    assert.match(js, /moduleRowVersion: directory.moduleRowVersion/);
    assert.match(js, /featureRowVersion: item.featureRowVersion/);
    assert.match(js, /mappingRowVersion: item.mappingRowVersion/);
    assert.doesNotMatch(js, /innerHTML/);
});
test("workspace and SuperAdmin navigation are present", () => {
    assert.match(view, /platformModuleFeatures/);
    assert.match(view, /module-feature-mapping.js/);
    assert.match(nav, /PlatformModuleFeatures\/Index/);
});
