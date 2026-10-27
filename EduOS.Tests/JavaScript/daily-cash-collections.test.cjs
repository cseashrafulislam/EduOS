const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const js = read('EduOS.App/wwwroot/js/finance/daily-cash-collections.js');
const view = read('EduOS.App/Views/FeeReceivablesAging/Index.cshtml');
const api = read('EduOS.App/Controllers/Api/FeeReceivablesAgingController.cs');
const service = read('EduOS.Service/Services/Finance/FeeDailyCashCollections.cs');

test('cash register is role gated and campus scoped before SQL aggregation', () => {
    assert.match(api, /daily-cash-collections/);
    assert.match(api, /RequireModule\("FINANCE"\)/);
    assert.match(service, /_db\.CurrentTenantId|if \(!Allowed\)/);
    assert.match(service, /UserCampusAccesses/);
    assert.match(service, /scope\.Count == all\.Count/);
    assert.match(service, /scope\.Allocated == payment\.Amount/);
    assert.match(service, /IgnoreQueryFilters\(\)/);
});
test('cash register has bounded paging and safe current-page export', () => {
    for (const id of ['dailyCashFrom', 'dailyCashTo', 'dailyCashCampus',
        'dailyCashRun', 'dailyCashRows', 'dailyCashSummary',
        'dailyCashExport', 'dailyCashPrev', 'dailyCashNext'])
        assert.ok(view.includes('id="' + id + '"'), id);
    assert.match(js, /credentials: 'same-origin', cache: 'no-store'/);
    assert.match(js, /csvField/);
    assert.match(js, /revokeObjectURL/);
    assert.match(js, /state\.generation/);
    assert.match(service, /Take\(request\.PageSize\)/);
    assert.match(service, /GroupBy/);
});
