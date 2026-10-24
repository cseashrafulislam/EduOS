const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const source = fs.readFileSync(path.resolve(__dirname,
    '../../EduOS.App/wwwroot/js/hostel-operations/checkout-follow-up.js'), 'utf8');
test('checkout follow-up script parses and uses authorized endpoint', () => {
    assert.doesNotThrow(() => new Function(source));
    assert.match(source, /\/api\/hostel\/checkout-follow-ups\?/);
    assert.match(source, /\/api\/hostel\/checkout-follow-ups\/export\?/);
});
test('worklist is privacy-safe and stale exports are blocked', () => {
    assert.match(source, /textContent = String\(value/);
    assert.match(source, /applied === null \|\| snapshot !== applied/);
    assert.doesNotMatch(source, /innerHTML/);
});
