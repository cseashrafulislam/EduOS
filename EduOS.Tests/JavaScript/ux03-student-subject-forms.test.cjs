'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const script = read('EduOS.App/wwwroot/js/students/optional-subjects.js');
const reference = '00000000-0000-4000-8000-000000000001';
const enrollment = '00000000-0000-4000-8000-000000000002';
const offering = '00000000-0000-4000-8000-000000000003';

function harness({ holdWorkspace = false, registrations = [] } = {}) {
    const elements = new Map(), writes = [], pending = [];
    let failWorkspace = false, uid = 0, dirty = false, discard = true;
    class Element {
        constructor(tag = 'div') {
            this.tagName = tag.toUpperCase(); this.value = ''; this.textContent = '';
            this.className = ''; this.children = []; this.options = this.children;
            this.handlers = {}; this.disabled = false; this.inert = false; this.hidden = false;
            this.dataset = { canManage: 'true' };
            this.classList = { add: () => { this.hidden = true; }, remove: () => { this.hidden = false; } };
        }
        addEventListener(type, handler) { this.handlers[type] = handler; }
        append(...values) { this.children.push(...values); }
        add(value) { this.children.push(value); }
        replaceChildren(...values) {
            this.children = values; this.options = this.children;
            if (values.some(x => x instanceof Option)) this.value = values[0].value;
        }
        reset() { get('subjectsOffering').value = ''; get('subjectsRemarks').value = ''; }
        dispatchEvent() {}
        querySelector() { return { value: 'csrf' }; }
    }
    class Option {
        constructor(text, value) { this.text = text; this.value = value; this.disabled = false; }
    }
    const get = id => {
        if (!elements.has(id)) elements.set(id, new Element());
        return elements.get(id);
    };
    const document = { body: { dataset: { authTenantId: '1', authSessionReference: 'session-one' } },
        getElementById: get, createElement: tag => new Element(tag) };
    const workspace = { studentReference: reference, enrollmentReference: enrollment,
        studentName: 'Student One', studentCode: 'S001', rollNo: 'R01',
        offerings: [{ reference: offering, code: 'MATH', subjectName: 'Math', isEligible: true,
            seatsRemaining: 20, capacity: 30, missingPrerequisites: [] }], registrations };
    const ok = data => ({ ok: true, async json() { return { success: true, data }; } });
    const fail = message => ({ ok: false, status: 503,
        async json() { return { success: false, message }; } });
    const fetch = async (url, options = {}) => {
        url = String(url);
        if (options.method === 'POST') return new Promise(resolve => writes.push({
            url, body: JSON.parse(options.body), resolve
        }));
        if (url.startsWith('/api/student-optional-subjects/students/')) {
            if (holdWorkspace) return new Promise(resolve => pending.push(resolve));
            return failWorkspace ? fail('Workspace unavailable') : ok(workspace);
        }
        if (url.startsWith('/api/students?')) return ok({ items: [{
            reference, studentCode: 'S001', fullName: 'Student One'
        }] });
        throw Error('Unexpected request ' + url);
    };
    const window = { EduOSFormGuard: { create: () => ({
        isDirty: () => dirty, confirmDiscard: () => discard, markClean: () => { dirty = false; }
    }) }, EduOSSessionTransitionPending: () => false };
    vm.runInNewContext(script, { window, document, fetch, Option, URLSearchParams,
        location: { search: '?studentReference=' + reference },
        crypto: { randomUUID: () => '00000000-0000-4000-8000-' + String(++uid).padStart(12, '0') },
        confirm: () => true, CustomEvent: class { constructor(type, args) { this.type = type; this.detail = args.detail; } },
        Date });
    const flush = async () => { for (let i = 0; i < 40; i++) await Promise.resolve(); };
    const submit = () => get('subjectsRequestForm').handlers.submit({ preventDefault() {} });
    function fill() { get('subjectsOffering').value = offering; get('subjectsRemarks').value = 'Need elective'; }
    return { get, document, workspace, pending, writes, flush, submit, fill, ok, fail,
        set failWorkspace(value) { failWorkspace = value; }, set dirty(value) { dirty = value; },
        set discard(value) { discard = value; } };
}

