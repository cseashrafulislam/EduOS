const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "../..");
const read = file => fs.readFileSync(path.join(root, file), "utf8");
test("TenantAdmin billing page preserves security boundaries", () => {
 const mvc = read("EduOS.App/Controllers/SubscriptionBillingController.cs");
 const view = read("EduOS.App/Views/SubscriptionBilling/Index.cshtml");
 const js = read("EduOS.App/wwwroot/js/subscription-billing/index.js");
 const layout = read("EduOS.App/Views/Shared/_Layout.cshtml");
 assert.ok(mvc.includes('[Authorize(Roles = "TenantAdmin")]'));
 assert.ok(mvc.includes("NoStore = true"));
 assert.ok(view.includes("@Html.AntiForgeryToken()"));
 assert.ok(js.includes('credentials:"same-origin"'));
 assert.ok(js.includes('opts.headers["RequestVerificationToken"]=token'));
 assert.ok(js.includes('rowVersion:s.rowVersion'));
 assert.ok(js.includes('[2,3].includes(inv.state)'));
 assert.ok(!js.includes("innerHTML"));
 assert.ok(!js.includes("tenantId"));
 assert.equal(layout.split('href="/SubscriptionBilling/Index"').length - 1, 1);
});
test("Billing navigation uses existing tenant-scoped endpoints", () => {
 const js = read("EduOS.App/wwwroot/js/subscription-billing/index.js");
 for (const route of ["/api/subscription/current", "/api/subscription/invoices",
   "/api/subscription/history", "/api/subscription-payment/invoice/",
   "/api/subscription/"]) assert.ok(js.includes(route), route);
 assert.ok(js.includes("/Account/Payment?invoiceId="));
 assert.ok(js.includes("if(Number(inv.dueAmount)>0"));
});
