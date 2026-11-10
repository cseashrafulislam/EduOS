'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const repo = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(repo, name), 'utf8');
const tick = () => new Promise(resolve => setImmediate(resolve));
async function settle() { for (let n = 0; n < 12; n++) await tick(); }

function browser(rootId, dataset) {
    const elements = new Map();
    let discard = true;
    const element = tag => ({ tagName: String(tag).toUpperCase(), type: 'text', value: '',
        checked: false, disabled: false, inert: false, textContent: '', children: [],
        handlers: {}, classList: { add() {}, remove() {}, toggle() {} },
        addEventListener(name, listener) { this.handlers[name] = listener; },
        append(...nodes) { this.children.push(...nodes); },
        replaceChildren(...nodes) { this.children = nodes; },
        add(node) { this.children.push(node); },
        remove() { this.removed = true; },
        querySelector() { return { value: 'csrf-token' }; },
        querySelectorAll() { return []; }
    });
    const get = id => {
        if (!elements.has(id)) elements.set(id, element('div'));
        return elements.get(id);
    };
    const root = get(rootId);
    root.dataset = dataset;
    const document = { documentElement: { lang: 'en' },
        body: { dataset: { authTenantId: 'tenant-1', authSessionReference: 'session-1' } },
        getElementById: get, createElement: element,
        querySelector() { return { content: 'csrf-token' }; }, addEventListener() {} };
    function form(id, fields, defaults = {}) {
        const container = get(id);
        container.tagName = 'FORM';
        container.elements = fields.map(field => {
            const input = get(field);
            input.tagName = field.includes('Summary') || field.includes('Statement') ||
                field.includes('Note') || field.includes('Reason') ? 'TEXTAREA' : 'INPUT';
            input.type = field === 'caseUrgent' || field === 'caseConsent' ? 'checkbox' : 'text';
            input.value = defaults[field] || '';
            return input;
        });
        container.reset = () => {
            for (const field of fields) {
                get(field).value = defaults[field] || '';
                get(field).checked = false;
            }
        };
        return container;
    }
    const window = { document, confirm: () => discard, addEventListener() {} };
    vm.runInNewContext(read('EduOS.App/wwwroot/js/platform/form-guard.js'), { window });
    return { get, form, document, window, setDiscard: value => { discard = value; } };
}

function response(data, success = true) {
    return { ok: success, status: success ? 200 : 503,
        async json() { return success ? { success: true, data } : { success: false, message: 'Uncertain response' }; } };
}

test('Counseling keeps confidential draft and immutable retry ID on failed writes, and asks before switching students', async () => {
    const { get, form, document, window, setDiscard } = browser('counselingApp', {
        officer: 'false', counselor: 'true', admin: 'false', aggregate: 'false'
    });
    form('caseSubmitForm', ['caseStudent', 'caseSummary', 'caseUrgent', 'caseConsent']);
    form('caseActionForm', ['caseAction', 'caseAppointment', 'caseNote'], { caseAction: 'Note' });
    form('caseRedactForm', ['caseRedactReason']);
    const posted = [];
    const students = [{ studentReference: 'student-a', studentName: 'A', studentCode: 'S1' },
        { studentReference: 'student-b', studentName: 'B', studentCode: 'S2' }];
    const fetch = async (url, options = {}) => {
        const address = String(url);
        if (address === '/api/student-counseling' && options.method === 'POST') {
            posted.push(JSON.parse(options.body));
            return response({ reference: 'case-1' }, posted.length === 3);
        }
        if (address.startsWith('/api/student-support-directory/enrollments?')) return response(students);
        if (address === '/api/student-counseling/policy-campuses') return response([]);
        if (address.startsWith('/api/student-counseling/mine?')) return response({ items: [] });
        if (address.startsWith('/api/student-counseling?')) return response({ items: [], totalCount: 0 });
        throw Error('Unexpected counseling request: ' + address);
    };
    let seq = 0;
    vm.runInNewContext(read('EduOS.App/wwwroot/js/students/student-counseling.js'), {
        document, window, fetch, URLSearchParams, crypto: { randomUUID: () => 'key-' + ++seq }
    });
    await settle();
    assert.equal(get('caseStudents').children.length, 2, 'initial lookup is no longer dropped behind the staff worklist');
    get('caseStudents').children[0].handlers.click();
    get('caseSummary').value = 'Confidential original description';
    get('caseSubmitForm').handlers.submit({ preventDefault() {} });
    await settle();
    assert.equal(get('caseSummary').value, 'Confidential original description', 'failed write must retain private notes');
    get('caseSubmitForm').handlers.submit({ preventDefault() {} });
    await settle();
    assert.equal(posted[0].requestId, posted[1].requestId, 'same payload after uncertain response keeps request ID');
    get('caseSummary').value = 'Confidential description with new evidence';
    get('caseSubmitForm').handlers.submit({ preventDefault() {} });
    await settle();
    assert.notEqual(posted[1].requestId, posted[2].requestId, 'changed private summary rotates request ID');
    assert.equal(get('caseSummary').value, '', 'clear only after server confirms success');
    get('caseSummary').value = 'Another confidential unsaved draft';
    get('caseStudentSearchForm').handlers.submit({ preventDefault() {} });
    await settle();
    setDiscard(false);
    get('caseStudents').children[1].handlers.click();
    assert.equal(get('caseStudent').value, 'A', 'cancelled switch must preserve selected student');
    assert.equal(get('caseSummary').value, 'Another confidential unsaved draft');
    setDiscard(true);
    get('caseStudents').children[1].handlers.click();
    assert.equal(get('caseStudent').value, 'B');
    assert.equal(get('caseSummary').value, '');
});

