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
    assert.match(client, /credentials:'same-origin',cache:'no-store'/);
    assert.match(client, /rows\.replaceChildren\(\)/);
    assert.doesNotMatch(client, /\.innerHTML\s*=/);
});
