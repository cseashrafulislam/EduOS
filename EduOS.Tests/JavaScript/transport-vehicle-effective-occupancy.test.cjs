const test = require('node:test'), assert = require('node:assert/strict'), fs = require('node:fs');
const read = path => fs.readFileSync(path, 'utf8');
const service = read('EduOS.Service/Services/Transport/TransportService.VehicleSetup.cs');
const dto = read('EduOS.Core/DTOs/Transport/TransportVehicleSetupDtos.cs');
const view = read('EduOS.App/Views/TransportOperations/_VehicleSetup.cshtml');
const js = read('EduOS.App/wwwroot/js/transport-operations/vehicle-setup.js');
const api = read('EduOS.App/Controllers/Api/TransportController.cs');
test('vehicle setup reports effective occupancy separately from active reservations', () => {
    assert.match(service, /Reservations = g.Count\(\)/);
    assert.match(service, /OccupiedToday = g.Count\(a => a.StartDate <= today/);
    assert.match(service, /!a.EndDate.HasValue \|\| a.EndDate.Value >= today/);
    assert.match(service, /AssignedStudents = reservations.GetValueOrDefault/);
    assert.match(service, /OccupiedToday = reservations.GetValueOrDefault/);
    assert.match(dto, /public int OccupiedToday/);
});
test('capacity and deactivation still protect future active reservations', () => {
    assert.match(service, /request.Capacity < occupied/);
    assert.match(service, /!request.IsActive && \(occupied > 0 \|\| currentRoutes > 0 \|\| currentDriver\)/);
    assert.match(service, /IsolationLevel.Serializable/);
    assert.match(service, /vehicle.RowVersion.AsSpan\(\).SequenceEqual\(version\)/);
    assert.match(js, /value < item.assignedStudents/);
    assert.match(js, /item.assignedStudents > 0/);
});
test('authorized existing API and safe UI show both counts without new endpoints', () => {
    assert.match(api, /HttpGet\("vehicle-setup"\)/);
    assert.match(api, /Authorize\(Roles = "TenantAdmin,Principal,TransportManager"\)/);
    assert.match(view, /Occupied today<\/th><th>Active reservations/);
    assert.match(js, /cell\(item.occupiedToday\)/);
    assert.match(js, /cell\(item.assignedStudents\)/);
    assert.doesNotMatch(js, /innerHTML/);
    new Function(js);
});
