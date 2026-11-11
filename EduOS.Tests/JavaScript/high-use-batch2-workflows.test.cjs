'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.resolve(__dirname, '../..');
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8');
const payroll = read('EduOS.App/wwwroot/js/payroll/index.js');
const marks = read('EduOS.App/wwwroot/js/assessment/component-marks.js');
const examWorkflow = read('EduOS.App/wwwroot/js/assessment/workflow.js');
const attendance = read('EduOS.App/wwwroot/js/attendance/index.js');
const admissions = read('EduOS.App/wwwroot/js/admissions/index.js');
const fees = read('EduOS.App/wwwroot/js/finance/fees.js');
const dashboard = read('EduOS.App/Views/Dashboard/Index.cshtml');
const flush = () => new Promise(resolve => setImmediate(resolve));
const response = data => ({ ok: true, json: async () => ({ success: true, data }) });
function deferred() {
    let resolve;
    const promise = new Promise(next => { resolve = next; });
    return { promise, resolve };
}
function element() {
    const listeners = new Map(), classes = new Set();
    return {
        value: '', textContent: '', disabled: false, children: [], dataset: {},
        classList: { add: value => classes.add(value), remove: value => classes.delete(value),
            toggle: (value, on) => on ? classes.add(value) : classes.delete(value) },
        addEventListener(type, listener) { listeners.set(type, listener); },
        trigger(type, event = {}) { return listeners.get(type)?.(event); },
        append(...children) { this.children.push(...children); },
        replaceChildren(...children) { this.children = children; },
        add(child) { this.children.push(child); },
        setAttribute(name, value) { this[name] = value; },
        removeAttribute(name) { delete this[name]; },
        querySelector() { return { value: 'csrf-token' }; },
        querySelectorAll() { return []; },
        focus() {}, scrollIntoView() {}, reportValidity() { return true; }
    };
}
function harness(script, { manager = false, scope = false } = {}) {
    const nodes = new Map(), calls = [];
    const get = id => {
        if (!nodes.has(id)) nodes.set(id, element());
        return nodes.get(id);
    };
    get('payrollApp').dataset.manager = String(manager);
    const document = {
        getElementById: id => {
            if (id === 'payrollApp' && scope) return null;
            if (id === 'componentMarksWorkspace' && !scope) return null;
            return get(id);
        },
        createElement: () => element(),
        querySelector: () => null
    };
    const context = {
        document, URLSearchParams, Date, Number, Math, String, JSON,
        Option: class { constructor(label, value) { this.textContent = label; this.value = String(value); } },
        window: { addEventListener() {}, confirm: () => true, print() {} },
        crypto: { randomUUID: () => 'b70bcc69-997e-4608-83a8-aa49fd9bc0ee' },
        fetch(url) {
            const pending = deferred();
            calls.push({ url: String(url), ...pending });
            return pending.promise;
        }
    };
    vm.runInNewContext(script, context, { filename: scope ? 'component-marks.js' : 'payroll.js' });
    return { get, calls };
}
function call(h, match, index = 0) {
    const rows = h.calls.filter(x => x.url.includes(match));
    assert.ok(rows[index], `Missing fetch ${match} #${index + 1}`);
    return rows[index];
}
function financeHarness() {
    const nodes = new Map(), calls = [];
    const get = id => {
        if (!nodes.has(id)) nodes.set(id, element());
        return nodes.get(id);
    };
    get('feeApp').dataset.activeFeature = 'ledger';
    const document = {
        documentElement: { lang: 'en-BD' },
        getElementById: get, createElement: () => element(),
        querySelector: () => ({ content: 'csrf-token' })
    };
    const context = { document, AbortController, encodeURIComponent, URLSearchParams, Date,
        Number, Math, String, JSON, confirm: () => true,
        crypto: { randomUUID: () => '1b0ea93e-cb47-4e9f-9533-3dc74eb5ee76' },
        fetch(url) {
            const pending = deferred();calls.push({ url: String(url), ...pending });return pending.promise;
        }
    };
    vm.runInNewContext(fees, context, { filename: 'fees.js' });
    return { get, calls };
}
function attendanceHarness() {
    const nodes = new Map(), calls = [];
    const get = id => {
        if (!nodes.has(id)) nodes.set(id, element());
        return nodes.get(id);
    };
    const document = { getElementById: get, createElement: () => element(),
        querySelector: () => null, addEventListener() {} };
    const context = { document, Date, Number, String, Math, JSON, URLSearchParams,
        confirm: () => false, encodeURIComponent,
        fetch(url) { const pending = deferred();calls.push({ url: String(url), ...pending });return pending.promise; } };
    vm.runInNewContext(attendance, context, { filename: 'attendance.js' });
    return { get, calls };
}
function examHarness() {
    const nodes = new Map(), calls = [];
    const get = id => {
        if (!nodes.has(id)) nodes.set(id, element());
        return nodes.get(id);
    };
    get('examWorkflow').dataset.canPublish = 'true';
    const document = { getElementById: get, createElement: () => element() };
    const context = { document, Number, String, Math, JSON, URLSearchParams, encodeURIComponent,
        Option: class { constructor(label, value) { this.textContent = label; this.value = String(value); } },
        window: { addEventListener() {}, confirm: () => true },
        crypto: { randomUUID: () => '19bbeea3-097b-437c-b5df-dcfc75811e80' },
        fetch(url) { const pending = deferred();calls.push({ url: String(url), ...pending });return pending.promise; } };
    vm.runInNewContext(examWorkflow, context, { filename: 'assessment-workflow.js' });
    return { get, calls };
}

