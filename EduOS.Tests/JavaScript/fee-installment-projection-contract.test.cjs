const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const api = read('EduOS.App/Controllers/Api/FeeBillingController.cs');
const service = read('EduOS.Service/Services/Finance/FeeBillingInstallmentProjection.cs');
const view = read('EduOS.App/Views/FeeInstallmentProjection/Index.cshtml');
const js = read('EduOS.App/wwwroot/js/finance/fee-installment-projection.js');

test('forward projection is a bounded tenant and campus-scoped read-only query', () => {
    assert.match(api, /\[HttpGet\("installment-projection"\)\]/);
    assert.match(service, /if \(!CanManage\(\)/);
    assert.match(service, /CanAccessBillingCampusAsync/);
    assert.match(service, /request\.Months is < 1 or > 12/);
    assert.match(service, /e\.TenantId == tenant && e\.AcademicBatchId == batch\.Id/);
    assert.match(service, /i\.TenantId == tenant && !i\.IsDeleted/);
    assert.doesNotMatch(service, /SaveChangesAsync|IgnoreQueryFilters|ExecuteUpdate|ExecuteDelete/);
});

test('UI exposes honest gross-only scope and safe CSV/print', () => {
    assert.match(view, /aria-label="Future fee projection help"/);
    assert.match(view, /feeProjectionCsv/);
    assert.match(view, /feeProjectionPrint/);
    assert.match(view, /does not schedule or issue invoices/);
    assert.match(js, /cache: 'no-store'/);
    assert.match(js, /credentials: 'same-origin'/);
    assert.match(js, /BigInt/);
    assert.match(js, /textContent = String\(value \?\? ''\)/);
    assert.match(js, /let options = null, report = null/);
    assert.match(js, /URL\.revokeObjectURL/);
    assert.match(js, /\\uFEFF/);
});
