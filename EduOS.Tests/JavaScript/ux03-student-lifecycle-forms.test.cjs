'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.resolve(__dirname,
    '../../EduOS.App/wwwroot/js/students/lifecycle.js'), 'utf8');
const reference = '00000000-0000-4000-8000-000000000001';
const otherReference = '00000000-0000-4000-8000-000000000002';

function harness({ holdStudent = false } = {}) {
    const elements = new Map(), writes = [], pendingStudents = [], gets = [];
    let historyFailure = false, counter = 0, dirty = false, discard = true;
    class Element {
        constructor(tag = 'div') {
            this.tagName = tag.toUpperCase(); this.children = []; this.handlers = {};
            this.value = ''; this.textContent = ''; this.className = '';
            this.disabled = false; this.inert = false; this.hidden = false;
            this.dataset = { bn: 'false' };
            this.classList = { add: () => { this.hidden = true; }, remove: () => { this.hidden = false; } };
        }
        addEventListener(type, handler) { this.handlers[type] = handler; }
        append(...children) { this.children.push(...children); }
        replaceChildren(...children) { this.children = children; }
        reset() {
            get('lifecycleExitType').value = 'Transfer';
            get('lifecycleReason').value = '';
            get('lifecycleConduct').value = '';
        }
        querySelector() { return { value: 'csrf' }; }
        scrollIntoView() {}
    }
    const get = id => {
        if (!elements.has(id)) elements.set(id, new Element());
        return elements.get(id);
    };
    get('lifecycleExitType').value = 'Transfer';
    const document = { body: { dataset: { authTenantId: '1', authSessionReference: 'session-one' } },
        getElementById: get, createElement: tag => new Element(tag) };
    const ok = data => ({ ok: true, status: 200, async json() { return { success: true, data }; } });
    const fail = (message, status = 503) => ({ ok: false, status,
        async json() { return { success: false, message }; } });
    const student = ref => ({ reference: ref, fullName: 'Student', studentCode: 'S001',
        status: 'Active', rowVersion: 'version', enrollments: [] });
    const fetch = async (url, options = {}) => {
        url = String(url);
        if (options.method === 'POST') return new Promise(resolve => writes.push({
            url, body: JSON.parse(options.body), resolve
        }));
        gets.push(url);
        if (url.startsWith('/api/students?')) return ok({ items: [{
            reference: otherReference, fullName: 'Student Two', studentCode: 'S002', status: 'Active'
        }] });
        if (url.includes('/exit/') && url.endsWith('/history'))
            return historyFailure ? fail('History unavailable') : ok([]);
        if (url.endsWith('/' + reference) || url.endsWith('/' + otherReference)) {
            if (holdStudent) return new Promise(resolve => pendingStudents.push(resolve));
            return ok(student(url.endsWith('/' + reference) ? reference : otherReference));
        }
        throw Error('Unexpected request ' + url);
    };
    const window = { EduOSFormGuard: { create: () => ({
        isDirty: () => dirty, confirmDiscard: () => discard, markClean: () => { dirty = false; }
    }) }, EduOSSessionTransitionPending: () => false };
    vm.runInNewContext(source, { window, document, fetch, URLSearchParams,
        location: { search: '?studentReference=' + reference }, confirm: () => true,
        crypto: { randomUUID: () => '00000000-0000-4000-8000-' + String(++counter).padStart(12, '0') },
        Uint8Array, Date, Promise });
    const flush = async () => { for (let i = 0; i < 45; i++) await Promise.resolve(); };
    const submit = id => get(id).handlers.submit({ preventDefault() {} });
    return { get, document, gets, writes, pendingStudents, student, ok, fail, submit, flush,
        set historyFailure(value) { historyFailure = value; },
        set dirty(value) { dirty = value; }, set discard(value) { discard = value; } };
}

