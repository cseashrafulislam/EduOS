const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "../..");
const read = p => fs.readFileSync(path.join(root, p), "utf8");

test("Immediate subscription cancellation is explicit, tenant-authenticated and CSRF protected", () => {
 const ui = read("EduOS.App/wwwroot/js/subscription-billing/index.js");
 const view = read("EduOS.App/Views/SubscriptionBilling/Index.cshtml");
 const api = read("EduOS.App/Controllers/Api/SubscriptionController.cs");
 assert.match(ui, /End subscription now/);
 assert.match(ui, /typed!=="END"/);
 assert.match(ui, /window\.confirm/);
 assert.match(ui, /atPeriodEnd:false,rowVersion:s\.rowVersion/);
 assert.match(ui, /"\/cancel","POST"/);
 assert.match(ui, /RequestVerificationToken/);
 assert.match(ui, /!state\.upcoming/);
 assert.match(view, /does not automatically refund payments/);
 assert.match(api, /Authorize\(Roles = "TenantAdmin"\)/);
 assert.match(api, /AutoValidateAntiforgeryToken/);
 assert.doesNotMatch(ui, /innerHTML/);
});
test("Cancellation enforces current period, successor guard, audit and serializable transaction", () => {
 const svc = read("EduOS.Service/Services/SaaS/SubscriptionService.cs");
 assert.match(svc, /Subscription\.Cancelled/);
 assert.match(svc, /PreviousSubscriptionId == row\.Id/);
 assert.match(svc, /IsolationLevel\.Serializable/);
 assert.match(svc, /FixedTimeEquals/);
 assert.match(svc, /_audits\.AddAsync/);
 assert.match(svc, /SubscriptionState\.Cancelled/);
});
