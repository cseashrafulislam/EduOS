const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const js = read('EduOS.App/wwwroot/js/finance/bank-statement-preview.js');
const view = read('EduOS.App/Views/AccountingReports/Index.cshtml');
const api = read('EduOS.App/Controllers/Api/AccountingReportsController.cs');
const service = read('EduOS.Service/Services/Accounting/AccountingReportingBankReconciliationApprovals.cs');

test('approval requires authenticated tenant admin and antiforgery token', () => {
    assert.match(api, /bank-reconciliations\/approval"\), Authorize\(Roles = "TenantAdmin"\), ValidateAntiForgeryToken/);
    assert.match(api, /bank-reconciliations\/\{batchId:long\}\/approval/);
    assert.match(service, /TenantWideAllowed/);
    assert.match(service, /PreparedByUserId/);
    assert.match(service, /IsExactMatch/);
    assert.match(service, /IsolationLevel\.Serializable/);
    assert.match(service, /Closed accounting period/);
});
test('approval UI is explicit, accessible and export is formula-safe', () => {
    assert.match(view, /statementApprovalAction/);
    assert.match(view, /statementApprovalSummary/);
    assert.match(view, /statementApprovalRows/);
    assert.match(js, /RequestVerificationToken:token\(\)/);
    assert.match(js, /statementApprovalCsv/);
    assert.match(js, /approvalStatus\.events/);
    assert.match(js, /data-draft-id/);
});