test('Scholarship keeps evidence-linked draft on failures, changes retry ID with payload and protects enrollment switches', async () => {
    const { get, form, document, window, setDiscard } = browser('scholarshipsApp', { reviewer: 'false' });
    form('aidSubmitForm', ['aidEnrollment', 'aidMode', 'aidRule', 'aidCashAmount', 'aidFrom', 'aidTo',
        'aidStatement', 'aidEvidenceFile', 'aidEvidence', 'aidRenewal'], { aidMode: 'FeeCredit' });
    get('aidEvidenceFile').files = [];
    const posted = [];
    const students = [{ enrollmentReference: 'enrollment-a', studentName: 'A', studentCode: 'S1', rollNo: 'R1' },
        { enrollmentReference: 'enrollment-b', studentName: 'B', studentCode: 'S2', rollNo: 'R2' }];
    const fetch = async (url, options = {}) => {
        const address = String(url);
        if (address === '/api/student-scholarships' && options.method === 'POST') {
            posted.push(JSON.parse(options.body));
            return response({ reference: 'aid-1' }, posted.length === 3);
        }
        if (address.startsWith('/api/student-support-directory/enrollments?')) return response(students);
        if (address === '/api/student-support-directory/aid-rules')
            return response([{ id: 7, name: 'Policy', value: 10, isPercentage: true }]);
        if (address.startsWith('/api/student-scholarships?')) return response({ items: [], totalCount: 0 });
        throw Error('Unexpected scholarship request: ' + address);
    };
    let seq = 0;
    const Option = (label, value) => ({ label, value });
    vm.runInNewContext(read('EduOS.App/wwwroot/js/students/student-scholarships.js'), {
        document, window, fetch, URLSearchParams, Option,
        crypto: { randomUUID: () => 'key-' + ++seq }
    });
    await settle();
    assert.equal(get('aidStudents').children.length, 2);
    get('aidStudents').children[0].handlers.click();
    get('aidRule').value = '7';
    get('aidStatement').value = 'Eligible for hardship assistance';
    get('aidSubmitForm').handlers.submit({ preventDefault() {} });
    await settle();
    assert.equal(get('aidStatement').value, 'Eligible for hardship assistance', 'failed write retains eligibility reason');
    get('aidSubmitForm').handlers.submit({ preventDefault() {} });
    await settle();
    assert.equal(posted[0].requestId, posted[1].requestId);
    get('aidStatement').value = 'Eligible for hardship assistance plus new documentation';
    get('aidSubmitForm').handlers.submit({ preventDefault() {} });
    await settle();
    assert.notEqual(posted[1].requestId, posted[2].requestId);
    assert.equal(get('aidStatement').value, '');
    get('aidStatement').value = 'Next unsaved request';
    get('aidStudentSearchForm').handlers.submit({ preventDefault() {} });
    await settle();
    setDiscard(false);
    get('aidStudents').children[1].handlers.click();
    assert.match(get('aidEnrollment').value, /^A \/ R1$/);
    assert.equal(get('aidStatement').value, 'Next unsaved request');
    setDiscard(true);
    get('aidStudents').children[1].handlers.click();
    assert.match(get('aidEnrollment').value, /^B \/ R2$/);
    assert.equal(get('aidStatement').value, '');
});

test('Sensitive Student Support form guards load before their page scripts and Finance retries are action-scoped', () => {
    for (const name of ['Counseling', 'Scholarships']) {
        const view = read(`EduOS.App/Views/StudentSupport/${name}.cshtml`);
        assert.match(view, /form-guard\.js"[\s\S]*student-(counseling|scholarships)\.js"/);
    }
    const cases = read('EduOS.App/wwwroot/js/students/student-counseling.js');
    assert.match(cases, /if \(!success\) return;/);
    assert.match(cases, /policyGuard\.confirmDiscard\(\)/);
    const aid = read('EduOS.App/wwwroot/js/students/student-scholarships.js');
    for (const slot of ['aid-cash-prepare', 'aid-cash-review', 'aid-cash-reverse', 'aid-cash-reverse-review'])
        assert.ok(aid.includes(`retryId('${slot}'`));
    assert.match(aid, /confirmPaid.*reason.*aidCashReason/);
    assert.match(aid, /confirmReversed.*reason.*aidCashReason/);
});
