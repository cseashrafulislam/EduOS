'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');
const source = file => read('EduOS.App/wwwroot/js/students/' + file);
const studentReference = '00000000-0000-4000-8000-000000000001';
const siblingReference = '00000000-0000-4000-8000-000000000002';
const guardianReference = '00000000-0000-4000-8000-000000000003';

function deferred() {
    let resolve, reject;
    const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
    return { promise, resolve, reject };
}
function harness(file, name, api = async () => {}) {
    const elements = new Map(), guards = [], messages = [], renders = [], renderBusy = [];
    const events = {};
    let current = true, counter = 0, allowDiscard = true;
    class Element {
        constructor() {
            this.value = ''; this.checked = false; this.disabled = false;
            this.inert = false; this.textContent = ''; this.children = []; this.listeners = {};
            this.hidden = false; this.dirty = false; this.resetCount = 0;
            this.classList = { add: name => { if (name === 'd-none') this.hidden = true; },
                remove: name => { if (name === 'd-none') this.hidden = false; },
                contains: name => name === 'd-none' && this.hidden };
        }
        addEventListener(name, fn) { this.listeners[name] = fn; }
        reportValidity() { return true; }
        reset() { this.value = ''; this.resetCount++; this.dirty = false; }
        replaceChildren(...children) { this.children = children; }
        append(child) { this.children.push(child); }
        get childElementCount() { return this.children.length; }
    }
    const $ = id => { if (!elements.has(id)) elements.set(id, new Element()); return elements.get(id); };
    const window = { EduOSFormGuard: { create(form) {
        const guard = { isDirty: () => form.dirty,
            confirmDiscard: () => allowDiscard, markClean: () => { form.dirty = false; } };
        guards.push(guard); return guard;
    } } };
    const document = { events, addEventListener: (type, callback) => { events[type] = callback; },
        createElement: () => new Element() };
    const setup = vm.runInNewContext(source(file).replace('export function', 'function') + '\n' + name,
        { window, document, URLSearchParams, crypto: { randomUUID: () => '00000000-0000-4000-8000-' + String(++counter).padStart(12, '0') } });
    const state = { student: { studentReference, studentRowVersion: 'v1' }, requestId: null,
        requestFingerprint: null, guards: [], busy: false, selecting: false, requiresRefresh: false,
        searchVersion: 0, selectionVersion: 0, sameSession: () => current,
        refreshActions() {}, confirmStudentSwitch: () => allowDiscard,
        setSelecting(value) { this.selecting = value; } };
    const notice = (message, error = false) => messages.push({ message, error });
    const render = student => { renders.push(student); renderBusy.push(state.busy); if (h.throwOnRender) throw Error('Display failure'); };
    const h = { $, elements, state, guards, messages, renders, renderBusy, document, window,
        throwOnRender: false, setup: () => setup({ $, api, state, render, notice }),
        set session(value) { current = value; },
        set allowDiscard(value) { allowDiscard = value; },
        submit: id => $(id).listeners.submit({ preventDefault() {} }),
        flush: async () => { for (let i = 0; i < 25; i++) await Promise.resolve(); } };
    return h;
}

test('guardian creation holds one in-flight POST and uses stable ID on unchanged uncertain retry', async () => {
    const writes = [];
    const h = harness('guardian-save.js', 'setupGuardianSave', (url, method, payload) => {
        const pending = deferred(); writes.push({ url, method, payload, ...pending }); return pending.promise;
    });
    h.setup(); h.$('guardianName').value = 'Parent One';
    h.$('guardianCreateForm').dirty = true;
    h.submit('guardianCreateForm'); h.submit('guardianCreateForm'); await h.flush();
    assert.equal(writes.length, 1); assert.equal(h.$('guardianCreateForm').inert, true);
    writes[0].reject(Error('Uncertain')); await h.flush();
    assert.equal(h.$('guardianCreateForm').inert, false);
    h.submit('guardianCreateForm'); await h.flush();
    assert.equal(writes[1].payload.clientRequestId, writes[0].payload.clientRequestId);
    writes[1].reject(Error('Uncertain')); await h.flush();
    h.$('guardianName').value = 'Parent Two';
    h.submit('guardianCreateForm'); await h.flush();
    assert.notEqual(writes[2].payload.clientRequestId, writes[1].payload.clientRequestId);
    writes[2].resolve({ studentReference }); await h.flush();
    assert.equal(h.renders.length, 1);
    assert.deepEqual(h.renderBusy, [false]);
    assert.equal(h.$('guardianCreateForm').dirty, false);
    assert.match(h.messages.at(-1).message, /linked successfully/i);
});

