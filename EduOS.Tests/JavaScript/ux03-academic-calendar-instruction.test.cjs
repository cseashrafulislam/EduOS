'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

function deferred() {
    let resolve, reject;
    const promise = new Promise((res, rej) => { resolve = res; reject = rej; });
    return { promise, resolve, reject };
}
async function until(predicate, message) {
    for (let i = 0; i < 20; i++) {
        if (predicate()) return;
        await new Promise(resolve => setImmediate(resolve));
    }
    throw new Error(message || 'Expected async action did not complete');
}

function fixture(kind) {
    const nodes = new Map(), handlers = new Map(), requests = [], guards = [];
    const makeNode = (id = '') => ({ id, value: '', checked: false, disabled: false, textContent: '',
        dataset: {}, children: [], elements: [],
        get childNodes() { return this.children; },
        classList: { add() {}, remove() {} },
        addEventListener(name, handler) { handlers.set(`${id}:${name}`, handler); },
        replaceChildren(...children) { this.children = children; },
        append(...children) { this.children.push(...children); },
        setAttribute() {}, removeAttribute() {}, focus() {}, reportValidity() { return true; },
        reset() { this.resets = (this.resets || 0) + 1; }
    });
    const el = id => {
        if (!nodes.has(id)) nodes.set(id, makeNode(id));
        return nodes.get(id);
    };
    const isCalendar = kind === 'calendar';
    el(isCalendar ? 'academicCalendarApp' : 'instructionApp').dataset = isCalendar
        ? { canManage: 'true', tenantAdmin: 'true' } : { canManage: 'true', canTeach: 'true' };
    const body = { dataset: { authTenantId: '7', authSessionReference: 's1', authUserId: 'actor-1' } };
    const weekend = [0, 1, 2, 3, 4, 5, 6].map(n => ({ value: String(n), checked: n === 5 || n === 6 }));
    const document = { body, getElementById: el,
        querySelector: () => ({ value: 'csrf', content: 'csrf' }),
        querySelectorAll: selector => selector.includes('weekendDay')
            ? (selector.includes(':checked') ? weekend.filter(x => x.checked) : weekend) : [],
        createElement: tag => makeNode(tag), documentElement: { lang: 'en-BD' } };
    let uuid = 0, responder = async () => ({ success: true, data: null });
    const window = { document, confirm: () => true, addEventListener() {},
        EduOSFormGuard: { create() { const guard = { dirty: false, allow: false, cleans: 0,
            isDirty() { return this.dirty; },
            confirmDiscard() { return this.allow; },
            markClean() { this.dirty = false; this.cleans++; } };
            guards.push(guard); return guard; } } };
    const fetch = async (url, options = {}) => {
        requests.push({ url, options });
        const result = await responder(url, options);
        if (result?.ok !== undefined) return result;
        return { ok: true, status: 200, async json() { return result; } };
    };
    const context = { window, document, fetch, crypto: { randomUUID: () => `request-${++uuid}` },
        URLSearchParams, Date, console, setTimeout: fn => fn() };
    const file = isCalendar ? 'EduOS.App/wwwroot/js/academic/calendar.js'
        : 'EduOS.App/wwwroot/js/academic/instruction.js';
    const exported = isCalendar ? 'load, openEditor, closeEditor, changeScope, saveEvent, savePolicy, state'
        : 'load, openEditor, resetEditor, selectBatch, saveLesson, saveSub, state';
    const source = read(file).replace(/    init(?:Scopes)?\(\);\s*\}\)\(\);\s*$/,
        `globalThis.__ux03 = { ${exported} };})();`);
    vm.runInNewContext(source, context, { filename: file });
    const request = (method = 'POST') => requests.filter(x => x.options.method === method);
    return { el, body, weekend, handlers, guards, requests, api: context.__ux03, request,
        setResponder(fn) { responder = fn; } };
}

const calendarEvents = [{ id: 20, academicYearId: 1, campusId: 10, title: 'Existing', eventType: 1,
    startDate: '2026-02-01', endDate: '2026-02-01', rowVersion: 'AQID' }];
