'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const project = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(project, 'EduOS.App/wwwroot/js/academic', name), 'utf8');

const cases = [
    ['departments.js', 'departmentForm', 'departmentCancel', 'departmentSave', 'POST',
        { departmentName: 'Science', departmentCode: 'SCI', departmentDescription: '', departmentActive: true }],
    ['programmes.js', 'programmeEditForm', 'programmeEditCancel', 'programmeEditSave', 'PUT',
        { programmeEditId: '9', programmeEditVersion: 'v1', programmeEditName: 'BSc',
            programmeEditDuration: '48', programmeEditOrder: '1', programmeEditActive: true,
            programmeEditAdmissions: true }],
    ['levels.js', 'levelEditForm', 'levelEditCancel', '', 'PUT',
        { levelEditId: '8', levelEditVersion: 'v1', levelEditNumber: '5',
            levelEditOrder: '0', levelEditPromotable: true, levelEditTerminal: false }],
    ['batches.js', 'batchEditForm', 'batchEditCancel', '', 'PUT',
        { batchEditId: '7', batchEditVersion: 'v1', batchEditCapacity: '40', batchEditOrder: '0' }],
    ['subjects.js', 'subjectEditForm', 'subjectEditCancel', '', 'PUT',
        { subjectEditId: '6', subjectEditVersion: 'v1', subjectEditCredits: '3' }]
];

function setup(name, formId, inputs) {
    const nodes = new Map(), guards = new Map(), notices = [], calls = [];
    const mkNode = id => ({
        id, value: '', checked: false, min: '0', disabled: false, inert: false, textContent: '',
        handlers: {}, options: [], children: [], resets: 0, scrollIntoView() {},
        classList: { add() {}, remove() {} },
        addEventListener(type, callback) { this.handlers[type] = callback; },
        reportValidity: () => true,
        querySelector: () => ({ disabled: false }),
        append(...children) { this.children.push(...children); },
        replaceChildren(...children) { this.options = children; this.children = [...children]; },
        add(option) { this.options.push(option); },
        reset() { this.resets++; }
    });
    const el = id => {
        if (!nodes.has(id)) nodes.set(id, mkNode(id));
        return nodes.get(id);
    };
    for (const [id, value] of Object.entries(inputs)) {
        const n = el(id);
        if (typeof value === 'boolean') n.checked = value;
        else n.value = value;
    }
    const form = el(formId), button = { disabled: false };
    form.querySelector = () => button;
    const guard = id => {
        if (!guards.has(id)) guards.set(id, {
            dirty: false, discard: false, cleans: 0,
            isDirty() { return this.dirty; },
            confirmDiscard() { return this.discard; },
            markClean() { this.dirty = false; this.cleans++; }
        });
        return guards.get(id);
    };
    let session = true, failReadback = false, apiImpl = null;
    const api = (url, request) => {
        calls.push({ url, request });
        if (apiImpl) return apiImpl(url, request);
        if (request?.method && request.method !== 'GET') return Promise.resolve({});
        return failReadback ? Promise.reject(Error('readback failed')) : Promise.resolve({ items: [], totalCount: 0 });
    };
    const management = {
        state: { campuses: [], years: [], catalog: { programs: [] } },
        el, api, notice: (message, danger) => notices.push({ message, danger }),
        reload: () => failReadback ? Promise.reject(Error('reload failed')) : Promise.resolve(),
        sameSession: () => session, guard,
        mayReplace: id => !guard(id).dirty || guard(id).discard
    };
    const subscriptions = new Map();
    const window = { eduosAcademicManagement: management,
        addEventListener(name, callback) { subscriptions.set(name, callback); } };
    const document = {
        createElement: name => mkNode(name),
        getElementById: id => id === 'academicManagement' ? { dataset: { activeFeature: 'none' } } : el(id),
        querySelector: () => mkNode('panel')
    };
    class Option {
        constructor(text, value) { this.text = text; this.value = value; }
    }
    vm.runInNewContext(read(name), { window, document, Option, URLSearchParams, URL, Blob,
        setTimeout, console }, { filename: name });
    const submit = () => form.handlers.submit({ currentTarget: form, preventDefault() {} });
    return { el, form, button, guards, notices, calls, submit, subscriptions,
        setSession: value => { session = value; },
        setReadbackFailure: value => { failReadback = value; },
        setApi: fn => { apiImpl = fn; } };
}

