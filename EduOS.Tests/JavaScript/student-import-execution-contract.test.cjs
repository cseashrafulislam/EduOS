const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

test('student import execution is an authorized atomic bounded workflow', () => {
    const api = read('EduOS.App/Controllers/Api/StudentImportsController.cs');
    const service = read('EduOS.Service/Services/Students/StudentImportExecutionService.cs');
    assert.match(api, /\[Authorize\(Roles = "TenantAdmin"\)\]/);
    assert.match(api, /\[RequireModule\("STUDENT"\)\]/);
    assert.match(api, /\[AutoValidateAntiforgeryToken\]/);
    assert.match(api, /\[HttpPost\("execute"\)\]/);
    assert.match(api, /\[HttpGet\("\{reference:guid\}"\)\]/);
    assert.match(service, /IsolationLevel\.Serializable/);
    assert.match(service, /MaxBatchRows = 100/);
    assert.match(service, /SourceScope = "StudentImportSource"/);
    assert.match(service, /_preview\.PreviewAsync/);
    assert.match(service, /new StudentPersonLink/);
    assert.match(service, /new StudentEnrollment/);
    assert.match(service, /new ImportLogItem/);
    assert.match(service, /new IdempotencyRecord/);
    assert.doesNotMatch(service, /IgnoreQueryFilters\(/);
});

test('import UI requires preview and token, offers reconciliation, avoids raw html', () => {
    const view = read('EduOS.App/Views/StudentImports/Index.cshtml');
    const js = read('EduOS.App/wwwroot/js/students/import-preview.js');
    assert.match(view, /@Html\.AntiForgeryToken\(\)/);
    assert.match(view, /studentImportCommit/);
    assert.match(view, /studentImportCommittedRows/);
    assert.match(js, /RequestVerificationToken/);
    assert.match(js, /credentials: 'same-origin'/);
    assert.match(js, /report\.invalidRows !== 0/);
    assert.match(js, /\/api\/student-imports\/execute/);
    assert.match(js, /loadReconciliation/);
    assert.match(js, /textContent/);
    assert.doesNotMatch(js, /innerHTML\s*=/);
});