test('acknowledged guardian creation is never reported as a failed POST on render error', async () => {
    const h = harness('guardian-save.js', 'setupGuardianSave', async () => ({ studentReference }));
    h.setup(); h.throwOnRender = true;
    await h.submit('guardianCreateForm');
    assert.equal(h.state.requiresRefresh, true);
    assert.match(h.messages.at(-1).message, /was saved/i);
    assert.equal(h.state.requestId, null);
    assert.equal(h.$('guardianCreateForm').inert, false);
});

test('guardian creation ignores a confirmed response from a switched institution session', async () => {
    const pending = deferred();
    const h = harness('guardian-save.js', 'setupGuardianSave', () => pending.promise);
    h.setup(); h.submit('guardianCreateForm'); await h.flush();
    h.session = false; pending.resolve({ studentReference }); await h.flush();
    assert.equal(h.renders.length, 0);
    assert.equal(h.messages.length, 0);
});

test('existing guardian link single-flight retry preserves the unsaved form after a failed POST', async () => {
    const writes = [];
    const h = harness('guardian-reuse.js', 'setupGuardianReuse', (url, method, payload) => {
        const pending = deferred(); writes.push({ url, method, payload, ...pending }); return pending.promise;
    });
    h.setup(); h.$('guardianExistingReference').value = guardianReference;
    h.$('guardianReuseForm').dirty = true;
    h.submit('guardianReuseForm'); h.submit('guardianReuseForm'); await h.flush();
    assert.equal(writes.length, 1);
    assert.equal(writes[0].payload.guardianReference, guardianReference);
    writes[0].reject(Error('Timeout')); await h.flush();
    assert.equal(h.$('guardianExistingReference').value, guardianReference);
    assert.equal(h.$('guardianReuseForm').dirty, true);
    assert.match(h.messages.at(-1).message, /Verify this student/i);
    h.submit('guardianReuseForm'); await h.flush();
    writes[1].resolve({ studentReference }); await h.flush();
    assert.equal(h.$('guardianReuseForm').dirty, false);
    assert.deepEqual(h.renderBusy, [false]);
    assert.match(h.messages.at(-1).message, /linked to the selected student/i);
});

test('existing guardian accepted link followed by a broken display remains a confirmed write', async () => {
    const h = harness('guardian-reuse.js', 'setupGuardianReuse', async () => ({ studentReference }));
    h.setup(); h.$('guardianExistingReference').value = guardianReference;
    h.throwOnRender = true;
    await h.submit('guardianReuseForm');
    assert.equal(h.state.requiresRefresh, true);
    assert.match(h.messages.at(-1).message, /was saved/i);
});

test('guardian contact correction retains request ID for uncertain retries and rotates after edits', async () => {
    const writes = [];
    const api = (url, method, payload) => {
        if (method === 'PUT') { const pending = deferred(); writes.push({ payload, ...pending }); return pending.promise; }
        return Promise.resolve({ fullName: 'Parent One', rowVersion: 'guardian-v1', phone: '111' });
    };
    const h = harness('guardian-contact-profile.js', 'setupGuardianContact', api);
    h.setup(); h.document.events['guardian:contact-selected']({ detail: { guardianReference } }); await h.flush();
    h.$('guardianContactReason').value = 'Correct phone';
    h.$('guardianContactPhone').value = '222';
    h.$('guardianContactForm').dirty = true;
    h.submit('guardianContactForm'); h.submit('guardianContactForm'); await h.flush();
    assert.equal(writes.length, 1); assert.equal(h.$('guardianContactForm').inert, true);
    writes[0].reject(Error('Unknown result')); await h.flush();
    h.submit('guardianContactForm'); await h.flush();
    assert.equal(writes[0].payload.clientRequestId, writes[1].payload.clientRequestId);
    writes[1].reject(Error('Unknown result')); await h.flush();
    h.$('guardianContactPhone').value = '333';
    h.submit('guardianContactForm'); await h.flush();
    assert.notEqual(writes[1].payload.clientRequestId, writes[2].payload.clientRequestId);
    writes[2].reject(Error('Unknown result')); await h.flush();
});

