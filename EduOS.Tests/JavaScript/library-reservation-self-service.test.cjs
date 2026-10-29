const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const service = read('EduOS.Service/Services/Library/LibraryReservationService.cs');
const api = read('EduOS.App/Controllers/Api/LibraryReservationsController.cs');
const view = read('EduOS.App/Views/Library/Reservations.cshtml');
const js = read('EduOS.App/wwwroot/js/library/reservations.js');

test('student reservation status is filtered by tenant and user before SQL paging', () => {
    assert.match(service, /studentIds\.Contains\(x\.StudentId\)/);
    assert.match(service, /x\.TenantId == tenant && !x\.IsDeleted/);
    assert.match(service, /status == "expired"/);
    assert.match(service, /x\.ExpiresAt <= now/);
    assert.match(api, /HttpGet\("mine\/export"\)/);
    assert.match(api, /Authorize\(Roles = "Student"\)/);
    assert.match(api, /GetMyReservationsAsync\(page, pageSize, ct, status\)/);
    assert.match(api, /CacheControl = "no-store, private"/);
});

test('reservation UI filters current page and downloads only fresh authorized CSV', () => {
    assert.match(view, /id="reservationMineStatus"/);
    assert.match(view, /@if \(canExportMine\)/);
    assert.match(view, /id="reservationMineCsv"/);
    assert.match(view, /aria-label="Reservation help"/);
    assert.match(js, /status: el\('reservationMineStatus'\)\.value/);
    assert.match(js, /\/api\/library\/reservations\/mine\/export\?/);
    assert.match(js, /cache: 'no-store'/);
    assert.match(js, /mineRequest/);
    assert.doesNotMatch(js, /reservationMineRows\.innerHTML\s*=/);
    new Function(js);
});
