const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "../..");
const js = fs.readFileSync(path.join(root, "EduOS.App/wwwroot/js/platform/tenant-lifecycle.js"), "utf8");
const view = fs.readFileSync(path.join(root, "EduOS.App/Views/PlatformTenants/Index.cshtml"), "utf8");
const api = fs.readFileSync(path.join(root, "EduOS.App/Controllers/Api/PlatformTenantsApiController.cs"), "utf8");
for (const match of js.matchAll(/\$\("([A-Za-z][A-Za-z0-9]+)"\)/g)) {
    assert.match(view, new RegExp(`id="${match[1]}"`), `Missing Razor element ${match[1]}`);
}
assert.match(api, /Authorize\(Roles = "SuperAdmin"\)/);
assert.match(api, /AutoValidateAntiforgeryToken/);
assert.match(js, /RequestVerificationToken/);
assert.match(js, /requestId/);
assert.match(api, /HttpGet\("\{tenantId:long\}\/history"\)/);
assert.match(js, /history\?\$\{query\}/);
assert.match(js, /historyRows\.replaceChildren\(\)/);
assert.match(js, /historySequence/);
assert.doesNotMatch(js, /innerHTML\s*=/);
console.log("PASS platform tenant lifecycle UI/API contract");
