const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const service = fs.readFileSync(path.join(root, 'EduOS.Service/Services/Transport/TransportService.FleetInsights.cs'), 'utf8');
const api = fs.readFileSync(path.join(root, 'EduOS.App/Controllers/Api/TransportController.cs'), 'utf8');
const ui = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/transport-operations/fleet-insights.js'), 'utf8');
test('fleet insights reuse authorized paged report with bounded retrieval', () => {
    assert.match(service, /GetCapacityReportAsync\(campusId, 1, 100, search/);
    assert.match(service, /TotalCount > 1000/);
    assert.match(service, /next\.Data\.TotalCount != first\.Data\.TotalCount/);
    assert.match(service, /!ids\.Add\(x\.VehicleReference\)/);
    assert.match(api, /\[HttpGet\("fleet-insights"\)\]/);
    assert.match(api, /Authorize\(Roles = "TenantAdmin,Principal,TransportManager"\)/);
});
test('fleet metrics UI uses text-only DOM and rejects stale filter responses', () => {
    assert.match(ui, /filterSnapshot !== filters\(\)/);
    assert.match(ui, /textContent/);
    assert.doesNotMatch(ui, /innerHTML/);
    new Function(ui);
});
