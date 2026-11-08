'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname,
    '../../EduOS.App/wwwroot/js/assessment/omr-workspace.js'), 'utf8');

function deferred() {
    let resolve;
    const promise = new Promise(done => { resolve = done; });
    return { promise, resolve };
}
const tick = () => new Promise(resolve => setImmediate(resolve));

function browser(fetch) {
    const elements = new Map();
    function create(id = '') {
        const listeners = {};
        const classes = new Set(['d-none']);
        const el = {
            id, value: '', textContent: '', href: '', children: [], disabled: false,
            classList: { add: name => classes.add(name), remove: name => classes.delete(name) },
            addEventListener: (name, handler) => { listeners[name] = handler; },
            replaceChildren() { this.children = []; this.value = ''; },
            append(child) { this.children.push(child); },
            querySelector: () => create('submit'),
            setAttribute() {}, scrollIntoView() {}, focus() {},
            click() { return listeners.click?.({ preventDefault() {} }); },
            emit(name, extra = {}) { return listeners[name]?.({ preventDefault() {}, ...extra }); },
            get files() { return this._files; }, set files(value) { this._files = value; }
        };
        return el;
    }
    const get = id => {
        if (!elements.has(id)) elements.set(id, create(id));
        return elements.get(id);
    };
    const root = get('omrWorkspace');
    root.querySelectorAll = () => [];
    root.querySelector = () => ({ value: 'csrf-fixture' });
    get('omrHelpText'); get('omrHelp');
    const document = { getElementById: get, createElement: () => create(),
        documentElement: { lang: 'en-BD' }, body: { append() {} } };
    vm.runInNewContext(source, { document, fetch, URLSearchParams,
        crypto: { randomUUID: () => 'request-id' }, FormData: class {},
        window: { location: { assign() {} }, setTimeout() {} },
        confirm: () => true, URL: { createObjectURL() {}, revokeObjectURL() {} } });
    return get;
}
const response = data => ({ ok: true, status: 200, json: async () => ({ success: true, data }) });

test('stale OMR sheet response cannot re-display a previous template or its review action', async () => {
    const oldSheets = deferred();
    const calls = [];
    const get = browser(async url => {
        calls.push(url);
        if (url.includes('/scopes/page')) return response({ items: [{ assessmentSubjectId: 11,
            assessmentId: 12, academicBatchId: 13, assessmentName: 'Exam',
            academicBatchName: 'Batch', subjectName: 'Math' }] });
        if (url.includes('/component-marks/roster')) return response({
            components: [{ id: 20, name: 'OMR', fullMarks: 10 }],
            students: [{ studentSubjectRegistrationId: 100, rollNo: '01', studentName: 'Learner' }],
            totalCount: 1
        });
        if (url.includes('/templates?')) return response([
            { reference: 'form-A', name: 'A', questionCount: 2 },
            { reference: 'form-B', name: 'B', questionCount: 2 }
        ]);
        if (url.includes('templateReference=form-A')) return oldSheets.promise;
        if (url.includes('templateReference=form-B')) return response({ items: [{
            reference: 'sheet-B', studentSubjectRegistrationId: 100, state: 'Captured',
            uncertainCount: 0, suggestedAnswers: 'AA', confirmedAnswers: '', rowVersion: 'v'
        }], totalCount: 1 });
        throw new Error(`Unexpected OMR endpoint: ${url}`);
    });
    await tick();
    get('omrScope').value = '11'; get('omrScope').emit('change');
    await tick(); await tick();
    assert.ok(calls.some(x => x.includes('templateReference=form-A')));
    get('omrTemplate').value = 'form-B'; get('omrTemplate').emit('change');
    await tick();
    assert.equal(get('omrRows').children.length, 1);
    assert.equal(get('omrTemplate').value, 'form-B');
    oldSheets.resolve(response({ items: [{ reference: 'sheet-A', state: 'Reviewed',
        studentSubjectRegistrationId: 999, uncertainCount: 0, proposedMarks: 40 }], totalCount: 1 }));
    await tick();
    assert.equal(get('omrRows').children.length, 1);
    assert.equal(get('omrRows').children[0].children[0].textContent, '01 - Learner');
    assert.equal(get('omrRows').children[0].children[1].textContent, 'Captured');
    assert.equal(get('omrReviewForm').classList.add instanceof Function, true);
});

test('student search uses bounded SQL roster and ignores an older response', async () => {
    const previous = deferred();
    const pages = [];
    const get = browser(async url => {
        if (url.includes('/scopes/page')) return response({ items: [{ assessmentSubjectId: 11,
            assessmentId: 12, academicBatchId: 13, assessmentName: 'Exam',
            academicBatchName: 'Batch', subjectName: 'Math' }] });
        if (url.includes('/component-marks/roster')) {
            pages.push(url);
            if (url.includes('Search=old')) return previous.promise;
            return response({ components: [{ id: 20, name: 'OMR', fullMarks: 10 }],
                students: [{ studentSubjectRegistrationId: url.includes('Search=new') ? 202 : 100,
                    rollNo: '02', studentName: url.includes('Search=new') ? 'New pupil' : 'First pupil' }],
                totalCount: 1 });
        }
        if (url.includes('/templates?')) return response([]);
        throw new Error(`Unexpected OMR endpoint: ${url}`);
    });
    await tick();
    get('omrScope').value = '11'; get('omrScope').emit('change');
    await tick(); await tick();
    get('omrStudentSearch').value = 'old'; get('omrStudentFind').click();
    await tick();
    get('omrStudentSearch').value = 'new'; get('omrStudentFind').click();
    await tick();
    assert.ok(pages.some(x => x.includes('Page=1') && x.includes('PageSize=100') && x.includes('Search=new')));
    assert.equal(get('omrStudent').children[1].textContent, '02 - New pupil');
    previous.resolve(response({ students: [{ studentSubjectRegistrationId: 101,
        rollNo: '01', studentName: 'Old pupil' }], totalCount: 1 }));
    await tick();
    assert.equal(get('omrStudent').children[1].textContent, '02 - New pupil');
});

test('OMR UI contracts retain CSRF, idempotency keys, private URLs and independent verification', () => {
    assert.match(source, /RequestVerificationToken': token/);
    assert.match(source, /state\.captureContext !== context/);
    assert.match(source, /state\.reviewContext !== context/);
    assert.match(source, /identityConfirmed/);
    assert.match(source, /assessment-omr\/sheets\/\$\{encodeURIComponent\(sheet\.reference\)\}\/post/);
    const view = fs.readFileSync(path.join(__dirname,
        '../../EduOS.App/Views/AssessmentAdministration/Omr.cshtml'), 'utf8');
    assert.match(view, /id="omrStudentSearch"[^>]*maxlength="100"/);
    assert.match(view, /id="omrStudentNext"/);
    assert.match(view, /id="omrIdentityConfirmed"/);
    const exports = fs.readFileSync(path.join(__dirname,
        '../../EduOS.Service/Services/Assessment/OmrWorkflowService.Exports.cs'), 'utf8');
    assert.match(exports, /SetValue\(SafeSpreadsheetText\(title\)\)/);
    assert.match(exports, /SetValue\(SafeSpreadsheetText\(CellText\(/);
});
