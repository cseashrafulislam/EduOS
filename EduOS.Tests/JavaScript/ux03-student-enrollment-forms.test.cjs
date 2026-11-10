'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const script = read('EduOS.App/wwwroot/js/students/enrollment-management.js');
const studentRef = '00000000-0000-4000-8000-000000000001';

function harness() {
    const elements = new Map(), writes = [], pending = [];
    let counter = 0, dirty = false, allowDiscard = true, holdCurrent = false;
    class Element {
        constructor(tag = 'div') {
            this.tagName = tag.toUpperCase(); this.value = ''; this.textContent = '';
            this.children = []; this.options = this.children; this.handlers = {};
            this.disabled = false; this.inert = false; this.hidden = false; this.className = '';
            this.classList = { add: () => { this.hidden = true; }, remove: () => { this.hidden = false; } };
        }
        addEventListener(type, handler) { this.handlers[type] = handler; }
        append(...values) { this.children.push(...values); }
        add(value) { this.children.push(value); }
        replaceChildren(...values) {
            this.children = values; this.options = this.children;
            if (this.tagName === 'DIV' || values.some(x => x instanceof Option)) this.value = values[0]?.value || '';
        }
        reset() {
            for (const id of ['enrollmentBatchSearch', 'enrollmentRoll', 'enrollmentDate', 'enrollmentRemarks',
                'enrollmentBatch', 'enrollmentCurriculum']) get(id).value = '';
        }
        querySelector() { return { value: 'csrf' }; }
        scrollIntoView() {}
    }
    class Option { constructor(text, value) { this.text = text; this.value = value; } }
    const get = id => {
        if (!elements.has(id)) elements.set(id, new Element());
        return elements.get(id);
    };
    const document = { body: { dataset: { authTenantId: '1', authSessionReference: 'session-one' } },
        getElementById: get, createElement: tag => new Element(tag) };
    const ok = data => ({ ok: true, status: 200, async json() { return { success: true, data }; } });
    const fail = message => ({ ok: false, status: 503, async json() { return { success: false, message }; } });
    const fetch = async (url, options = {}) => {
        url = String(url);
        if (options.method === 'POST') return new Promise(resolve => writes.push({
            url, body: JSON.parse(options.body), resolve
        }));
        if (url.startsWith('/api/academic-setup/catalog')) return ok({ batches: [{
            id: 10, name: 'G9-A', code: 'G9A', isActive: true, capacity: 50
        }] });
        if (url.startsWith('/api/academic-enrollments/batches/')) return ok([{ id: 20,
            name: 'General', isActive: true, isCurrent: true }]);
        if (url.startsWith('/api/academic-enrollments/students/')) {
            if (holdCurrent) return new Promise(resolve => pending.push(resolve));
            return { ok: false, status: 404 };
        }
        if (url.startsWith('/api/students?')) return ok({ items: [{
            reference: studentRef, studentCode: 'S001', fullName: 'Student One'
        }] });
        throw Error('Unexpected URL ' + url);
    };
    const window = { EduOSFormGuard: { create: () => ({
        isDirty: () => dirty, confirmDiscard: () => allowDiscard, markClean: () => { dirty = false; }
    }) }, EduOSSessionTransitionPending: () => false, dispatchEvent() {} };
    vm.runInNewContext(script, { document, window, fetch, URLSearchParams, Option,
        crypto: { randomUUID: () => '00000000-0000-4000-8000-' + String(++counter).padStart(12, '0') },
        CustomEvent: class { constructor(type, options) { this.type = type; this.detail = options.detail; } },
        Uint8Array, Date });
    const flush = async () => { for (let i = 0; i < 35; i++) await Promise.resolve(); };
    async function choose() {
        get('enrollmentStudentSearch').value = 'Student One';
        get('enrollmentStudentSearchForm').handlers.submit({ preventDefault() {} });
        await flush();
        get('enrollmentStudentResults').children[0].handlers.click();
        await flush();
    }
    async function fill() {
        get('enrollmentBatch').value = '10'; get('enrollmentBatch').handlers.change();
        get('enrollmentDate').value = '2026-11-10'; get('enrollmentDate').handlers.input();
        await flush();
        get('enrollmentCurriculum').value = '20'; get('enrollmentCurriculum').handlers.change();
        get('enrollmentRoll').value = 'R001'; get('enrollmentRoll').handlers.input();
    }
    return { get, document, writes, pending, ok, fail, flush, choose, fill,
        submit: () => get('enrollmentForm').handlers.submit({ preventDefault() {} }),
        set dirty(value) { dirty = value; }, set allowDiscard(value) { allowDiscard = value; },
        set holdCurrent(value) { holdCurrent = value; } };
}

