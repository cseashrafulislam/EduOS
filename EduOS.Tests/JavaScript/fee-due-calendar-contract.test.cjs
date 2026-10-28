const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const api = read('EduOS.App/Controllers/Api/FeeReceivablesAgingController.cs');
const service = read('EduOS.Service/Services/Finance/FeeReceivablesDueCalendar.cs');
const view = read('EduOS.App/Views/FeeDueCalendar/Index.cshtml');
const js = read('EduOS.App/wwwroot/js/finance/fee-due-calendar.js');

test('due calendar is server tenant and campus scoped and read-only', () => {
    assert.match(api, /\[HttpGet\("due-calendar"\)\]/);
    assert.match(service, /if \(!Allowed\)/);
    assert.match(service, /UserCampusAccesses\.Any/);
    assert.match(service, /i\.TenantId == tenant && e\.TenantId == tenant && s\.TenantId == tenant/);
    assert.match(service, /i\.DueDate >= request\.FromDate && i\.DueDate <= request\.ToDate/);
    assert.match(service, /GroupBy\(x => new/);
    assert.doesNotMatch(service, /SaveChangesAsync|IgnoreQueryFilters|ExecuteUpdate|ExecuteDelete/);
});
test('report has explicit bounded scope and safe page-only export', () => {
    assert.match(view, /feeDueCalendarApp/);
    assert.match(view, /feeDueCsv/);
    assert.match(view, /feeDuePrint/);
    assert.match(view, /aria-label="Due calendar help"/);
    assert.match(js, /cache: 'no-store'/);
    assert.match(js, /credentials: 'same-origin'/);
    assert.match(js, /textContent = String\(value \?\? ''\)/);
    assert.match(js, /BigInt/);
    assert.match(js, /state\.report\.invoices\.items/);
    assert.match(js, /\\uFEFF/);
    assert.match(js, /URL\.revokeObjectURL/);
});