test('Payroll run requests are page/period scoped and old results never overwrite a newer register', async () => {
    const h = harness(payroll, { manager: true });
    call(h, '/api/hr-payroll/runs?', 0);
    h.get('payrollLoad').trigger('click');
    call(h, '/api/hr-payroll/runs?', 1).resolve(response({
        items: [{ runNumber: 'NEW', month: 11, year: 2026, state: 4, netAmount: 10 }], totalCount: 1
    }));
    await flush();
    assert.equal(h.get('payrollRunRows').children[0].children[0].textContent, 'NEW');
    call(h, '/api/hr-payroll/runs?', 0).resolve(response({
        items: [{ runNumber: 'STALE', month: 11, year: 2026, state: 4, netAmount: 100 }], totalCount: 1
    }));
    await flush();
    assert.equal(h.get('payrollRunRows').children[0].children[0].textContent, 'NEW');
    h.get('payrollYear').value = '2027';
    h.get('payrollYear').trigger('change');
    assert.equal(h.get('payrollRunRows').children.length, 0);
    assert.match(h.get('payrollRunPageLabel').textContent, /select Load runs/);
    h.get('payrollLoad').trigger('click');
    assert.match(call(h, '/api/hr-payroll/runs?', 2).url, /year=2027/);
    call(h, '/api/hr-payroll/runs?', 2).resolve(response({ items: [], totalCount: 0 }));
    await flush();
    assert.match(h.get('payrollRunPageLabel').textContent, /0 records/);
});

test('Payroll employees and own payslips keep the latest result on out-of-order completions', async () => {
    const h = harness(payroll, { manager: true });
    call(h, '/api/hr-payroll/runs?').resolve(response({ items: [
        { reference: 'A', runNumber: 'A', state: 4 },
        { reference: 'B', runNumber: 'B', state: 4 }
    ], totalCount: 2 }));
    await flush();
    const actions = h.get('payrollRunRows').children.map(row => row.children[9].children[0]);
    actions[0].trigger('click');
    actions[1].trigger('click');
    call(h, '/runs/B/employees').resolve(response({ items: [
        { employeeName: 'Current B', lines: [], runState: 4 }
    ], totalCount: 1 }));
    await flush();
    call(h, '/runs/A/employees').resolve(response({ items: [
        { employeeName: 'Obsolete A', lines: [], runState: 4 }
    ], totalCount: 1 }));
    await flush();
    assert.equal(h.get('payrollEmployeeRows').children[0].children[1].textContent, 'Current B');
    assert.equal(h.get('payrollDetailPageLabel').textContent, 'Page 1 of 1 · 1 records');
    h.get('payrollMy').trigger('click');
    call(h, '/my-payslips?', 1).resolve(response({ items: [
        { employeeName: 'Current payslip', lines: [], runState: 4 }
    ], totalCount: 1 }));
    await flush();
    call(h, '/my-payslips?', 0).resolve(response({ items: [
        { employeeName: 'Old payslip', lines: [], runState: 4 }
    ], totalCount: 1 }));
    await flush();
    assert.equal(h.get('payrollSlipRows').children[0].children[1].textContent, 'Current payslip');
});

