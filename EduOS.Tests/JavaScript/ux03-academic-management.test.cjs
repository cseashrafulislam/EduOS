'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { webcrypto } = require('node:crypto');
const repo = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(repo, file), 'utf8');
const formsScript = read('EduOS.App/wwwroot/js/academic/management-forms.js');
const mainScript = read('EduOS.App/wwwroot/js/academic/management.js');
const offeringScript = read('EduOS.App/wwwroot/js/academic/subject-offerings.js');

function setup() {
    const formIds = ['yearForm', 'termForm', 'programForm', 'levelForm', 'sectionForm',
        'subjectForm', 'curriculumForm', 'curriculumSubjectForm'];
    const values = new Map(Object.entries({
        yearStart: '2026-01-01', yearEnd: '2026-12-31', yearName: '2026', yearCode: '26',
        yearId: '', yearVersion: '', yearCampus: '', yearCycle: '1',
        sectionCampus: '1', sectionYear: '10', sectionTerm: '', sectionProgram: '20',
        sectionLevel: '30', sectionStart: '2026-01-01', sectionEnd: '2026-12-31',
        sectionName: 'A', sectionCode: 'A', sectionCapacity: '40'
    }));
    const nodes = new Map();
    const guards = new Map();
    const notices = [], requests = [];
    let currentSession = true;
    let apiImpl = async () => ({});
    let reloadImpl = async () => {};
    for (const id of formIds) {
        const button = { disabled: false };
        const form = {
            id, dataset: {}, inert: false, elements: [], button, resets: 0, handlers: {},
            reportValidity: () => true,
            querySelector: () => button,
            reset() { this.resets++; },
            addEventListener(name, handler) { this.handlers[name] = handler; }
        };
        nodes.set(id, form);
        guards.set(id, {
            dirty: false, cleans: 0, discard: false,
            isDirty() { return this.dirty; },
            markClean() { this.cleans++; this.dirty = false; },
            confirmDiscard() { return this.discard; }
        });
    }
    for (const id of ['yearId', 'yearVersion', 'termId', 'termVersion'])
        nodes.set(id, { value: values.get(id) || '' });
    const resetButtons = new Map(['year', 'term'].map(kind => [kind, { handlers: {},
        addEventListener(type, callback) { this.handlers[type] = callback; }
    }]));
    const mock = {
        state: { terms: [] },
        el: id => nodes.get(id) || { addEventListener() {} },
        field: id => values.get(id) || '',
        idOf: id => Number(values.get(id)) || null,
        findYear: id => id === 10 ? { id: 10, startDate: '2026-01-01', endDate: '2026-12-31' } : null,
        find: (key, id) => key === 'programs' && id === 20
            ? { id: 20, campusIds: [1] }
            : key === 'levels' && id === 30 ? { id: 30, academicProgramId: 20 } : null,
        notice: (message, danger) => notices.push({ message, danger }),
        api: (...args) => { requests.push(args); return apiImpl(...args); },
        reload: () => reloadImpl(),
        checked: () => false,
        sameSession: () => currentSession,
        guard: id => guards.get(id),
        mayReplace: id => !guards.get(id).isDirty() || guards.get(id).confirmDiscard()
    };
    const window = { eduosAcademicManagement: mock,
        EduOSFormGuard: { fingerprint: form => JSON.stringify(form.elements.map(field => [field.value, field.checked])) } };
    const document = { querySelector: selector => resetButtons.get(selector.includes('year') ? 'year' : 'term') };
    vm.runInNewContext(formsScript, { window, document, crypto: webcrypto, Uint8Array, console },
        { filename: 'management-forms.js' });
    const dispatch = (id, type = 'submit') => {
        const target = nodes.get(id);
        return target.handlers[type]({ currentTarget: target, preventDefault() {} });
    };
    return { nodes, guards, notices, requests, values, resetButtons, dispatch,
        setApi: fn => { apiImpl = fn; }, setReload: fn => { reloadImpl = fn; },
        setSession: value => { currentSession = value; } };
}

test('all eight Academic Management editors are registered for draft protection', () => {
    const ctx = setup();
    assert.equal(ctx.guards.size, 8);
    for (const id of ctx.guards.keys())
        assert.equal(typeof ctx.nodes.get(id).handlers.submit, 'function', id);
    const view = read('EduOS.App/Views/AcademicManagement/Index.cshtml');
    assert.match(view, /platform\/form-guard\.js"[\s\S]*academic\/management\.js"/);
    assert.match(mainScript, /sameSession.*guard.*mayReplace/s);
    assert.match(mainScript, /select\.multiple/);
    assert.match(mainScript, /allocationLookup/);
});

