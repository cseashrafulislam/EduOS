const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/finance/accounting-reports.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/AccountingReports/Index.cshtml'), 'utf8');
const api = fs.readFileSync(path.join(root, 'EduOS.App/Controllers/Api/AccountingReportsController.cs'), 'utf8');

test('posted activity has tenant-admin-only paged API and bounded page export', () => {
    assert.match(api, /ledger\/\{accountId:long\}\/activity/);
    assert.match(api, /Authorize\(Roles = "TenantAdmin"\)/);
    assert.match(view, /User\.IsInRole\("TenantAdmin"\)/);
    assert.match(view, /id="ledgerAuditPanel"/);
    assert.match(js, /pageSize:'25'/);
    assert.match(js, /ledgerAuditCsv/);
    assert.match(js, /audit\.entries\.items/);
    assert.match(js, /auditKey/);
});