test('final-exit form locks duplicate submits and reuses unchanged uncertain request ID', async () => {
    const h = harness(); await h.flush();
    h.get('lifecycleReason').value = 'Family relocation';
    h.get('lifecycleReason').handlers.input();
    h.submit('lifecycleExitForm'); h.submit('lifecycleExitForm'); await h.flush();
    assert.equal(h.writes.length, 1);
    assert.equal(h.get('lifecycleExitForm').inert, true);
    assert.equal(h.get('lifecycleSearchSubmit').disabled, true);
    h.writes[0].resolve(h.fail('Network uncertainty')); await h.flush();
    assert.match(h.get('lifecycleAlert').textContent, /Retry unchanged details with the same request ID/);
    h.submit('lifecycleExitForm'); await h.flush();
    assert.equal(h.writes.length, 2);
    assert.equal(h.writes[0].body.clientRequestId, h.writes[1].body.clientRequestId);
    h.writes[1].resolve(h.fail('Network uncertainty')); await h.flush();
    h.get('lifecycleReason').value = 'New evidence';
    h.submit('lifecycleExitForm'); await h.flush();
    assert.notEqual(h.writes[1].body.clientRequestId, h.writes[2].body.clientRequestId);
    h.writes[2].resolve(h.fail('Network uncertainty')); await h.flush();
});

test('confirmed final exit remains successful when student history readback fails', async () => {
    const h = harness(); await h.flush();
    h.get('lifecycleReason').value = 'Completed academic course';
    h.submit('lifecycleExitForm'); await h.flush();
    h.historyFailure = true;
    h.writes[0].resolve(h.ok({ certificateNo: 'TC-42' })); await h.flush();
    assert.match(h.get('lifecycleAlert').textContent, /exit saved, but status refresh failed/i);
    assert.equal(h.get('lifecycleSubmit').disabled, true);
    assert.equal(h.get('lifecycleReason').value, '');
    assert.equal(h.get('lifecycleExitForm').inert, false);
    h.submit('lifecycleExitForm'); await h.flush();
    assert.equal(h.writes.length, 1, 'confirmed exit must not be resubmitted on failed readback');
});

test('dirty final-exit draft cannot be silently replaced by another student search result', async () => {
    const h = harness(); await h.flush();
    h.dirty = true; h.discard = false;
    h.get('lifecycleSearchText').value = 'Student Two';
    h.submit('lifecycleStudentSearch'); await h.flush();
    const reads = h.gets.length;
    h.get('lifecycleMatches').children[0].handlers.click(); await h.flush();
    assert.equal(h.gets.length, reads, 'canceling discard must not request or reveal another student');
});

test('late final-exit read from switched institution never exposes the old student', async () => {
    const h = harness({ holdStudent: true }); await h.flush();
    assert.equal(h.pendingStudents.length, 1);
    h.document.body.dataset.authTenantId = '2';
    h.pendingStudents[0](h.ok(h.student(reference))); await h.flush();
    assert.equal(h.get('lifecycleStudent').hidden, true);
    assert.equal(h.get('lifecycleSubmit').disabled, true);
    h.get('lifecycleReason').value = 'Old institution reason';
    h.submit('lifecycleExitForm'); await h.flush();
    assert.equal(h.writes.length, 0);
});

test('final exit keeps antiforgery, authorized server workflow and shared draft guard', () => {
    const view = fs.readFileSync(path.resolve(__dirname,
        '../../EduOS.App/Views/StudentLifecycle/Index.cshtml'), 'utf8');
    const controller = fs.readFileSync(path.resolve(__dirname,
        '../../EduOS.App/Controllers/Api/StudentExitController.cs'), 'utf8');
    assert.match(view, /form-guard\.js"[\s\S]*students\/lifecycle\.js"/);
    assert.match(view, /id="lifecycleSearchSubmit"/);
    assert.match(source, /RequestVerificationToken: token/);
    assert.match(source, /exitGuard\.confirmDiscard\(\)/);
    assert.match(controller, /Authorize\(Roles = "TenantAdmin"\)/);
    assert.match(controller, /AutoValidateAntiforgeryToken/);
});