test('Marks roster response from old assessment selection cannot appear under a new subject', async () => {
    const h = harness(marks, { scope: true });
    call(h, '/api/assessment-administration/scopes').resolve(response([
        { assessmentName: 'Old', academicBatchName: 'B', subjectName: 'Math', assessmentId: 1,
            academicBatchId: 10, assessmentSubjectId: 100 },
        { assessmentName: 'New', academicBatchName: 'B', subjectName: 'English', assessmentId: 2,
            academicBatchId: 10, assessmentSubjectId: 200 }
    ]));
    await flush();
    h.get('cmScope').value = '0';
    h.get('componentMarksFilters').trigger('submit', { preventDefault() {} });
    h.get('cmScope').value = '1';
    h.get('cmScope').trigger('change');
    call(h, '/component-marks/roster?', 0).resolve(response({ assessmentName: 'Old', subjectName: 'Math',
        components: [{ id: 1, code: 'M', name: 'Math', fullMarks: 100, passMarks: 40, weightage: 100 }],
        students: [], isEditable: true, page: 1, pageSize: 50, totalCount: 0 }));
    await flush();
    assert.equal(h.get('cmInfo').textContent, '');
    assert.equal(h.get('cmSave').disabled, true);
    h.get('componentMarksFilters').trigger('submit', { preventDefault() {} });
    assert.match(call(h, '/component-marks/roster?', 1).url, /assessmentId=2/);
    call(h, '/component-marks/roster?', 1).resolve(response({ assessmentName: 'New', subjectName: 'English',
        components: [{ id: 2, code: 'E', name: 'English', fullMarks: 100, passMarks: 40, weightage: 100 }],
        students: [], isEditable: true, page: 1, pageSize: 50, totalCount: 0 }));
    await flush();
    assert.match(h.get('cmHeading').textContent, /New \/ English/);
});

test('Cash collection permits one preflight and one payment post while an identical submit is pending', async () => {
    const h = financeHarness();
    h.get('feeStudent').value = 'student-A';
    h.get('feeLoadLedger').trigger('click');
    const ledgerData = { studentName: 'Student', studentCode: 'ST-01', totalBilled: 12,
        totalCreditNotes: 0, netBilled: 12, netPaid: 0, totalDue: 12 };
    call(h, '/students/student-A/ledger').resolve(response(ledgerData));
    await flush();
    call(h, '/students/student-A/invoices?').resolve(response({ totalCount: 1, items: [{
        reference: 'invoice-A', invoiceNumber: 'INV-1', invoiceDate: '2026-11-01',
        dueDate: '2026-11-30', state: 2, totalAmount: 12, paidAmount: 0, dueAmount: 12
    }] }));
    call(h, '/students/student-A/payments?').resolve(response({ totalCount: 0, items: [] }));
    await flush();
    h.get('feeInvoices').children[0].children[6].children[0].onclick();
    const form = h.get('feePaymentForm');
    form.trigger('submit', { currentTarget: form, preventDefault() {} });
    form.trigger('submit', { currentTarget: form, preventDefault() {} });
    assert.equal(h.calls.filter(x => x.url.endsWith('/payments/preflight')).length, 1);
    assert.equal(h.get('feeCollect').disabled, true);
    call(h, '/payments/preflight').resolve(response({ amount: 12, currencyCode: 'BDT',
        invoices: [{ invoiceReference: 'invoice-A', allocating: 12, invoiceNumber: 'INV-1', dueAfter: 0 }] }));
    await flush();
    assert.equal(h.calls.filter(x => x.url.endsWith('/payments')).length, 1);
    form.trigger('submit', { currentTarget: form, preventDefault() {} });
    assert.equal(h.calls.filter(x => x.url.endsWith('/payments/preflight')).length, 1);
    assert.equal(h.calls.filter(x => x.url.endsWith('/payments')).length, 1);
});

test('Attendance does not discard unsaved marks when a date filter change is declined', async () => {
    const h = attendanceHarness();
    call(h, '/api/student-attendance/batches?').resolve(response({ items: [
        { id: 10, name: 'A', academicYearName: '2026' }
    ], totalPages: 1, totalCount: 1 }));
    await flush();
    h.get('sectionId').value = '10';
    h.get('sectionId').trigger('change');
    call(h, '/api/student-attendance/subjects?').resolve(response({
        items: [], totalPages: 0, totalCount: 0
    }));
    await flush();
    h.get('attendanceFilters').trigger('submit', { preventDefault() {} });
    call(h, '/api/student-attendance/roster-page?').resolve(response({
        attendanceSessionId: 22, sessionRowVersion: 'v1', isFinalized: false,
        isHistoricalReadOnly: false, totalPages: 1,
        summary: { totalStudents: 1, marked: 1, unmarked: 0, present: 1 },
        students: [{ studentCode: 'ST-01', studentName: 'Student', studentEnrollmentReference: 'R',
            state: 1, rowVersion: 's1', id: 1 }]
    }));
    await flush();
    assert.equal(h.get('attendanceRows').children.length, 1);
    h.get('attendanceRows').trigger('change');
    const previousDate = h.get('attendanceDate').value;
    h.get('attendanceDate').value = '2028-01-01';
    h.get('attendanceDate').trigger('change');
    assert.equal(h.get('attendanceDate').value, previousDate);
    assert.equal(h.get('attendanceRows').children.length, 1);
    assert.match(h.get('attendanceSummary').textContent, /Unsaved edits on this page/);
    assert.equal(h.calls.filter(x => x.url.includes('/api/student-attendance/batches?')).length, 1);
});

