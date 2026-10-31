const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const { Script } = require('node:vm');

test('enterprise trace JavaScript parses and shows bilingual UNPOSTED settlement evidence', () => {
    const path = resolve(__dirname, '../../EduOS.App/wwwroot/js/inventory/v3-enterprise-trace.js');
    const source = readFileSync(path, 'utf8');
    assert.doesNotThrow(() => new Script(source, { filename: path }));
    assert.match(source, /settlementInstructions/);
    assert.match(source, /settlementReservedAmount/);
    assert.match(source, /NOT PAID/);
    assert.match(source, /অপরিশোধিত/);
    assert.match(source, /textContent = String\(value \?\? ''\)/);
});
