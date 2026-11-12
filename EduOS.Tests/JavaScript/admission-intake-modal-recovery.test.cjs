'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const js = read('EduOS.App/wwwroot/js/admissions/index.js');
const view = read('EduOS.App/Views/Admissions/Index.cshtml');
const css = read('EduOS.App/wwwroot/css/site.css');
const service = read('EduOS.Service/Services/Admission/AdmissionIntakeService.cs');
const persistence = read('EduOS.Persistence/Extensions/PersistenceServiceExtensions.cs');

function errorApi() {
    const nodes = new Map();
    const document = {
        body: { dataset: { authTenantId: '10', authSessionReference: 'S', authUserId: '1' } },
        documentElement: { lang: 'en-BD' },
        getElementById(id) {
            if (!nodes.has(id)) nodes.set(id, { id, focus() {}, setAttribute() {} });
            return nodes.get(id);
        },
        addEventListener() {}
    };
    document.getElementById('admissionStrings').textContent = '{}';
    const source = js.replace(/\}\)\(\);\s*$/,
        'globalThis.__intakeErrors = intakeServerErrors;})();');
    assert.notEqual(source, js, 'test must expose the existing helper, not simulate it');
    const context = { document, crypto: { randomUUID: () => 'test-ref' }, console };
    vm.runInNewContext(source, context);
    return { errors: context.__intakeErrors, nodes };
}
test('Admission editor scrolls inside its modal while Save draft remains visible', () => {
    assert.match(view, /id="intakeFormEditor" novalidate aria-describedby="intakeEditorHelp"/);
    assert.match(view, /class="modal-dialog admission-intake-dialog/);
    assert.match(view, /class="modal-body admission-intake-body"/);
    assert.match(view, /class="modal-footer admission-intake-footer"/);
    assert.match(css, /#intakeFormModal \.admission-intake-editor\s*\{[^}]*flex-direction:\s*column;[^}]*min-height:\s*0/s);
    assert.match(css, /#intakeFormModal \.admission-intake-body\s*\{[^}]*overflow-y:\s*auto/s);
    assert.match(css, /#intakeFormModal \.admission-intake-footer\s*\{[^}]*flex:\s*0 0 auto/s);
    assert.doesNotMatch(view, /Form description is not stored by the current canonical intake model/);
    assert.match(js, /modalTitle\.textContent = item/);
    assert.match(js, /querySelectorAll\('\.intake-field-row'\)\.length >= 50/);
    assert.match(js, /querySelectorAll\('\.intake-document-row'\)\.length >= 20/);
});
test('a duplicate admission code is linked to Code, not mislabeled as a stale edit', () => {
    const api = errorApi();
    const items = api.errors({ status: 409 }, {
        success: false, message: 'Admission form code already exists; choose a unique code.'
    });
    assert.equal(items.length, 1);
    assert.equal(items[0].row, api.nodes.get('intakeCode'));
    assert.match(items[0].message, /code already exists/i);
});
test('ASP.NET validation errors are mapped back to field controls', () => {
    const api = errorApi();
    const items = api.errors({ status: 400 }, {
        title: 'One or more validation errors occurred.',
        errors: { Code: ['Code is required.'], AcademicYearId: ['Invalid academic year.'] }
    });
    assert.equal(items.length, 2);
    assert.equal(items[0].row, api.nodes.get('intakeCode'));
    assert.equal(items[1].row, api.nodes.get('intakeYearId'));
});
test('HTTP 500 shows a safe support reference and instructs verifying the list before retry', () => {
    const api = errorApi();
    const items = api.errors({ status: 500 }, {
        success: false, message: 'Admission request could not be completed. Support reference: 12ab34cd56ef'
    });
    assert.equal(items.length, 1);
    assert.match(items[0].message, /HTTP 500/);
    assert.match(items[0].message, /check the list/i);
    assert.match(items[0].message, /12ab34cd56ef/);
});
test('Admission writes use EF execution strategy and force rollback on domain rejections', () => {
    assert.match(persistence, /EnableRetryOnFailure/);
    assert.match(service, /_uow\.ExecuteInTransactionAsync\(async _ =>/);
    assert.match(service, /if \(!result\.Success\) throw new RejectedAdmissionWrite<T>\(result\)/);
    assert.match(service, /System\.Data\.IsolationLevel\.Serializable/);
    assert.match(service, /catch \(RejectedAdmissionWrite<T> rejected\)/);
    assert.match(service, /support reference \{Reference\}/);
    assert.doesNotMatch(service, /new TransactionScope\(/);
    assert.match(js, /showIntakeValidation\(intakeServerErrors\(response, payload\)\)/);
    assert.match(js, /editor\.inert = false;\s*showIntakeValidation/);
});