test('An old exam preview is never published or displayed for another selected assessment', async () => {
    const h = examHarness();
    call(h, '/api/assessment-administration/scopes/page?').resolve(response({
        items: [
            { assessmentId: 1, assessmentName: 'Old', academicBatchId: 10, academicBatchName: 'Batch',
                assessmentSubjectId: 100, subjectName: 'Math', state: 5 },
            { assessmentId: 2, assessmentName: 'New', academicBatchId: 10, academicBatchName: 'Batch',
                assessmentSubjectId: 200, subjectName: 'English', state: 5 }
        ], page: 1, totalCount: 2
    }));
    await flush();
    const choose = (id, subject) => {
        h.get('examExam').value = String(id);h.get('examExam').trigger('change');
        h.get('examBatch').value = '10';h.get('examBatch').trigger('change');
        h.get('examSubject').value = String(subject);h.get('examSubject').trigger('change');
    };
    choose(1, 100);
    h.get('examGenerate').trigger('click');
    choose(2, 200);
    call(h, '/results/preview').resolve(response({ assessmentName: 'Old',
        results: [{ studentName: 'Old student', totalMarks: 50, maximumMarks: 100 }] }));
    await flush();
    assert.doesNotMatch(h.get('examResultSummary').textContent, /Old/);
    assert.equal(h.get('examPublish').disabled, true);
    h.get('examGenerate').trigger('click');
    call(h, '/results/preview', 1).resolve(response({ assessmentName: 'New',
        results: [{ studentName: 'New student', totalMarks: 90, maximumMarks: 100 }] }));
    await flush();
    assert.match(h.get('examResultSummary').textContent, /New/);
    assert.equal(h.get('examPublish').disabled, false);
});

test('Admissions, attendance, fees and dashboard preserve transaction and selection guards', () => {
    assert.match(admissions, /const request = \+\+state\.intakeRequest/);
    assert.match(admissions, /if \(request !== state\.intakeRequest \|\| !sameSession\(\)\) return;/);
    assert.match(admissions, /if \(list\.dataset\.mutatingIntake\)/);
    assert.match(attendance, /acceptedDate = date\.value; acceptedBatch = ''; acceptedSubject = ''/);
    assert.match(attendance, /dirty && !confirm\('Discard unsaved attendance before changing subjects\?'\)/);
    assert.match(attendance, /roster\?\.isHistoricalReadOnly \|\| roster\?\.isFinalized/);
    assert.match(fees, /state\.paymentBusy=true;e\('feeCollect'\)\.disabled=true/);
    assert.match(fees, /state\.billingBusy=true;e\('feeGenerate'\)\.disabled=true/);
    assert.match(fees, /state\.structureRefreshFailed=true/);
    assert.match(fees, /e\('feeSaveStructure'\)\.disabled=state\.structureRefreshFailed/);
    assert.match(fees, /request!==state\.registerRequest/);
    assert.match(fees, /request!==state\.editorRequest/);
    assert.match(examWorkflow, /state\.sheetScope !== resultScopeKey\(selected\(\)\)/);
    assert.match(examWorkflow, /if \(request !== state\.resultRequest \|\| !sameScope\(s\)\) return;/);
    assert.match(examWorkflow, /if \(request !== state\.readinessRequest \|\| !sameScope\(s\)\) return;/);
    assert.match(examWorkflow, /request !== state\.rosterRequest \|\| !sameScope\(s\)/);
    assert.match(examWorkflow, /state\.publishKeyScope = resultScopeKey\(s\)/);
    assert.match(dashboard, /href="\/AssessmentAdministration\/Marks"/);
    assert.match(dashboard, /href="\/PayrollOperations\/Index"/);
});
