const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "../..");
const read = p => fs.readFileSync(path.join(root, p), "utf8");

test("Next-period plan selection uses the existing renewal API with tenant-admin CSRF", () => {
 const ui = read("EduOS.App/wwwroot/js/subscription-billing/index.js");
 const view = read("EduOS.App/Views/SubscriptionBilling/Index.cshtml");
 const api = read("EduOS.App/Controllers/Api/SubscriptionController.cs");
 assert.match(ui, /api\("\/api\/subscription-plans"\)/);
 assert.match(ui, /nextPlanId:nextPlanId===Number\(s\.subscriptionPlanId\)\?null:nextPlanId/);
 assert.match(ui, /eduos:renewal:"\+s\.reference\+":"\+nextPlanId/);
 assert.match(ui, /p\.isActive&&p\.isPublic/);
 assert.match(ui, /credentials:"same-origin"/);
 assert.match(ui, /RequestVerificationToken/);
 assert.match(view, /Your current plan and entitlements remain unchanged/);
 assert.match(api, /Authorize\(Roles = "TenantAdmin"\)/);
 assert.match(api, /AutoValidateAntiforgeryToken/);
 assert.doesNotMatch(ui, /innerHTML/);
});
test("Renewal enforces plan validation, pricing snapshots and idempotent replay", () => {
 const svc = read("EduOS.Service/Services/SaaS/SubscriptionService.Renewal.cs");
 const dto = read("EduOS.Core/DTOs/SaaS/SubscriptionWorkflowDtos.cs");
 assert.match(dto, /long\? NextPlanId/);
 assert.match(svc, /successor\.SubscriptionPlanId != \(nextPlanId \?\? previous\.SubscriptionPlanId\)/);
 assert.match(svc, /x\.Id == \(nextPlanId \?\? previous\.SubscriptionPlanId\)/);
 assert.match(svc, /plan\.CurrencyCode, previous\.CurrencyCode/);
 assert.match(svc, /PriceSnapshot = price/);
 assert.match(svc, /IsolationLevel\.Serializable/);
});
