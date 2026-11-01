'use strict';
// Independent guard for enterprise evidence privacy, not SQL or browser proof.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
test('enterprise trace rejects late detail/register results after tenant/session transition', () => {
    const js = read('EduOS.App/wwwroot/js/inventory/v3-enterprise-trace.js');
    assert.match(js, /dataset\.authSessionReference === session\.reference/);
    assert.match(js, /dataset\.authTenantId === session\.tenant/);
    assert.match(js, /EduOSSessionTransitionPending/);
    assert.match(js, /await window\.EduOSVerifySessionContext\(\)/);
    assert.match(js, /const generation = \+\+detailGeneration/);
    assert.match(js, /if \(generation !== detailGeneration\) return/);
    assert.match(js, /activeRequest !== request/);
    assert.match(js, /detailController\?\.abort\(\)/);
    assert.match(js, /activeRequest\?\.abort\(\)/);
    assert.match(js, /csv\.removeAttribute\('href'\)/);
    assert.match(js, /registerCsv\.removeAttribute\('href'\)/);
    assert.match(js, /response\.status === 401 \|\| response\.status === 403/);
    assert.match(js, /lockPage\(\)/);
});
test('enterprise evidence CSV and print require a fresh scoped session', () => {
    const js = read('EduOS.App/wwwroot/js/inventory/v3-enterprise-trace.js');
    assert.match(js, /exportLink\(csv\)/);
    assert.match(js, /exportLink\(registerCsv\)/);
    assert.match(js, /await verifySession\(\)/);
    assert.match(js, /if \(sessionCurrent\(\)\) window\.print\(\)/);
    const api = read('EduOS.App/Controllers/Api/V3EnterpriseLedgerTraceController.cs');
    assert.match(api, /Authorize\(Roles = "TenantAdmin"\)/);
    assert.match(api, /RequireModule\("INVENTORY"\)/);
    assert.match(api, /RequireModule\("FINANCE"\)/);
    assert.match(api, /ResponseCache\(NoStore = true/);
});
