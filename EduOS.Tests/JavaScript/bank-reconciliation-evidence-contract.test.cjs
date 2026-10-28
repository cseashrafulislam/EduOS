const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const api = read('EduOS.App/Controllers/Api/AccountingReportsController.cs');
const service = read('EduOS.Service/Services/Accounting/AccountingReportingBankReconciliationEvidence.cs');
const view = read('EduOS.App/Views/AccountingReports/Index.cshtml');
const js = read('EduOS.App/wwwroot/js/finance/bank-statement-preview.js');

test('historical reconciliation evidence is tenant-owned and not a live bank mutation', () => {
    assert.match(api, /bank-reconciliations\/\{batchId:long\}\/evidence"\), Authorize\(Roles = "TenantAdmin"\)/);
    assert.match(service, /TenantWideAllowed/);
    assert.match(service, /GetBankReconciliationDraftAsync\(batchId, ct\)/);
    assert.match(service, /TenantId == user\.TenantId/);
    assert.match(service, /OriginalMatchEventId == x\.Id/);
    assert.match(service, /EvidenceFingerprint != fingerprint/);
    assert.doesNotMatch(service, /SaveChangesAsync|JournalLines\.Update|IgnoreQueryFilters/);
});
test('historical evidence has distinct explicit CSV and print actions', () => {
    assert.match(js, /data-evidence-id/);
    assert.match(view, /statementEvidencePanel/);
    assert.match(view, /statementEvidenceCsv/);
    assert.match(view, /statementEvidencePrint/);
    assert.match(js, /bank-reconciliation-evidence-/);
    assert.match(js, /evidenceRows\(\)/);
    assert.match(js, /popup\.opener = null/);
    assert.match(js, /resetEvidence\(\)/);
    assert.match(js, /textContent = String\(value \?\? ''\)/);
});