function calendarReady(ctx) {
    ctx.api.state.years = [{ id: 1, name: '2026', startDate: '2026-01-01', endDate: '2026-12-31' }];
    ctx.api.state.campuses = [{ id: 10, name: 'Main' }, { id: 11, name: 'Other' }];
    ctx.api.state.events = calendarEvents;
    ctx.api.state.appliedYear = '1'; ctx.api.state.appliedCampus = '10';
    Object.assign(ctx.el('calendarYear'), { value: '1' });
    Object.assign(ctx.el('calendarCampus'), { value: '10' });
    ctx.el('calendarFrom').value = '2026-01-01'; ctx.el('calendarTo').value = '2026-12-31';
    ctx.el('calendarEventType').value = '1';
}

test('Academic Calendar source registers shared guards before transactional scripts', () => {
    for (const [view, script] of [
        ['AcademicCalendar', 'academic/calendar.js'], ['AcademicInstruction', 'academic/instruction.js']]) {
        const html = read(`EduOS.App/Views/${view}/Index.cshtml`);
        assert.match(html, new RegExp('form-guard\\.js"[\\s\\S]*' + script.replaceAll('/', '\\/').replaceAll('.', '\\.')));
    }
});

test('Calendar rejects dirty campus switch and duplicate in-flight event submission', async () => {
    const ctx = fixture('calendar'); calendarReady(ctx);
    ctx.api.openEditor(null);
    ctx.el('calendarEventTitle').value = 'Exam week';
    ctx.el('calendarEventStart').value = '2026-02-10';
    ctx.el('calendarEventEnd').value = '2026-02-10';
    ctx.guards[0].dirty = true;
    ctx.el('calendarCampus').value = '11';
    ctx.api.changeScope('campus');
    assert.equal(ctx.el('calendarCampus').value, '10');
    assert.equal(ctx.api.state.editId, null);
    const pending = deferred();
    ctx.setResponder((url, request) => request.method === 'POST' ? pending.promise : {
        success: true, data: url.includes('/events?') ? calendarEvents :
            url.includes('/working-days?') ? [] : { weekendDay1: 5, weekendDay2: 6, campusId: 10 }
    });
    const e = { preventDefault() {}, currentTarget: ctx.el('calendarEventForm') };
    const first = ctx.api.saveEvent(e);
    await ctx.api.saveEvent(e);
    assert.equal(ctx.request().length, 1);
    assert.equal(ctx.el('calendarEventForm').inert, true);
    pending.resolve({ success: true, data: { id: 21 } });
    await first;
    assert.equal(ctx.el('calendarEventForm').inert, false);
    assert.equal(ctx.el('calendarAlert').className, 'alert alert-success');
    assert.equal(ctx.el('calendarEditorPanel').classList.add !== undefined, true);
});

test('Calendar policy retains draft on failed POST and distinguishes committed write from readback failure', async () => {
    const ctx = fixture('calendar'); calendarReady(ctx);
    ctx.setResponder((url, request) => request.method === 'POST'
        ? { ok: false, status: 503, json: async () => ({ success: false, message: 'Offline' }) }
        : { success: true, data: null });
    const e = { preventDefault() {}, currentTarget: ctx.el('calendarPolicyForm') };
    await ctx.api.savePolicy(e);
    assert.equal(ctx.el('calendarPolicyForm').resets || 0, 0);
    assert.match(ctx.el('calendarAlert').textContent, /Check the current policy before retrying/);
    ctx.setResponder((url, request) => request.method === 'POST'
        ? { success: true, data: { campusId: 10, weekendDay1: 5, weekendDay2: 6, rowVersion: 'new' } }
        : { ok: false, status: 503, json: async () => ({ success: false, message: 'Readback offline' }) });
    await ctx.api.savePolicy(e);
    assert.equal(ctx.request().length, 2);
    assert.equal(ctx.el('calendarAlert').className, 'alert alert-warning');
    assert.match(ctx.el('calendarAlert').textContent, /saved, but.*refreshed/);
});