for (const [name, id, cancel, buttonId, method, values] of cases) {
    test(name + ' prevents parallel saves and distinguishes a confirmed write from failed refresh', async () => {
        const ctx = setup(name, id, values);
        await new Promise(resolve => setImmediate(resolve));
        ctx.calls.length = 0;
        let release;
        ctx.setApi((url, options) => options?.method === method
            ? new Promise(resolve => { release = resolve; })
            : Promise.reject(Error('readback failed')));
        ctx.setReadbackFailure(true);
        const first = ctx.submit();
        await ctx.submit();
        assert.equal(ctx.calls.filter(x => x.request?.method === method).length, 1);
        assert.equal(ctx.form.inert, true);
        release({});
        await first;
        assert.equal(ctx.form.inert, false);
        assert.ok(ctx.guards.get(id).cleans > 0, 'confirmed save clears the shared draft guard');
        assert.match(ctx.notices.at(-1).message, /saved|updated|directory did not refresh/i);
        assert.equal(ctx.notices.at(-1).danger, true, 'readback failure is explicitly reported');
    });
    test(name + ' respects unsaved cancel and does not submit in an expired session', async () => {
        const ctx = setup(name, id, values);
        await new Promise(resolve => setImmediate(resolve));
        const guard = ctx.guards.get(id);
        guard.dirty = true;
        ctx.el(cancel).handlers.click();
        assert.equal(guard.dirty, true, 'discard refused');
        guard.discard = true;
        ctx.el(cancel).handlers.click();
        assert.equal(guard.dirty, false, 'explicit discard clears the draft guard');
        ctx.calls.length = 0;
        ctx.setSession(false);
        await ctx.submit();
        assert.equal(ctx.calls.filter(x => x.request?.method === method).length, 0);
    });
    test(name + ' preserves the expected guard, token and bounded server page contracts', () => {
        const source = read(name);
        assert.match(source, /sameSession/);
        assert.match(source, /mayReplace/);
        assert.ok(source.includes("guard('" + id + "')"));
        assert.match(source, /pageSize=20|pageSize: String\(pageSize\)|pageSize: String\(size\)/);
        if (buttonId) assert.ok(source.includes(buttonId));
    });
}

for (const [name, formId, searchForm, listId, endpoint, idField] of [
    ['levels.js', 'levelEditForm', 'levelSearchForm', 'levelRows', 'levels', 'levelEditId'],
    ['batches.js', 'batchEditForm', 'batchSearchForm', 'batchRegisterRows', 'batches', 'batchEditId'],
    ['subjects.js', 'subjectEditForm', 'subjectSearchForm', 'subjectRows', 'subjects', 'subjectEditId']
]) {
    test(name + ' never overwrites a later selected editor with an older API response', async () => {
        const ctx = setup(name, formId, {});
        const resolvers = new Map();
        ctx.setApi(url => {
            if (url.includes('/' + endpoint + '?'))
                return Promise.resolve({ items: [{ id: 1, name: 'First', code: 'ONE', rowVersion: 'v1' },
                    { id: 2, name: 'Second', code: 'TWO', rowVersion: 'v2' }], totalCount: 2 });
            const id = Number(/\/(\d+)$/.exec(url)?.[1]);
            return new Promise(resolve => resolvers.set(id, resolve));
        });
        ctx.el(searchForm).handlers.submit({ preventDefault() {} });
        await new Promise(resolve => setImmediate(resolve));
        const rows = ctx.el(listId).children;
        assert.equal(rows.length, 2);
        const buttons = rows.map(row => row.children.at(-1).children[0]);
        const first = buttons[0].handlers.click();
        const second = buttons[1].handlers.click();
        assert.equal(ctx.form.inert, true);
        resolvers.get(2)({ id: 2, name: 'Second', code: 'TWO', rowVersion: 'v2',
            capacity: 40, levelNo: 2, displayOrder: 0 });
        await second;
        resolvers.get(1)({ id: 1, name: 'First', code: 'ONE', rowVersion: 'v1',
            capacity: 20, levelNo: 1, displayOrder: 0 });
        await first;
        assert.equal(Number(ctx.el(idField).value), 2);
        assert.equal(ctx.form.inert, false);
    });
}
