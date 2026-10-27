const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../../EduOS.App');
const script = fs.readFileSync(path.join(root, 'wwwroot/js/library/reservations.js'), 'utf8');
const controller = fs.readFileSync(path.join(root, 'Controllers/Api/LibraryReservationsController.cs'), 'utf8');
test('reservation fulfillment uses campus-eligible paged copy picker', () => {
    assert.match(controller, /HttpGet\("\{id:long\}\/eligible-copies"\)/);
    assert.match(script, /\/eligible-copies\?/);
    assert.match(script, /pageSize: 50/);
    assert.match(script, /hasNextPage/);
    assert.doesNotMatch(script, /\/api\/library\/books\/.*\/copies\?/);
});