test('Calendar discards stale request and rejects old-tenant response', async () => {
    const ctx = fixture('calendar'); calendarReady(ctx);
    const queries = [];
    ctx.setResponder((url, request) => {
        if (request.method === 'GET') return new Promise(resolve => queries.push({ url, resolve }));
        return { success: true, data: null };
    });
    const first = ctx.api.load();
    const second = ctx.api.load();
    await until(() => queries.length === 4, 'Expected both calendar loads');
    const entries = queries.splice(0);
    entries.slice(2).forEach(x => x.resolve({ success: true, data: x.url.includes('/events?')
        ? [{ ...calendarEvents[0], title: 'Fresh' }] : [] }));
    await until(() => queries.length > 0, 'Expected current policy readback');
    const policy = queries.splice(0);
    policy.forEach(x => x.resolve({ success: true, data: null }));
    await second;
    entries.slice(0, 2).forEach(x => x.resolve({ success: true, data: x.url.includes('/events?')
        ? [{ ...calendarEvents[0], title: 'Stale' }] : [] }));
    await first;
    assert.equal(ctx.api.state.events[0].title, 'Fresh');
    const oldTenant = ctx.api.load();
    await until(() => queries.length === 2, 'Expected tenant-scoped readback');
    ctx.body.dataset.authTenantId = '8';
    queries.splice(0).forEach(x => x.resolve({ success: true, data: [{ ...calendarEvents[0], title: 'Foreign' }] }));
    await oldTenant;
    assert.equal(ctx.api.state.events[0].title, 'Fresh');
});

test('Calendar scope change releases a superseded load button and clears outdated policy', async () => {
    const ctx = fixture('calendar'); calendarReady(ctx);
    ctx.api.state.policy = { campusId: 10, academicYearId: 1, weekendDay1: 5, weekendDay2: 6 };
    ctx.setResponder(() => new Promise(() => {}));
    ctx.api.load();
    assert.equal(ctx.el('calendarLoad').disabled, true);
    ctx.el('calendarYear').value = '2';
    ctx.api.changeScope('year');
    assert.equal(ctx.el('calendarLoad').disabled, false);
    assert.equal(ctx.api.state.policy, null);
});

function instructionReady(ctx) {
    ctx.el('instructionBatch').value = '5';ctx.api.state.selectedBatch = '5';
    ctx.el('instructionFrom').value = '2026-09-01';ctx.el('instructionTo').value = '2026-09-30';
    ctx.el('lessonDate').value = '2026-09-10';
    ctx.el('lessonAssignment').value = 'offering-1';
    ctx.api.state.offerings = [{ reference: 'offering-1', isActive: true, subjectName: 'Math', code: 'M1' }];
    ctx.el('substitutionDate').value = '2026-09-10';
    ctx.el('substitutionRoutine').value = '15';
    ctx.el('substitutionTeacher').value = 'teacher-2';
    ctx.api.state.routines = [{ id: 15, dayOfWeek: 4 }];
}

function instructionReadback(url) {
    if (url.includes('/lesson-plans?')) return { success: true, data: { items: [], totalPages: 1 } };
    if (url.includes('/substitutions/page?')) return { success: true, data: { items: [], totalPages: 1 } };
    if (url.includes('/substitutions/summary?')) return { success: true, data: { activeCount: 0, cancelledCount: 0 } };
    if (url.includes('/lesson-offerings?') || url.includes('/substitution-routines?'))
        return { success: true, data: { items: [], totalPages: 1 } };
    if (url.includes('/instructors/page?')) return { success: true, data: { items: [], totalPages: 1 } };
    return { success: true, data: [] };
}

test('Instruction protects dirty lesson when changing academic batch', () => {
    const ctx = fixture('instruction'); instructionReady(ctx);
    ctx.api.openEditor(null);
    ctx.guards[0].dirty = true;
    ctx.el('instructionBatch').value = '6';
    ctx.api.selectBatch();
    assert.equal(ctx.el('instructionBatch').value, '5');
    assert.equal(ctx.el('lessonEditor').classList.add !== undefined, true);
    assert.equal(ctx.guards[0].dirty, true);
});