test('confirmed guardian contact write followed by failed relationship readback is not replayed', async () => {
    const pending = deferred();
    const api = (url, method) => {
        if (method === 'PUT') return pending.promise;
        if (url.endsWith('/contact')) return Promise.resolve({ fullName: 'Parent One', rowVersion: 'v1' });
        return Promise.reject(Error('Readback unavailable'));
    };
    const h = harness('guardian-contact-profile.js', 'setupGuardianContact', api);
    h.setup(); h.document.events['guardian:contact-selected']({ detail: { guardianReference } }); await h.flush();
    h.$('guardianContactReason').value = 'Correct information';
    h.$('guardianContactForm').dirty = true;
    h.submit('guardianContactForm'); await h.flush();
    pending.resolve({ rowVersion: 'v2' }); await h.flush();
    assert.equal(h.state.requiresRefresh, true);
    assert.equal(h.$('guardianContactReason').value, '');
    assert.equal(h.$('guardianContactForm').dirty, false);
    assert.match(h.messages.at(-1).message, /was saved, but/i);
    assert.equal(h.$('guardianContactSave').disabled, true);
});

test('guardian contact selection respects unsaved correction and stale tenant response', async () => {
    const pending = deferred();
    const h = harness('guardian-contact-profile.js', 'setupGuardianContact', () => pending.promise);
    h.setup(); h.document.events['guardian:contact-selected']({ detail: { guardianReference } });
    h.session = false; pending.resolve({ fullName: 'Parent', rowVersion: 'v1' }); await h.flush();
    assert.equal(h.$('guardianContactForm').hidden, true);
    assert.equal(h.$('guardianContactSave').disabled, true);
});

test('guardian search ignores superseded requests and blocks unsaved student switch', async () => {
    const searches = [], selections = [];
    const api = url => {
        const pending = deferred();
        (url.startsWith('/api/students?') ? searches : selections).push(pending);
        return pending.promise;
    };
    const h = harness('guardian-search.js', 'setupGuardianSearch', api);
    h.setup(); h.$('guardianSearch').value = 'first';
    h.submit('guardianSearchForm'); await h.flush();
    h.$('guardianSearch').listeners.input(); h.$('guardianSearch').value = 'second';
    h.submit('guardianSearchForm'); await h.flush();
    searches[1].resolve({ items: [{ reference: siblingReference, fullName: 'Second', studentCode: 'S2' }] });
    await h.flush();
    searches[0].resolve({ items: [{ reference: studentReference, fullName: 'First', studentCode: 'S1' }] });
    await h.flush();
    assert.equal(h.$('guardianMatches').children.length, 1);
    assert.match(h.$('guardianMatches').children[0].textContent, /Second/);
    h.allowDiscard = false;
    await h.$('guardianMatches').children[0].listeners.click();
    assert.equal(selections.length, 0);
    h.allowDiscard = true;
    const click = h.$('guardianMatches').children[0].listeners.click();
    await h.flush(); assert.equal(h.state.selecting, true);
    h.session = false;
    selections[0].resolve({ studentReference: siblingReference }); await click;
    assert.equal(h.renders.length, 0);
    assert.equal(h.state.selecting, false);
});

