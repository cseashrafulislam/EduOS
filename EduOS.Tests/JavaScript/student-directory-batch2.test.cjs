'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/Students/Index.cshtml'), 'utf8');
const script = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/students/index.js'), 'utf8');
const contact = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/students/contact-profile.js'), 'utf8');
const flush = () => new Promise(resolve => setImmediate(resolve));
const response = (data, success = true) => ({ ok: success, json: async () => ({ success, data }) });
function deferred() {
    let resolve;
    const promise = new Promise(r => { resolve = r; });
    return { promise, resolve };
}
function element() {
    const listeners = new Map();
    const classes = new Set();
    return {
        value: '', textContent: '', disabled: false, className: '', dataset: {}, children: [],
        classList: {
            toggle: (name, force) => force ? classes.add(name) : classes.delete(name),
            add: name => classes.add(name), remove: name => classes.delete(name)
        },
        addEventListener(name, handler) { listeners.set(name, handler); },
        trigger(name, event = {}) { return listeners.get(name)?.(event); },
        setAttribute() {}, removeAttribute() {}, focus() {},
        append(...items) { this.children.push(...items); },
        replaceChildren(...items) { this.children = items; },
        querySelector() { return { value: 'csrf-token' }; }
    };
}
function harness(source, values = {}) {
    const nodes = new Map(), listeners = new Map(), calls = [], events = [];
    const get = id => {
        if (!nodes.has(id)) nodes.set(id, element());
        return nodes.get(id);
    };
    get('studentStrings').textContent = JSON.stringify({ loading: 'Loading', empty: 'Empty',
        page: 'Page', countNoun: 'students', detailsFailed: 'Details failed' });
    Object.entries(values).forEach(([id, value]) => { get(id).value = value; });
    const body = { dataset: { authTenantId: '22', authSessionReference: 'S' } };
    const document = {
        body, getElementById: get, createElement: () => element(),
        querySelector: () => null,
        addEventListener(name, handler) { listeners.set(name, handler); },
        dispatchEvent(event) { events.push(event); }
    };
    const context = {
        document, window: {}, AbortController, URLSearchParams, encodeURIComponent,
        Intl, Date, Number, JSON, setTimeout, console,
        crypto: { randomUUID: () => 'ed19b24f-b4b0-4470-8a11-22f5dc7c0400' },
        fetch(url, options) {
            const pending = deferred();
            calls.push({ url: String(url), options, ...pending });
            return pending.promise;
        },
        bootstrap: { Modal: { getOrCreateInstance: () => ({ show() {} }) } },
        CustomEvent: class { constructor(type, init) { this.type = type; this.detail = init.detail; } }
    };
    vm.runInNewContext(source, context, { filename: 'student-directory.js' });
    return { get, calls, events, listeners, body };
}