test('Instruction retains dirty offering and routine choices during a new register fetch', async () => {
    const ctx = fixture('instruction'); instructionReady(ctx);
    ctx.guards[0].dirty = true;ctx.guards[1].dirty = true;
    ctx.setResponder(url => instructionReadback(url));
    assert.equal(await ctx.api.load(), true);
    assert.equal(ctx.el('lessonAssignment').value, 'offering-1');
    assert.equal(ctx.el('substitutionRoutine').value, '15');
    assert.equal(ctx.el('lessonAssignment').children.some(x => x.value === 'offering-1'), true);
    assert.equal(ctx.el('substitutionRoutine').children.some(x => x.value === '15'), true);
});

test('Instruction lesson create is single-flight, stable on retry, and changes key only for changed command', async () => {
    const ctx = fixture('instruction'); instructionReady(ctx);
    ctx.api.openEditor(null);
    ctx.el('lessonAssignment').value = 'offering-1';ctx.el('lessonTitle').value = 'Integers';
    ctx.el('lessonDate').value = '2026-09-10';
    const posts = [];
    ctx.setResponder((url, request) => request.method === 'POST'
        ? new Promise(resolve => posts.push({ resolve, body: JSON.parse(request.body) }))
        : instructionReadback(url));
    const e = { preventDefault() {}, currentTarget: ctx.el('lessonForm') };
    const first = ctx.api.saveLesson(e);
    await ctx.api.saveLesson(e);
    assert.equal(posts.length, 1);
    posts[0].resolve({ ok: false, status: 503, json: async () => ({ success: false, message: 'Offline' }) });
    await first;
    assert.equal(ctx.api.state.draftRequestId, posts[0].body.clientRequestId);
    const retry = ctx.api.saveLesson(e);
    assert.equal(posts[1].body.clientRequestId, posts[0].body.clientRequestId);
    posts[1].resolve({ ok: false, status: 503, json: async () => ({ success: false, message: 'Offline' }) });
    await retry;
    ctx.el('lessonTitle').value = 'Integers corrected';
    const changed = ctx.api.saveLesson(e);
    assert.notEqual(posts[2].body.clientRequestId, posts[1].body.clientRequestId);
    posts[2].resolve({ success: true, data: { id: 51 } });
    await changed;
    assert.equal(ctx.api.state.editingId, null);
    assert.equal(ctx.api.state.draftRequestId, null);
    assert.equal(ctx.el('instructionAlert').className, 'alert alert-success');
});

test('Instruction substitute scheduling is single-flight and a confirmed save survives a failing readback', async () => {
    const ctx = fixture('instruction'); instructionReady(ctx);
    ctx.el('substitutionReason').value = 'Illness';
    const posts = [];
    ctx.setResponder((url, request) => request.method === 'POST'
        ? new Promise(resolve => posts.push({ resolve, body: JSON.parse(request.body) }))
        : { ok: false, status: 503, json: async () => ({ success: false, message: 'Register offline' }) });
    const e = { preventDefault() {}, currentTarget: ctx.el('substitutionForm') };
    const first = ctx.api.saveSub(e);
    await ctx.api.saveSub(e);
    assert.equal(posts.length, 1);
    posts[0].resolve({ success: true, data: { id: 90 } });
    await first;
    assert.equal(ctx.api.state.substitutionRequestId, null);
    assert.equal(ctx.el('substitutionForm').resets, 1);
    assert.equal(ctx.el('instructionAlert').className, 'alert alert-warning');
    assert.match(ctx.el('instructionAlert').textContent, /scheduled, but.*refresh/);
});

test('Instruction ignores a successful lesson response from an expired tenant session', async () => {
    const ctx = fixture('instruction'); instructionReady(ctx);
    ctx.api.openEditor(null);
    ctx.el('lessonAssignment').value = 'offering-1';ctx.el('lessonTitle').value = 'Geometry';
    ctx.el('lessonDate').value = '2026-09-10';
    const pending = deferred();ctx.setResponder(() => pending.promise);
    const writing = ctx.api.saveLesson({ preventDefault() {}, currentTarget: ctx.el('lessonForm') });
    ctx.body.dataset.authSessionReference = 's2';
    pending.resolve({ success: true, data: { id: 31 } });
    await writing;
    assert.notEqual(ctx.api.state.draftRequestId, null);
    assert.equal(ctx.el('lessonForm').inert, false);
});