test('enrollment creates one immutable command and preserves retry key for uncertain POST', async () => {
    const h = harness(); await h.flush(); await h.choose(); await h.fill();
    assert.equal(h.get('enrollmentSubmit').disabled, false);
    h.submit(); h.submit(); await h.flush();
    assert.equal(h.writes.length, 1); assert.equal(h.get('enrollmentForm').inert, true);
    h.writes[0].resolve(h.fail('Network uncertainty')); await h.flush();
    h.submit(); await h.flush();
    assert.equal(h.writes[0].body.clientRequestId, h.writes[1].body.clientRequestId);
    h.writes[1].resolve(h.fail('Network uncertainty')); await h.flush();
    h.get('enrollmentRoll').value = 'R002'; h.get('enrollmentRoll').handlers.input();
    h.submit(); await h.flush();
    assert.notEqual(h.writes[1].body.clientRequestId, h.writes[2].body.clientRequestId);
    h.writes[2].resolve(h.fail('Network uncertainty')); await h.flush();
});

test('confirmed enrollment disables a second save and retains the confirmed roll', async () => {
    const h = harness(); await h.flush(); await h.choose(); await h.fill();
    h.submit(); await h.flush();
    h.writes[0].resolve(h.ok({ rollNo: 'R001' })); await h.flush();
    assert.match(h.get('enrollmentNotice').textContent, /saved successfully/i);
    assert.match(h.get('enrollmentCurrent').textContent, /R001/);
    assert.equal(h.get('enrollmentSubmit').disabled, true);
    h.submit(); await h.flush();
    assert.equal(h.writes.length, 1);
});

test('enrollment selection rejects a stale tenant response and never enables the writer', async () => {
    const h = harness(); await h.flush(); h.holdCurrent = true;
    await h.choose(); assert.equal(h.pending.length, 1);
    h.document.body.dataset.authTenantId = '2';
    h.pending[0]({ ok: false, status: 404 }); await h.flush();
    assert.equal(h.get('enrollmentSubmit').disabled, true);
    await h.fill(); h.submit(); await h.flush();
    assert.equal(h.writes.length, 0);
});

test('searching or reselecting never erases an unconfirmed enrollment draft', async () => {
    const h = harness(); await h.flush(); await h.choose(); await h.fill();
    h.dirty = true; h.allowDiscard = false;
    await h.choose();
    assert.equal(h.get('enrollmentRoll').value, 'R001');
    h.get('enrollmentBatchSearch').value = 'Another Section';
    h.get('enrollmentBatchSearchButton').handlers.click(); await h.flush();
    assert.equal(h.get('enrollmentRoll').value, 'R001');
    assert.equal(h.get('enrollmentBatch').value, '10');
});

test('enrollment preserves server authority, bounded catalog and shared unsaved guard', () => {
    const view = read('EduOS.App/Views/EnrollmentManagement/Index.cshtml');
    const api = read('EduOS.App/Controllers/Api/AcademicEnrollmentsController.cs');
    assert.match(view, /form-guard\.js"[\s\S]*students\/enrollment-management\.js"/);
    assert.match(script, /guard\.confirmDiscard\(\)/);
    assert.match(script, /take=100/);
    assert.match(script, /RequestVerificationToken: token/);
    assert.match(api, /AutoValidateAntiforgeryToken/);
});
