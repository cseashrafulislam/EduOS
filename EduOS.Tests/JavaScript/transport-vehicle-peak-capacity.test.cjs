const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');

test('vehicle inventory shows future peak reservations and uses it for capacity validation', () => {
    const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/transport-operations/vehicle-setup.js'), 'utf8');
    const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/TransportOperations/_VehicleSetup.cshtml'), 'utf8');
    assert.match(js, /cell\(item\.peakReservedSeats\)/);
    assert.match(js, /value < item\.peakReservedSeats/);
    assert.match(view, /Peak reserved/);
    assert.match(js, /rowVersion: item\.rowVersion/);
});

test('server computes bounded tenant-scoped peak and validates inside serializable transaction', () => {
    const source = fs.readFileSync(path.join(root, 'EduOS.Service/Services/Transport/TransportService.VehicleSetup.cs'), 'utf8');
    assert.match(source, /PeakReservedSeats\(/);
    assert.match(source, /request\.Capacity < peak/);
    assert.match(source, /Take\(10001\)/);
    assert.match(source, /IsolationLevel\.Serializable/);
    assert.match(source, /CanManageCampusAsync/);
});
