const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const app = path.resolve(__dirname, '../../EduOS.App');
const js = fs.readFileSync(path.join(app,'wwwroot/js/transport-operations/trips.js'),'utf8');
const view = fs.readFileSync(path.join(app,'Views/TransportOperations/Trips.cshtml'),'utf8');
const controller = fs.readFileSync(path.join(app,'Controllers/Api/TransportTripsController.cs'),'utf8');

test('dispatch UI uses real API and anti-forgery, never inferred attendance', () => {
    assert.match(js, /api\/transport\/trips/);
    assert.match(js, /RequestVerificationToken/);
    assert.match(js, /rowVersion:item\.rowVersion/);
    assert.match(js, /trip\.operatingDate !== today\(\)/);
    assert.doesNotMatch(js, /gps|latitude|longitude/i);
});
test('trip view exposes responsive planning, dispatch and frozen manifest', () => {
    for (const id of ['tripPlanForm','tripRows','tripManifestRows','tripStatus','tripExport']) {
        assert.match(view, new RegExp('id="'+id+'"'));
    }
    assert.match(view, /@Html\.AntiForgeryToken\(\)/);
    assert.match(view, /ট্রিপ/);
});
test('trip API requires transport entitlement and operator role', () => {
    assert.match(controller, /RequireModule\("TRANSPORT"\)/);
    assert.match(controller, /Authorize\(Roles = "TenantAdmin,Principal,TransportManager"\)/);
    assert.match(controller, /AutoValidateAntiforgeryToken/);
    assert.match(controller, /GetRidersAsync/);
    assert.match(controller, /ExportRidersCsvAsync/);
});
