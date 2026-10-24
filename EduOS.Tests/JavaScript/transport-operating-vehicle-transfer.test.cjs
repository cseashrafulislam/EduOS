const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const test = require('node:test');
const script = fs.readFileSync(path.resolve(__dirname, '../../EduOS.App/wwwroot/js/transport-operations/operating-vehicle-transfer.js'), 'utf8');
test('transfer UI sends durable request key and concurrency token', () => {
    assert.match(script, /clientRequestId: requestId/);
    assert.match(script, /rowVersion: pending\.rowVersion/);
    assert.match(script, /\/transfer-vehicle/);
});
test('transfer UI preserves source reservation and reloads list', () => {
    assert.match(script, /Prior vehicle history is preserved/);
    assert.match(script, /transport:refresh-assignments/);
});
test('transfer UI validates effective date before submit', () => {
    assert.match(script, /effectiveDate <= pending\.startDate/);
    assert.match(script, /effectiveDate < localDate\(\)/);
});
