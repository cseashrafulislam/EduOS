const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

test('invoice and receipt documents use scoped read-only authorized endpoints', () => {
    const api = read('EduOS.App/Controllers/Api/FeeBillingController.cs');
    const service = read('EduOS.Service/Services/Finance/FeeBillingDocumentReads.cs');
    const mvc = read('EduOS.App/Controllers/FeeDocumentsController.cs');
    assert.match(api, /students\/\{reference:guid\}\/invoices\/\{documentReference:guid\}/);
    assert.match(api, /students\/\{reference:guid\}\/payments\/\{documentReference:guid\}/);
    assert.match(service, /i\.TenantId == _user\.TenantId/);
    assert.match(service, /p\.TenantId == _user\.TenantId/);
    assert.match(service, /enrollmentIds\.Contains\(i\.StudentEnrollmentId\)/);
    assert.match(service, /p\.StudentId == student\.Id/);
    assert.match(mvc, /RequireModule\("FINANCE"\)/);
    assert.match(mvc, /NoStore = true/);
});
test('print document handles invalid references and renders without HTML interpolation', () => {
    const js = read('EduOS.App/wwwroot/js/finance/fee-documents.js');
    const view = read('EduOS.App/Views/FeeDocuments/Document.cshtml');
    assert.match(js, /guid\.test\(student\)/);
    assert.match(js, /guid\.test\(reference\)/);
    assert.match(js, /textContent =/);
    assert.doesNotMatch(js, /innerHTML\s*=/);
    assert.match(js, /cache:'no-store'/);
    assert.match(js, /docPrint'\)\.disabled = true/);
    assert.match(view, /@media print/);
});
