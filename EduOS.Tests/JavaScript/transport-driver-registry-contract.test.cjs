const test = require('node:test'), assert = require('node:assert/strict'), fs = require('node:fs');
const read = path => fs.readFileSync(path, 'utf8');
const service = read('EduOS.Service/Services/Transport/TransportService.DriverRegistry.cs');
const api = read('EduOS.App/Controllers/Api/TransportController.cs');
const view = read('EduOS.App/Views/TransportOperations/Index.cshtml');
const partial = read('EduOS.App/Views/TransportOperations/_DriverRegistry.cshtml');
const js = read('EduOS.App/wwwroot/js/transport-operations/driver-registry.js');
test('driver registry is tenant-only and guarded from campus-scoped roles', () => {
    for (const token of ['CanManageDriverRegistry()', '_currentUser.IsTenantAdmin', 'x.TenantId == tenant', '!x.IsDeleted', 'pageSize is < 1 or > 100'])
        assert.ok(service.includes(token), token);
    assert.match(api, /HttpGet\("driver-registry"\)/);
    assert.match(api, /HttpPost\("driver-registry"\)/);
    assert.match(api, /HttpPut\("driver-registry\/\{driverId:long\}"\)/);
    assert.equal((api.match(/Authorize\(Roles = "TenantAdmin"\)/g) || []).length >= 3, true);
    assert.ok(view.includes('User.IsInRole("TenantAdmin")'));
});
test('driver registry preserves row-version and safe DOM rendering', () => {
    assert.ok(service.includes('DbUpdateConcurrencyException'));
    assert.ok(service.includes('IsolationLevel.Serializable'));
    assert.ok(service.includes('x.LicenseNumber.ToUpper() == license'));
    assert.ok(service.includes('existing.RowVersion.AsSpan().SequenceEqual(version)'));
    assert.ok(partial.includes('@Html.AntiForgeryToken()'));
    assert.ok(js.includes('RequestVerificationToken'));
    assert.ok(js.includes('rowVersion: editing?.rowVersion'));
    assert.ok(js.includes('td.textContent ='));
    assert.ok(!js.includes('innerHTML'));
    assert.ok(view.includes('driver-registry.js'));
    new Function(js);
});

test('driver license expiry cannot invalidate active or future assignments', () => {
    for (const token of [
        'existing.LicenseExpiresOn != request.LicenseExpiresOn',
        'x.IsCurrent && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= today)',
        'x.EffectiveTo.Value > requestedExpiry.Value',
        'License expiry cannot precede a current vehicle assignment end date'
    ]) assert.ok(service.includes(token), token);
});
