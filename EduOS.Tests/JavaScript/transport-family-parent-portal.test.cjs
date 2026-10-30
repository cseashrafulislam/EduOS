const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

test('Parent family entry and V2 route remain aligned with the server permission contract', () => {
    const view = read('EduOS.App/Views/TransportOperations/Index.cshtml');
    const family = read('EduOS.App/Views/TransportOperations/Family.cshtml');
    const client = read('EduOS.App/wwwroot/js/transport-operations/family-schedule.js');
    assert.match(view, /User\.IsInRole\("Parent"\)/);
    assert.match(view, /asp-action="Family"/);
    assert.match(family, /transportFamilyPanel/);
    assert.match(client, /\/api\/transport\/family-schedule/);
    const guard = read('EduOS.App/wwwroot/js/transport-operations/session-guard.js');
    assert.match(client, /guard\.request\(/);
    assert.match(guard, /credentials: 'same-origin'/);
    assert.match(guard, /cache: 'no-store'/);
    assert.match(guard, /controller\.abort\(\)/);
    assert.match(family, /session-guard\.js/);
    assert.match(client, /rows\.replaceChildren\(\)/);
    assert.doesNotMatch(client, /\.innerHTML\s*=/);
});

test('Transport family readback and CSV use the same live pickup permission as guardian hostel', () => {
    const transport = read('EduOS.Service/Services/Transport/TransportFamilyScheduleService.cs');
    const hostel = read('EduOS.Service/Services/Hostel/GuardianHostelStayService.cs');
    const family = read('EduOS.App/Views/TransportOperations/Family.cshtml');
    assert.match(transport, /link\.IsAuthorizedPickup/);
    assert.match(hostel, /link\.IsAuthorizedPickup/);
    assert.match(transport, /var query = BuildQuery\(date\)/);
    assert.match(transport, /await BuildQuery\(date\)/);
    assert.match(family, /guardian link permits pickup/);
});
