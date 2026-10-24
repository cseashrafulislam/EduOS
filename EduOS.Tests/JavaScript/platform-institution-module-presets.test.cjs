const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "../..");
const js = fs.readFileSync(path.join(root, "EduOS.App/wwwroot/js/platform/institution-module-presets.js"), "utf8");
const view = fs.readFileSync(path.join(root, "EduOS.App/Views/PlatformInstitutionModulePresets/Index.cshtml"), "utf8");
const nav = fs.readFileSync(path.join(root, "EduOS.App/Views/Shared/_Layout.cshtml"), "utf8");
test("preset administration includes antiforgery, concurrency, idempotency and safe DOM", () => {
    assert.match(js, /RequestVerificationToken/);
    assert.match(js, /requestId = crypto.randomUUID\(\)/);
    assert.match(js, /institutionTypeRowVersion: data.institutionTypeRowVersion/);
    assert.match(js, /rowVersion: item.rowVersion/);
    assert.doesNotMatch(js, /innerHTML/);
});
test("workspace and SuperAdmin navigation are present", () => {
    assert.match(view, /institutionModulePresets/);
    assert.match(view, /institution-module-presets.js/);
    assert.match(nav, /PlatformInstitutionModulePresets\/Index/);
});
