const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/Dashboard/Admin.cshtml'), 'utf8');
const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/dashboard/platform-admin.js'), 'utf8');
const mvc = fs.readFileSync(path.join(root, 'EduOS.App/Controllers/DashboardController.cs'), 'utf8');
const api = fs.readFileSync(path.join(root, 'EduOS.App/Controllers/Api/DashboardController.cs'), 'utf8');
test('platform dashboard is authorized and backed by the canonical dashboard service', () => {
    assert.match(mvc, /Authorize\(Roles = "SuperAdmin"\)/);
    assert.match(mvc, /GetPlatformDashboardAsync/);
    assert.match(api, /HttpGet\("platform"\)/);
    assert.match(api, /Authorize\(Roles = "SuperAdmin"\)/);
    assert.match(view, /@model EduOS.Core.DTOs.Dashboard.PlatformDashboardVm/);
});
test('refresh is safe and preserves server-rendered data on failure', () => {
    assert.match(js, /\/api\/dashboard\/platform/);
    assert.match(js, /textContent/);
    assert.match(js, /replaceChildren/);
    assert.match(js, /AbortController/);
    assert.doesNotMatch(js, /innerHTML|localStorage|sessionStorage|eval\(/);
    for (const id of ['platformDashboard', 'platformRefresh', 'platformRecentTenants', 'platformExpiringSubscriptions']) {
        assert.match(view, new RegExp('id="' + id + '"'));
    }
});
