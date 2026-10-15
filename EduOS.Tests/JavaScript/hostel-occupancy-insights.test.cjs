const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const service = read('EduOS.Service/Services/Hostel/HostelService.OccupancyInsights.cs');
const api = read('EduOS.App/Controllers/Api/HostelController.cs');
const ui = read('EduOS.App/wwwroot/js/hostel-operations/occupancy-insights.js');
const view = read('EduOS.App/Views/HostelOperations/Index.cshtml');
test('occupancy insights reuse authorized capacity with bounded, stable pagination', () => {
    assert.match(service, /GetCapacityAsync\(campusId, 1, 100, search/);
    assert.match(service, /TotalCount > 1000/);
    assert.match(service, /next\.Data\.TotalCount != first\.Data\.TotalCount/);
    assert.match(service, /!ids\.Add\(x\.RoomId\)/);
    assert.match(api, /\[HttpGet\("occupancy-insights"\)\]/);
    assert.match(api, /Authorize\(Roles = "TenantAdmin,Principal,HostelWarden"\)/);
});
test('hostel insights UI renders text-only and guards against stale filter responses', () => {
    assert.match(ui, /snapshot !== filters\(\)/);
    assert.match(ui, /textContent/);
    assert.doesNotMatch(ui, /innerHTML/);
    assert.match(view, /_OccupancyInsights/);
    new Function(ui);
});