test('Batch 2 Student directory remains server-paged, explicit-filtered and bilingual', () => {
    for (const id of ['studentPageSize', 'studentResetFilters', 'studentPageStatus', 'studentExportPage'])
        assert.match(view, new RegExp('id="' + id + '"'));
    assert.match(view, /id="studentPageStatus" role="status" aria-live="polite"/);
    assert.match(view, /@\(CultureInfo\.CurrentUICulture\.Name == "bn-BD"/);
    assert.match(script, /new URLSearchParams\(\{ page: String\(state\.page\), pageSize: String\(state\.pageSize\) \}\)/);
    assert.match(script, /request !== state\.listRequest/);
    assert.match(script, /request !== state\.detailRequest/);
    assert.match(script, /if \(request !== state\.listRequest \|\| !sameSession\(\)\) return;/);
    assert.doesNotMatch(script, /innerHTML\s*=|localStorage/);
});

test('older student search cannot overwrite newer search; paging and reset stay bounded', async () => {
    const h = harness(script);
    const ready = h.listeners.get('DOMContentLoaded')();
    assert.equal(h.calls.length, 1);
    h.calls[0].resolve(response({ items: [], totalCount: 0, totalPages: 0 }));
    await ready;
    h.get('studentSearch').value = 'A';
    h.get('studentFilters').trigger('submit', { preventDefault() {} });
    const older = h.calls[1];
    h.get('studentSearch').value = 'B';
    h.get('studentFilters').trigger('submit', { preventDefault() {} });
    const newer = h.calls[2];
    assert.equal(older.options.signal.aborted, true);
    newer.resolve(response({ items: [], totalCount: 3, totalPages: 2 }));
    await flush();
    assert.match(h.get('studentPageStatus').textContent, /3 students/);
    older.resolve(response({ items: [], totalCount: 99, totalPages: 9 }));
    await flush();
    assert.match(h.get('studentPageStatus').textContent, /3 students/);
    h.get('studentPageSize').value = '50';
    h.get('studentPageSize').trigger('change', { target: h.get('studentPageSize') });
    assert.match(h.calls[3].url, /page=1&pageSize=50&search=B/);
    h.calls[3].resolve(response({ items: [], totalCount: 3, totalPages: 1 }));
    await flush();
    h.get('studentResetFilters').trigger('click');
    assert.match(h.calls[4].url, /page=1&pageSize=50$/);
    assert.equal(h.get('studentSearch').value, '');
    assert.equal(h.get('studentStatus').value, '');
    h.calls[4].resolve(response({ items: [], totalCount: 0, totalPages: 0 }));
    await flush();
});

test('out-of-order student detail requests cannot display wrong student or reopen after close', async () => {
    const h = harness(script);
    const ready = h.listeners.get('DOMContentLoaded')();
    h.calls[0].resolve(response({ items: [], totalCount: 0, totalPages: 1 }));
    await ready;
    const select = reference => h.get('studentList').trigger('click', {
        target: { closest: () => ({ dataset: { reference } }) }
    });
    select('A');
    select('B');
    assert.equal(h.calls[1].options.signal.aborted, true);
    h.calls[2].resolve(response({ rowVersion: 'Bv1', studentCode: 'B-001',
        rollNo: '', guardians: [], enrollments: [] }));
    await flush();
    assert.equal(h.events.filter(event => event.type === 'student:opened').length, 1);
    assert.equal(h.events[0].detail.reference, 'B');
    h.calls[1].resolve(response({ rowVersion: 'Av1', studentCode: 'A-001' }));
    await flush();
    assert.equal(h.events.filter(event => event.type === 'student:opened').length, 1);
    select('C');
    h.get('studentModal').trigger('hidden.bs.modal');
    h.calls[3].resolve(response({ rowVersion: 'Cv1', studentCode: 'C-001' }));
    await flush();
    assert.equal(h.events.filter(event => event.type === 'student:opened').length, 1);
});

test('contact editor ignores old A→B→A reads and keeps Save disabled during stale writes', async () => {
    const h = harness(contact);
    const opened = reference => h.listeners.get('student:opened')({ detail: { reference } });
    opened('A');
    opened('B');
    opened('A');
    assert.equal(h.calls[0].options.signal.aborted, true);
    assert.equal(h.calls[1].options.signal.aborted, true);
    h.calls[2].resolve(response({ phone: 'current-A', rowVersion: 'A-v2', preferredLanguage: 'en-BD' }));
    await flush();
    assert.equal(h.get('studentContactPhone').value, 'current-A');
    assert.equal(h.get('studentContactSave').disabled, false);
    h.calls[0].resolve(response({ phone: 'old-A', rowVersion: 'A-v1' }));
    h.calls[1].resolve(response({ phone: 'B', rowVersion: 'B-v1' }));
    await flush();
    assert.equal(h.get('studentContactPhone').value, 'current-A');
    h.get('studentContactReason').value = 'Correct contact profile';
    h.get('studentContactForm').reportValidity = () => true;
    h.get('studentContactForm').trigger('submit', { preventDefault() {} });
    assert.equal(h.get('studentContactSave').disabled, true);
    opened('B');
    h.calls[4].resolve(response({ phone: 'current-B', rowVersion: 'B-v2' }));
    await flush();
    assert.equal(h.get('studentContactSave').disabled, true, 'prior A save still in flight');
    h.calls[3].resolve(response({ rowVersion: 'A-v3' }));
    await flush();
    assert.equal(h.get('studentContactPhone').value, 'current-B');
    assert.equal(h.get('studentContactSave').disabled, false);
    assert.equal(h.events.filter(event => event.type === 'student:contact-updated').length, 0);
    h.get('studentModal').trigger('hidden.bs.modal');
    assert.equal(h.get('studentContactSave').disabled, true);
    h.body.dataset.authSessionReference = 'rotated-session';
    opened('A');
    assert.equal(h.calls.length, 5, 'stale authenticated session must not fetch contact data');
    assert.equal(h.get('studentContactSave').disabled, true);
});

test('directory export is ignored if a new filter invalidates the displayed page', async () => {
    const h = harness(script);
    const ready = h.listeners.get('DOMContentLoaded')();
    h.calls[0].resolve(response({ items: [{ reference: 'A', studentCode: 'ST-01', fullName: 'A' }],
        totalCount: 1, totalPages: 1 }));
    await ready;
    assert.equal(h.get('studentExportPage').disabled, false);
    h.get('studentExportPage').trigger('click');
    assert.match(h.calls[1].url, /^\/api\/students\/export-page\?/);
    h.get('studentSearch').value = 'B';
    h.get('studentFilters').trigger('submit', { preventDefault() {} });
    assert.equal(h.get('studentExportPage').disabled, true);
    h.calls[2].resolve(response({ items: [], totalCount: 0, totalPages: 0 }));
    await flush();
    h.calls[1].resolve({ ok: true, headers: { get: () => 'text/csv' },
        blob: async () => ({ size: 12 }) });
    await flush();
    assert.equal(h.get('studentExportPage').disabled, true,
        'an old export completion cannot enable the button for an empty new page');
});
