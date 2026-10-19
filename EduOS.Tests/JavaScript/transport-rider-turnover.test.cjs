const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const service = read('EduOS.Service/Services/Transport/TransportService.RiderTurnover.cs');
const dto = read('EduOS.Core/DTOs/Transport/TransportRiderTurnoverDto.cs');
const iface = read('EduOS.Core/Interfaces/IServices/ITransportService.cs');
const api = read('EduOS.App/Controllers/Api/TransportController.cs');
const view = read('EduOS.App/Views/TransportOperations/Index.cshtml');
const partial = read('EduOS.App/Views/TransportOperations/_RiderTurnover.cshtml');
const ui = read('EduOS.App/wwwroot/js/transport-operations/rider-turnover.js');
test('canonical rider turnover is tenant and campus scoped with bounded months and privacy suppression', () => {
    for (const token of ['_assignments.GetQueryable().AsNoTracking()', 'assignment.TenantId == tenant',
        'enrollment.TenantId == tenant', 'route.TenantId == tenant', 'vehicle.TenantId == tenant',
        'campus.TenantId == tenant', 'enrollment.StudentId == assignment.StudentId',
        '!assignment.IsDeleted', '!enrollment.IsDeleted', '!route.IsDeleted',
        '!vehicle.IsDeleted', '!campus.IsDeleted', 'campus.IsActive',
        'AllowedCampusIds(tenant)', 'GroupBy(', 'span >= 12',
        'TransportAssignmentState.Cancelled', 'TransportAssignmentState.Closed',
        'StartDate <= today', 'EndDate.Value <= today', 'SuppressRiderTurnoverSmallCohort'])
        assert.ok(service.includes(token), token);
    assert.doesNotMatch(dto, /StudentId|StudentName|MonthlyFare|LicenseNumber|Phone/);
});
test('authorized API, MVC navigation, JS filters and CSV are wired without student details', () => {
    assert.match(iface, /GetRiderTurnoverAsync/);
    assert.match(iface, /ExportRiderTurnoverCsvAsync/);
    assert.match(api, /\[HttpGet\("rider-turnover"\)\]/);
    assert.match(api, /\[HttpGet\("rider-turnover\/export"\)\]/);
    assert.match(api, /Authorize\(Roles = "TenantAdmin,Principal,TransportManager"\)/);
    assert.match(view, /_RiderTurnover/);
    assert.match(view, /rider-turnover\.js/);
    assert.match(partial, /transportTurnoverCampus/);
    assert.match(ui, /query !== filters\(\)/);
    assert.match(ui, /filters\(\) !== query/);
    assert.match(ui, /textContent/);
    assert.doesNotMatch(ui, /innerHTML/);
    assert.match(service, /"Suppressed"/);
    new Function(ui);
});
