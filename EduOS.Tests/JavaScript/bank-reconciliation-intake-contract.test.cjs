const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const script = fs.readFileSync(path.join(__dirname,
    '../../EduOS.App/wwwroot/js/finance/bank-statement-preview.js'), 'utf8');
const view = fs.readFileSync(path.join(__dirname,
    '../../EduOS.App/Views/AccountingReports/Index.cshtml'), 'utf8');

test('statement comparison is not a persistence side effect', () => {
    assert.match(script, /bank-statement-preview/);
    assert.match(script, /statementSave'\)\.addEventListener\('click'/);
    assert.match(script, /clientRequestId:requestKey/);
    assert.match(script, /RequestVerificationToken:token\(\)/);
    assert.match(script, /crypto\.randomUUID\(\)/);
    assert.match(script, /previewRequest = null/);
});
test('saved statements use tenant-authorized paged register and direct-ID readback', () => {
    assert.match(script, /bank-reconciliations/);
    assert.match(script, /pageSize=25/);
    assert.match(script, /data-draft-id/);
    assert.match(script, /statementDraftExport/);
    assert.match(script, /sourceFingerprint/);
    assert.match(view, /id="statementIdentifier"/);
    assert.match(view, /id="statementDraftRows"/);
    assert.match(view, /id="statementSave"/);
});
test('saved CSV applies formula-injection escaping and never offline-caches private statements', () => {
    assert.match(script, /row\.map\(cell\)/);
    assert.match(script, /cache:'no-store'/);
    assert.doesNotMatch(script, /localStorage|sessionStorage|indexedDB/);
});
