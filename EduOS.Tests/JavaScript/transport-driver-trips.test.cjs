const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const app = path.resolve(__dirname, '../../EduOS.App');
const service = path.resolve(__dirname, '../../EduOS.Service');
const read = p => fs.readFileSync(path.join(app, p), 'utf8');

test('staff driver portal is authenticated, module-gated and assigned-only', () => {
    const controller = read('Controllers/Api/TransportDriverTripsController.cs');
    const page = read('Controllers/TransportOperationsController.cs');
    const serviceCode = fs.readFileSync(path.join(service, 'Services/Transport/TransportTripService.Driver.cs'), 'utf8');
    const index = read('Views/TransportOperations/Index.cshtml');
    assert.match(controller, /Authorize\(Roles = "Staff"\)/);
    assert.match(controller, /RequireModule\("TRANSPORT"\)/);
    assert.match(controller, /AutoValidateAntiforgeryToken/);
    assert.match(page, /Authorize\(Roles = "Staff"\)/);
    assert.match(index, /User\.IsInRole\("Staff"\)/);
    assert.match(serviceCode, /employee\.UserId == userId/);
    assert.match(serviceCode, /employee\.State == EmployeeState\.Active/);
    assert.match(serviceCode, /assignment\.CampusId == trip\.CampusId/);
    assert.match(serviceCode, /driver\.Id == trip\.TransportDriverId/);
    assert.match(serviceCode, /driver\.LicenseExpiresOn\.Value >= today/);
});

test('driver attendance is real, CSRF-protected, retry-keyed and never permits dispatch or corrections', () => {
    const js = read('wwwroot/js/transport-operations/driver-trips.js');
    const view = read('Views/TransportOperations/DriverTrips.cshtml');
    const controller = read('Controllers/Api/TransportDriverTripsController.cs');
    assert.match(js, /RequestVerificationToken/);
    assert.match(js, /pendingKeys\.set\(key, crypto\.randomUUID\(\)\)/);
    assert.match(js, /clientRequestId:pendingKeys\.get\(key\)/);
    assert.match(js, /textContent/);
    const guard = read('wwwroot/js/transport-operations/session-guard.js');
    assert.match(js, /guard\.request\(/);
    assert.match(js, /guard\.assert\(\)/);
    assert.match(guard, /credentials: 'same-origin'/);
    assert.match(guard, /cache: 'no-store'/);
    assert.match(guard, /controller\.abort\(\)/);
    assert.match(view, /session-guard\.js/);
    assert.match(view, /Html\.AntiForgeryToken/);
    assert.match(view, /driverRiderRows/);
    assert.match(controller, /RecordDriverAttendanceAsync/);
    assert.doesNotMatch(controller, /DispatchAsync|CorrectAttendanceAsync|CompleteAsync/);
    assert.doesNotMatch(js, /innerHTML|latitude|longitude|gps/i);
});
