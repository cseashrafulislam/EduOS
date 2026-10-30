const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

test('audit register is principal-only, tenant/campus-scoped and bounded', () => {
    const api = read('EduOS.App/Controllers/Api/TransportTripsController.cs');
    const print = read('EduOS.App/Controllers/TransportTripPrintController.cs');
    const service = read('EduOS.Service/Services/Transport/TransportTripService.Audit.cs');
    assert.match(api, /Authorize\(Roles = "TenantAdmin,Principal"\)/);
    assert.match(api, /ExportAttendanceAuditCsvAsync/);
    assert.match(print, /GetAttendanceAuditReportAsync/);
    assert.match(service, /GetManifestReportAsync/);
    assert.match(service, /GetAttendanceAuditAsync/);
    assert.match(service, /\.Take\(6001\)/);
    assert.match(service, /\.Take\(pageSize\)/);
    assert.match(service, /CorrectsEventId/);
    assert.match(service, /CorrectionReason/);
    assert.doesNotMatch(service, /IgnoreQueryFilters|StudentTransports|innerHTML/);
});

test('audit dialog is session-safe, paged and offers full authorized print/export', () => {
    const view = read('EduOS.App/Views/TransportOperations/Trips.cshtml');
    const js = read('EduOS.App/wwwroot/js/transport-operations/trips.js');
    const report = read('EduOS.App/Views/TransportOperations/TripAttendanceAuditPrint.cshtml');
    assert.match(view, /tripAuditDialog/);
    assert.match(view, /tripAuditRows/);
    assert.match(js, /guard\.request\(/);
    assert.match(js, /guard\.assert\(\)/);
    assert.match(js, /\/audit\/export/);
    assert.match(js, /\/audit\/print/);
    assert.match(js, /pageSize:'50'/);
    assert.match(report, /foreach \(var item in Model\.Events\)/);
    assert.match(report, /@item\.CorrectionReason/);
    assert.match(report, /noindex,nofollow/);
});
