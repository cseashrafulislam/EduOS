const test = require('node:test'), assert = require('node:assert/strict'), fs = require('node:fs');
const read = path => fs.readFileSync(path, 'utf8');
const service = read('EduOS.Service/Services/Transport/TransportService.CapacityReport.cs');
const api = read('EduOS.App/Controllers/Api/TransportController.cs');
const view = read('EduOS.App/Views/TransportOperations/Index.cshtml');
const partial = read('EduOS.App/Views/TransportOperations/_CapacityReport.cshtml');
const js = read('EduOS.App/wwwroot/js/transport-operations/capacity-report.js');
test('capacity report enforces tenant, campus, paging and current rider dates', () => {
    for (const token of ['CanManage()', 'vehicle.TenantId == tenant', 'campus.TenantId == tenant',
        'AllowedCampusIds(tenant)', 'pageSize is < 1 or > 100', 'x.StartDate <= today',
        'x.EndDate.Value >= today', 'TransportAssignmentState.Active'])
        assert.ok(service.includes(token), token);
});
test('current route coverage excludes inactive and cross-campus route links', () => {
    assert.match(service, /route.IsActive && !vehicle.IsDeleted/);
    assert.match(service, /route.CampusId == vehicle.CampusId/);
    assert.match(service, /route.TenantId == tenant/);
});
test('bounded CSV protects against formula injection and contains no student identity', () => {
    assert.match(service, /TotalCount > 1000/);
    assert.match(service, /CsvCell\(row.VehicleNumber\)/);
    assert.match(service, /"=\+-@/);
    assert.ok(!service.includes('StudentName'));
    assert.ok(!service.includes('MonthlyFare'));
});
test('authorized API, view and JS are wired', () => {
    assert.match(api, /HttpGet\("capacity-report"\)/);
    assert.match(api, /HttpGet\("capacity-report\/export"\)/);
    assert.match(api, /Authorize\(Roles = "TenantAdmin,Principal,TransportManager"\)/);
    assert.ok(view.includes('_CapacityReport'));
    assert.ok(view.includes('capacity-report.js'));
    assert.ok(partial.includes('transportCapacityRows'));
    assert.ok(js.includes('/api/transport/capacity-report'));
    new Function(js);
});
