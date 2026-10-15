const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');
const controller = read('EduOS.App/Controllers/OperationsInsightsController.cs');
const view = read('EduOS.App/Views/OperationsInsights/Index.cshtml');
const transportNav = read('EduOS.App/Views/TransportOperations/_FleetInsights.cshtml');
const hostelNav = read('EduOS.App/Views/HostelOperations/_OccupancyInsights.cshtml');
const js = read('EduOS.App/wwwroot/js/operations-insights/index.js');
test('dashboard page is restricted to shared management roles', () => {
    assert.match(controller, /Authorize\(Roles\s*=\s*"TenantAdmin,Principal"\)/);
    assert.match(controller, /ResponseCache\(NoStore\s*=\s*true/);
});
test('each module uses existing independently authorized API and campus endpoints', () => {
    for (const route of ['/api/transport/fleet-insights','/api/hostel/occupancy-insights',
        '/api/transport/route-setup/campuses','/api/hostel/facility-campuses'])
        assert.ok(js.includes(route), route);
    assert.match(js, /response\.status === 401 \|\| response\.status === 403/);
});
test('no identifiers or financial values are rendered as metrics', () => {
    assert.doesNotMatch(js, /\[['"](?:studentName|driverName|vehicleReference|roomId|monthlyRent|monthlyFare)['"]/);
    assert.doesNotMatch(js, /innerHTML|localStorage|sessionStorage|eval\(/);
    assert.match(js, /value\.textContent = number\(data\[key\], suffix\)/);
    assert.ok(js.includes("value === null || value === undefined || value === ''"));
});
test('stale requests are cancelled and filter changes clear old metrics', () => {
    assert.match(js, /controller\?\.abort\(\)/);
    assert.match(js, /grid\.replaceChildren\(\)/);
    assert.match(js, /token !== serial \|\| snapshot !== query\(\)/);
    assert.match(js, /credentials: 'same-origin', cache: 'no-store'/);
});
test('Razor provides independent accessible filter controls', () => {
    for (const key of ['Transport','Hostel'])
        for (const suffix of ['Filters','Campus','Search','Refresh','Status','Metrics'])
            assert.ok(view.includes('ops' + key + suffix));
    assert.match(view, /aria-live="polite"/);
});
test('both existing operational dashboards link to the overview', () => {
    assert.ok(transportNav.includes('href="/OperationsInsights/Index"'));
    assert.ok(hostelNav.includes('href="/OperationsInsights/Index"'));
    for (const partial of [transportNav, hostelNav])
        assert.match(partial, /User\.IsInRole\("TenantAdmin"\) \|\| User\.IsInRole\("Principal"\)/);
});
test('JavaScript syntax is valid', () => assert.doesNotThrow(() => new Function(js)));
test('filter invalidation restores refresh after cancelling a pending request', () => {
    const invalidate = js.match(/function invalidate\(\) \{([\s\S]*?)\n        \}/)?.[1] ?? '';
    assert.match(invalidate, /controller\?\.abort\(\)/);
    assert.match(invalidate, /if \(available\) button\.disabled = false;/);
    assert.match(js, /if \(token === serial\) \{ controller = null; button\.disabled = false; \}/);
});
