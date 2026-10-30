const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

test('full manifest print routes enforce role and module entitlement before data read', () => {
    const controller = read('EduOS.App/Controllers/TransportTripPrintController.cs');
    assert.match(controller, /RequireModule\("TRANSPORT"\)/);
    assert.match(controller, /Authorize\(Roles = "TenantAdmin,Principal,TransportManager"\)/);
    assert.match(controller, /Authorize\(Roles = "Staff"\)/);
    assert.match(controller, /GetDriverManifestReportAsync/);
    assert.match(controller, /GetManifestReportAsync/);
    assert.match(controller, /NoStore = true/);
});

test('full print and export use bounded frozen snapshots and current role/campus/driver grants', () => {
    const service = read('EduOS.Service/Services/Transport/TransportTripService.ManifestReport.cs');
    assert.match(service, /CanDriveTripAsync/);
    assert.match(service, /HasCampusAsync/);
    assert.match(service, /trip\.RiderCount is < 1 or > 500/);
    assert.match(service, /\.Take\(501\)/);
    assert.match(service, /riders\.Count != trip\.RiderCount/);
    assert.match(service, /TransportTripAttendanceEvents/);
    assert.doesNotMatch(service, /StudentTransports|IgnoreQueryFilters|innerHTML/);
});

test('dispatcher and assigned driver print all report riders, not only a paged UI slice', () => {
    const print = read('EduOS.App/Views/TransportOperations/TripManifestPrint.cshtml');
    const trips = read('EduOS.App/wwwroot/js/transport-operations/trips.js');
    const driver = read('EduOS.App/wwwroot/js/transport-operations/driver-trips.js');
    const driverApi = read('EduOS.App/Controllers/Api/TransportDriverTripsController.cs');
    assert.match(print, /foreach \(var rider in Model\.Riders\)/);
    assert.match(print, /@rider\.StudentCode/);
    assert.match(trips, /\/transport\/trips\//);
    assert.match(driver, /\/transport\/driver-trips\//);
    assert.match(trips, /credentials: 'same-origin', cache: 'no-store'/);
    assert.match(driver, /credentials: 'same-origin', cache: 'no-store'/);
    assert.match(driverApi, /ExportDriverRidersCsvAsync/);
    assert.doesNotMatch(trips, /window\.print\(\)/);
    assert.doesNotMatch(driver, /window\.print\(\)/);
});
