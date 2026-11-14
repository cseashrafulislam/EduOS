const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const view = read('EduOS.App/Views/Dashboard/Admin.cshtml');
const mvc = read('EduOS.App/Controllers/DashboardController.cs');
const api = read('EduOS.App/Controllers/Api/DashboardController.cs');
const service = read('EduOS.Service/Services/SaaS/DashboardService.cs');

test('platform dashboard remains read-only, SuperAdmin-only and no-store', () => {
    assert.match(mvc, /Authorize\(Roles = "SuperAdmin"\)/);
    assert.match(api, /Authorize\(Roles = "SuperAdmin"\)/);
    assert.match(api, /HttpGet\("platform"\)/);
    assert.match(mvc, /ResponseCache\(NoStore = true/);
    assert.match(mvc, /GetPlatformDashboardAsync/);
    assert.match(service, /_currentUser\.IsSuperAdmin/);
    assert.match(view, /@model EduOS\.Core\.DTOs\.Dashboard\.PlatformDashboardVm/);
});

test('dashboard shows all eight canonical metrics and preserves unavailable payment count', () => {
    for (const field of ['TotalTenants', 'ActiveTenants', 'PendingVerificationTenants',
        'IncompleteOnboardingTenants', 'NewTenantsLast30Days',
        'SubscriptionsExpiringNext7Days', 'OverdueActiveSubscriptions', 'PendingManualPayments'])
        assert.match(view, new RegExp('Model\\.' + field));
    assert.match(view, /PendingManualPayments\.HasValue/);
    assert.match(view, /PendingManualPayments\.Value\.ToString\(\) : "—"/);
    assert.match(service, /AsNoTracking\(\)/);
});

test('compact page uses the shared shell title, without second hero or cosmetic refresh', () => {
    assert.match(view, /ViewData\["Title"\]/);
    assert.doesNotMatch(view, /platform-dashboard-hero|platformRefresh|Refresh metrics/);
    assert.doesNotMatch(view, /<h[12][^>]*>Platform Operations<\/h[12]>/);
    assert.doesNotMatch(view, /<script|\/hangfire|\/health|asp-controller="Pricing"/);
    assert.match(view, /gap-3/);
});

test('tables are accessible, bilingual and show genuine empty states', () => {
    assert.match(view, /isBangla/);
    assert.match(view, /string StatusLabel\(string value\)/);
    assert.equal((view.match(/@StatusLabel\(/g) || []).length, 3);
    assert.match(view, /Model\.RecentTenants\.Count == 0/);
    assert.match(view, /Model\.UpcomingExpirations\.Count == 0/);
    assert.match(view, /scope="col"/);
    assert.match(view, /caption class="visually-hidden"/);
    assert.match(view, /<time datetime=/);
    for (const label of ['মোট প্রতিষ্ঠান', 'ইমেইল যাচাই বাকি', 'পেমেন্ট যাচাই', 'প্রতিষ্ঠান পরিচালনা'])
        assert.ok(view.includes(label), label);
});

test('operations links target known restricted administrative destinations', () => {
    for (const controller of ['PlatformTenants', 'Account', 'SubscriptionPaymentHistory',
        'SubscriptionPlanAdministration'])
        assert.match(view, new RegExp('asp-controller="' + controller + '"'));
    assert.match(view, /asp-action="PaymentVerification"/);
    assert.doesNotMatch(view, /innerHTML|localStorage|sessionStorage|eval\(/);
});
