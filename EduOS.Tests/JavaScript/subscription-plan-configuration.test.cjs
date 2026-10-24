const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "../..");
const js = fs.readFileSync(path.join(root, "EduOS.App/wwwroot/js/platform/subscription-plan-administration.js"), "utf8");
const view = fs.readFileSync(path.join(root, "EduOS.App/Views/SubscriptionPlanAdministration/Index.cshtml"), "utf8");

test("plan configuration editor has validation, antiforgery and concurrency", () => {
    assert.match(js, /RequestVerificationToken/);
    assert.match(js, /requestId:\s*crypto\.randomUUID\(\)/);
    assert.match(js, /rowVersion:\s*editing\.rowVersion/);
    assert.match(js, /\/configuration/);
    assert.match(js, /editorForm\.reportValidity\(\)/);
    assert.doesNotMatch(js, /innerHTML/);
});
test("plan configuration form is wired to the existing admin view", () => {
    assert.match(view, /id="planConfigurationForm"/);
    assert.match(view, /id="planFeatureRows"/);
    assert.match(view, /subscription-plan-administration\.js/);
});
