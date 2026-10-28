const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../');
const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/finance/bank-reconciliation-candidates.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/AccountingReports/Index.cshtml'), 'utf8');
const api = fs.readFileSync(path.join(root, 'EduOS.App/Controllers/Api/AccountingReportsController.cs'), 'utf8');

test('bank candidate register is bounded, tenant-admin gated and server paged', () => {
    assert.match(api, /bank-book\/\{bankAccountId:long\}\/reconciliation-candidates/);
    assert.match(api, /Authorize\(Roles = "TenantAdmin"\)/);
    assert.match(js, /pageSize:'25'/);
    assert.match(js, /cache:'no-store'/);
    assert.match(js, /if \(seq !== revision\) return/);
    assert.match(view, /id="bankCandidateRows"/);
});
test('candidate CSV export guards formula injection and exports only the visible page', () => {
    assert.match(js, /\[=\+\\-@\]/);
    assert.match(js, /current\.rows\.map/);
    assert.match(js, /journalLineVersion/);
});