test('single-flight blocks duplicate academic saves; write remains successful if readback fails', async () => {
    const ctx = setup();
    let release;
    ctx.setApi(() => new Promise(resolve => { release = resolve; }));
    ctx.setReload(() => { throw Error('readback unavailable'); });
    const first = ctx.dispatch('yearForm');
    await ctx.dispatch('yearForm');
    assert.equal(ctx.requests.length, 1);
    assert.equal(ctx.nodes.get('yearForm').inert, true);
    assert.equal(ctx.nodes.get('yearForm').button.disabled, true);
    release({});
    await first;
    assert.equal(ctx.nodes.get('yearForm').resets > 0, true);
    assert.equal(ctx.guards.get('yearForm').cleans > 0, true);
    assert.equal(ctx.nodes.get('yearForm').inert, false);
    assert.equal(ctx.nodes.get('yearForm').button.disabled, false);
    assert.match(ctx.notices.at(-1).message, /Saved successfully.*could not refresh/);
});

test('response from an old session cannot clear the academic editor', async () => {
    const ctx = setup();
    let release;
    ctx.setApi(() => new Promise(resolve => { release = resolve; }));
    const call = ctx.dispatch('yearForm');
    ctx.setSession(false);
    release({});
    await call;
    assert.equal(ctx.nodes.get('yearForm').resets, 0);
    assert.equal(ctx.notices.length, 0);
    assert.equal(ctx.nodes.get('yearForm').inert, false);
});

test('uncertain section POST retains idempotency token until any input changes', async () => {
    const ctx = setup();
    const form = ctx.nodes.get('sectionForm');
    const name = { value: 'A', checked: false };
    form.elements.push(name);
    ctx.setApi(() => { throw Error('network timeout'); });
    await ctx.dispatch('sectionForm');
    await ctx.dispatch('sectionForm');
    assert.equal(ctx.requests.length, 2);
    const key1 = JSON.parse(ctx.requests[0][1].body).clientRequestId;
    const key2 = JSON.parse(ctx.requests[1][1].body).clientRequestId;
    assert.equal(key1, key2);
    name.value = 'B';
    ctx.values.set('sectionName', 'B');
    await ctx.dispatch('sectionForm');
    const key3 = JSON.parse(ctx.requests[2][1].body).clientRequestId;
    assert.notEqual(key3, key1);
    assert.equal(form.resets, 0, 'failed write does not erase the draft');
});

test('reset is canceled when user refuses to discard a dirty academic year', () => {
    const ctx = setup();
    const guard = ctx.guards.get('yearForm');
    guard.dirty = true;
    ctx.nodes.get('yearId').value = '42';
    ctx.resetButtons.get('year').handlers.click();
    assert.equal(ctx.nodes.get('yearForm').resets, 0);
    assert.equal(ctx.nodes.get('yearId').value, '42');
    guard.discard = true;
    ctx.resetButtons.get('year').handlers.click();
    assert.equal(ctx.nodes.get('yearId').value, '');
    assert.equal(ctx.nodes.get('yearForm').resets, 1);
    assert.equal(guard.dirty, false);
});

