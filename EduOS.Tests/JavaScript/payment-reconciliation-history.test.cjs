const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const script = fs.readFileSync(path.resolve(__dirname, '../../EduOS.App/wwwroot/js/platform/payment-reconciliation-history.js'), 'utf8');

test('reconciliation history is read-only, paged, and safely rendered', () => {
    assert.match(script, /admin\/reconciliation-history/);
    assert.match(script, /URLSearchParams/);
    assert.match(script, /replaceChildren/);
    assert.match(script, /textContent/);
    assert.doesNotMatch(script, /innerHTML|method:\s*['"]POST['"]|method:\s*['"]PUT['"]/);
    assert.match(script, /paymentState/);
    assert.match(script, /invoiceDueAmount/);
});