function parentHarness() {
    const elements = new Map(), captured = {}, pending = [], events = [];
    let dirty = false, mayDiscard = true;
    const get = id => {
        if (!elements.has(id)) elements.set(id, {
            disabled: false, inert: false, textContent: '', resetCount: 0, listeners: {},
            addEventListener(type, listener) { this.listeners[type] = listener; },
            querySelector() { return { value: 'csrf' }; }, querySelectorAll() { return []; },
            reset() { this.resetCount++; },
            classList: { contains: () => false }
        });
        return elements.get(id);
    };
    const document = { body: { dataset: { authTenantId: 't1', authSessionReference: 's1' } },
        getElementById: get, dispatchEvent: event => events.push(event) };
    const window = { EduOSSessionTransitionPending: () => false, confirm: () => mayDiscard };
    const fetch = async () => {
        const item = deferred(); pending.push(item); return item.promise;
    };
    const guardianGuard = { isDirty: () => dirty, confirmDiscard: () => mayDiscard,
        markClean: () => { dirty = false; } };
    const script = source('guardians.js').replace(/^import .+;\s*$/gm, '');
    vm.runInNewContext(script, {
        document, window, fetch, location: { search: '' }, URLSearchParams,
        CustomEvent: class { constructor(type, options) { this.type = type; this.detail = options.detail; } },
        setupGuardianSearch: args => { captured.search = args; },
        setupGuardianSave: args => { args.state.guards.push(guardianGuard); },
        setupGuardianReuse() {}, setupGuardianContact() {},
        setupGuardianHistory: () => ({ refresh() {} }),
        setupRevokedGuardianPaging: () => ({ refresh() {} }),
        renderGuardianLinks() {}, renderRevokedGuardianLinks() {}
    });
    return { get, document, window, captured, events, pending,
        set dirty(value) { dirty = value; }, set mayDiscard(value) { mayDiscard = value; },
        ok: student => ({ ok: true, async json() { return { success: true, data: student }; } }),
        flush: async () => { for (let i = 0; i < 25; i++) await Promise.resolve(); } };
}

test('guardian workspace guards dirty student switches and locks all action surfaces during selection', () => {
    const h = parentHarness();
    const student = { studentReference, studentName: 'One', studentCode: 'S1' };
    h.captured.search.render(student);
    h.dirty = true; h.mayDiscard = false;
    assert.equal(h.captured.search.state.confirmStudentSwitch(), false);
    h.mayDiscard = true;
    assert.equal(h.captured.search.state.confirmStudentSwitch(), true);
    h.captured.search.state.setSelecting(true);
    assert.equal(h.get('guardianLinks').inert, true);
    assert.equal(h.get('guardianRevokedLinks').inert, true);
    assert.equal(h.get('guardianCreateForm').inert, true);
    assert.equal(h.get('guardianCreate').disabled, true);
    h.captured.search.state.setSelecting(false);
    h.captured.search.render({ studentReference: siblingReference, studentName: 'Two', studentCode: 'S2' });
    assert.equal(h.get('guardianCreateForm').resetCount, 2);
    assert.equal(h.get('guardianReuseForm').resetCount, 2);
    assert.equal(h.events.at(-1).detail.studentReference, siblingReference);
});

test('guardian refresh does not render a stale response after a concurrent pending write or tenant switch', async () => {
    const h = parentHarness();
    h.captured.search.render({ studentReference, studentName: 'One', studentCode: 'S1' });
    const refresh = h.get('guardianRefresh').listeners.click(); await h.flush();
    h.captured.search.state.busy = true;
    h.pending[0].resolve(h.ok({ studentReference, studentName: 'Old response', studentCode: 'S1' }));
    await refresh;
    assert.match(h.get('guardianSelected').textContent, /One/);
    h.captured.search.state.busy = false;
    const second = h.get('guardianRefresh').listeners.click(); await h.flush();
    h.document.body.dataset.authTenantId = 'different';
    h.pending[1].resolve(h.ok({ studentReference, studentName: 'Wrong tenant', studentCode: 'S1' }));
    await second;
    assert.match(h.get('guardianSelected').textContent, /One/);
    h.captured.search.state.refreshActions();
    assert.equal(h.get('guardianCreate').disabled, true);
});

test('guardian editor view retains central security and draft protection contracts', () => {
    const view = read('EduOS.App/Views/StudentGuardians/Index.cshtml');
    const parent = source('guardians.js');
    const controller = read('EduOS.App/Controllers/Api/StudentGuardiansController.cs');
    const service = read('EduOS.Service/Services/Students/StudentGuardianWorkspaceService.cs');
    assert.match(view, /form-guard\.js"[\s\S]*students\/guardians\.js"/);
    assert.match(parent, /state\.confirmStudentSwitch/);
    assert.match(parent, /EduOSSessionTransitionPending/);
    assert.match(parent, /RequestVerificationToken: token/);
    assert.match(controller, /\[Authorize\(Roles = "TenantAdmin"\)\]/);
    assert.match(controller, /\[AutoValidateAntiforgeryToken\]/);
    assert.match(service, /x\.TenantId == tenant && x\.PublicId == request\.StudentReference/);
});