function offeringSetup() {
    const values = {
        offeringBatch: '10', offeringReference: '', offeringVersion: '',
        offeringCurriculumSubject: '30', offeringCode: 'BIO', offeringCapacity: '12',
        offeringDelivery: '1', offeringActive: '', offeringSearch: ''
    };
    const nodes = new Map();
    for (const [id, value] of Object.entries(values))
        nodes.set(id, { value, checked: id === 'offeringActive', disabled: false, handlers: {},
            addEventListener(type, callback) { this.handlers[type] = callback; } });
    for (const id of ['offeringCancel', 'offeringSearchButton', 'offeringPrevious', 'offeringNext',
        'offeringCsv', 'offeringSave', 'offeringMessage']) {
        if (!nodes.has(id)) nodes.set(id, { handlers: {}, disabled: false, textContent: '',
            addEventListener(type, callback) { this.handlers[type] = callback; } });
    }
    const form = { id: 'offeringForm', handlers: {}, inert: false, resets: 0,
        reportValidity: () => true,
        addEventListener(type, callback) { this.handlers[type] = callback; },
        reset() { this.resets++; } };
    nodes.set('offeringForm', form);
    const calls = [], notices = [], subscriptions = {};
    const guard = { dirty: false, mayDiscard: false, cleans: 0,
        isDirty() { return this.dirty; }, confirmDiscard() { return this.mayDiscard; },
        markClean() { this.dirty = false; this.cleans++; } };
    let apiImpl = async () => ({});
    const vmMock = {
        state: { catalog: { batches: [{ id: 10, capacity: 40, academicYearId: 2, academicTermId: null }] } },
        el: id => nodes.get(id),
        api: (...args) => { calls.push(args); return apiImpl(...args); },
        notice: (message, danger) => notices.push({ message, danger }),
        sameSession: () => true, guard: () => guard,
        mayReplace: () => !guard.dirty || guard.mayDiscard
    };
    const window = { eduosAcademicManagement: vmMock,
        addEventListener: (name, callback) => { subscriptions[name] = callback; } };
    vm.runInNewContext(offeringScript, { window, document: {}, crypto: webcrypto,
        Option: class {}, URL, Blob, setTimeout }, { filename: 'subject-offerings.js' });
    const submit = () => form.handlers.submit({ currentTarget: form, preventDefault() {} });
    return { nodes, calls, guard, notices, form, submit,
        setApi: fn => { apiImpl = fn; } };
}

test('offering retry reuses idempotency key only for unchanged payload', async () => {
    const ctx = offeringSetup();
    ctx.setApi(() => { throw Error('connection lost'); });
    await ctx.submit();
    await ctx.submit();
    const first = JSON.parse(ctx.calls[0][1].body).clientRequestId;
    const second = JSON.parse(ctx.calls[1][1].body).clientRequestId;
    assert.equal(first, second);
    ctx.nodes.get('offeringCapacity').value = '15';
    await ctx.submit();
    const third = JSON.parse(ctx.calls[2][1].body).clientRequestId;
    assert.notEqual(third, first);
    assert.equal(ctx.form.resets, 0);
});

test('offering cannot silently lose dirty draft on batch change', () => {
    const ctx = offeringSetup();
    ctx.guard.dirty = true;
    ctx.nodes.get('offeringBatch').value = '11';
    ctx.nodes.get('offeringBatch').handlers.change();
    assert.equal(ctx.nodes.get('offeringBatch').value, '10', 'batch is restored to its initial selected value');
    assert.equal(ctx.form.resets, 0);
});

test('offering single-flight save has distinct committed-but-refresh-failed outcome', async () => {
    const ctx = offeringSetup();
    let release;
    ctx.setApi((path) => path.includes('offerings?')
        ? Promise.reject(Error('directory unavailable'))
        : new Promise(resolve => { release = resolve; }));
    const first = ctx.submit();
    await ctx.submit();
    assert.equal(ctx.calls.length, 1);
    assert.equal(ctx.form.inert, true);
    release({});
    await first;
    assert.equal(ctx.form.resets, 1);
    assert.equal(ctx.form.inert, false);
    assert.ok(ctx.guard.cleans > 0);
    assert.match(ctx.notices.at(-1).message, /Offering saved, but the list did not refresh/);
});

test('track and room create/edit routes retain drafts, block duplicate writes and revalidate session', () => {
    for (const [file, prefix] of [['tracks.js', 'track'], ['rooms.js', 'room']]) {
        const script = read('EduOS.App/wwwroot/js/academic/' + file);
        for (const id of [prefix + 'CreateForm', prefix + 'EditForm'])
            assert.ok(script.includes("guard('" + id + "')"), id + ' is guarded');
        assert.match(script, /createSaving = true.*form\.inert = true/s);
        assert.match(script, /editSaving = true.*form\.inert = true/s);
        assert.match(script, /if \(createSaving \|\| !sameSession\(\) \|\| !form\.reportValidity\(\)\) return/);
        assert.match(script, /if \(editSaving \|\| !sameSession\(\) \|\| !form\.reportValidity\(\)\) return/);
        assert.match(script, /selection !== editSequence \|\| editGuard\?\.isDirty\(\)/);
        assert.match(script, /created, but the directory did not refresh|saved, but the directory did not refresh|saved, but the directory could not refresh/i);
        assert.match(script, /editGuard\?\.markClean\(\)/);
    }
});