test('optional subject request locks duplicate POST and retains immutable retry identity', async () => {
    const h = harness(); await h.flush(); h.fill();
    h.submit(); h.submit(); await h.flush();
    assert.equal(h.writes.length, 1); assert.equal(h.get('subjectsRequestForm').inert, true);
    h.writes[0].resolve(h.fail('Network uncertainty')); await h.flush();
    h.submit(); await h.flush();
    assert.equal(h.writes[1].body.clientRequestId, h.writes[0].body.clientRequestId);
    h.writes[1].resolve(h.fail('Network uncertainty')); await h.flush();
    h.get('subjectsRemarks').value = 'New reasoning'; h.submit(); await h.flush();
    assert.notEqual(h.writes[2].body.clientRequestId, h.writes[1].body.clientRequestId);
    h.writes[2].resolve(h.fail('Network uncertainty')); await h.flush();
});

test('confirmed optional subject request never becomes a false failed write on readback outage', async () => {
    const h = harness(); await h.flush(); h.fill(); h.submit(); await h.flush();
    h.failWorkspace = true;
    h.writes[0].resolve(h.ok({})); await h.flush();
    assert.match(h.get('subjectsNotice').textContent, /Request saved, but the workspace could not be refreshed/);
    assert.equal(h.get('subjectsRequestButton').disabled, true);
    h.submit(); await h.flush(); assert.equal(h.writes.length, 1);
});

test('approved subject decision and failed refresh lock stale action buttons', async () => {
    const h = harness({ registrations: [{ id: 10, state: 1, subjectName: 'Math',
        registeredAt: '2026-11-10', rowVersion: 'version' }] });
    await h.flush();
    h.get('subjectsRegistrations').children[0].children[3].children[0].handlers.click();
    await h.flush(); assert.equal(h.writes.length, 1);
    h.failWorkspace = true; h.writes[0].resolve(h.ok({})); await h.flush();
    assert.match(h.get('subjectsNotice').textContent, /Decision saved, but readback failed/);
    assert.equal(h.get('subjectsRequestButton').disabled, true);
});

test('old institution elective data never opens request UI after tenant switch', async () => {
    const h = harness({ holdWorkspace: true }); await h.flush();
    assert.equal(h.pending.length, 1);
    h.document.body.dataset.authTenantId = '2';
    h.pending[0](h.ok(h.workspace)); await h.flush();
    assert.equal(h.get('subjectsStudent').hidden, true);
    h.fill(); h.submit(); await h.flush();
    assert.equal(h.writes.length, 0);
});

test('reselecting an elective student cannot erase a rejected discard of unsaved remarks', async () => {
    const h = harness(); await h.flush(); h.fill();
    h.dirty = true; h.discard = false;
    h.get('subjectsSearch').value = 'Student One';
    h.get('subjectsSearchForm').handlers.submit({ preventDefault() {} }); await h.flush();
    h.get('subjectsMatches').children[0].handlers.click(); await h.flush();
    assert.equal(h.get('subjectsRemarks').value, 'Need elective');
    assert.equal(h.get('subjectsOffering').value, offering);
});

test('elective form reuses canonical authorized request and versioned decision contracts', () => {
    const view = read('EduOS.App/Views/StudentSubjects/Index.cshtml');
    const controller = read('EduOS.App/Controllers/Api/StudentOptionalSubjectsController.cs');
    assert.match(view, /form-guard\.js"[\s\S]*students\/optional-subjects\.js"/);
    assert.match(script, /requestFingerprint!==fingerprint/);
    assert.match(script, /rowVersion:item\.rowVersion/);
    assert.match(controller, /Authorize\(Roles = "TenantAdmin,Principal,VicePrincipal,Student,Guardian,Parent"\)/);
    assert.match(controller, /AutoValidateAntiforgeryToken/);
});
