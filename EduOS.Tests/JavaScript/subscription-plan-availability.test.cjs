const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "../..");
const js = fs.readFileSync(path.join(root, "EduOS.App/wwwroot/js/platform/subscription-plan-administration.js"), "utf8");
const view = fs.readFileSync(path.join(root, "EduOS.App/Views/SubscriptionPlanAdministration/Index.cshtml"), "utf8");
const nav = fs.readFileSync(path.join(root, "EduOS.App/Views/Shared/_Layout.cshtml"), "utf8");

test("plan availability UI uses antiforgery, concurrency and idempotency", () => {
    assert.match(js, /RequestVerificationToken/);
    assert.match(js, /requestId:\s*crypto\.randomUUID\(\)/);
    assert.match(js, /rowVersion:\s*item\.rowVersion/);
    assert.match(js, /\/api\/platform\/subscription-plans\//);
    assert.doesNotMatch(js, /innerHTML/);
});
test("plan administration view and host navigation are connected", () => {
    assert.match(view, /subscriptionPlanAdministration/);
    assert.match(view, /subscription-plan-administration\.js/);
    assert.match(nav, /SubscriptionPlanAdministration\/Index/);
});
