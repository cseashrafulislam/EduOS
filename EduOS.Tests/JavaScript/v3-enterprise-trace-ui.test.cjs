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

test('asset Finance detail and register reject campusless journal evidence', () => {
    const detail = readFileSync(resolve(__dirname,
        '../../EduOS.Service/Services/Enterprise/V3EnterpriseAssetFinanceTraceService.cs'), 'utf8');
    const register = readFileSync(resolve(__dirname,
        '../../EduOS.Service/Services/Enterprise/V3EnterpriseReconciliationRegisterService.cs'), 'utf8');
    assert.match(detail, /journals\.Any\(x => x\.IsDeleted \|\| x\.CampusId != run\.CampusId\)/);
    assert.match(register, /j\.IsDeleted \|\| j\.CampusId != r\.CampusId/);
});
