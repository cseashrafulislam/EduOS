'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

function deferred() {
    let resolve;
    const promise = new Promise(res => { resolve = res; });
    return { promise, resolve };
}

function fixture() {
    const nodes = new Map(), guards = [], requests = [], handlers = new Map();
    const selectIds = new Set(['routineBatch','routineSlot','routineTeacher','routineSubject',
        'routineAssignment','routineRoom','routineTeacherTimetable','routineDay']);
    const makeNode = id => ({ id, tagName: selectIds.has(id) ? 'SELECT' : 'INPUT',
        value: '', checked: false, disabled: false, textContent: '', dataset: {}, children: [], elements: [],
        classList: { add() {}, remove() {} },
        get childNodes() { return this.children; },
        addEventListener(event, handler) { handlers.set(id + ':' + event, handler); },
        append(...items) { this.children.push(...items); },
        replaceChildren(...items) {
            this.children = items;
            if (this.tagName === 'SELECT') this.value = items[0]?.value || '';
        },
        reportValidity() { return true; }, focus() {}, click() {}, remove() {},
        reset() {
            this.resets = (this.resets || 0) + 1;
            const fields = this.id === 'routineAssignmentForm'
                ? ['routineAssignFrom','routineAssignTo','routineTeacher','routineSubject']
                : this.id === 'routineEntryForm'
                    ? ['routineEntryFrom','routineEntryTo','routineAssignment','routineSlot','routineRoom']
                    : this.id === 'routineSlotForm'
                        ? ['routineSlotName','routineStartTime','routineEndTime'] : [];
            fields.forEach(id => { if (nodes.has(id)) nodes.get(id).value = ''; });
        }
    });
    const el = id => {
        if (!nodes.has(id)) nodes.set(id, makeNode(id));
        return nodes.get(id);
    };
    const body = { dataset: { authTenantId: '7', authSessionReference: 'session-1', authUserId: 'actor-1' } };
    const document = { body, getElementById: el, createElement: tag => makeNode(tag),
        querySelector: () => ({ value: 'csrf' }), documentElement: { lang: 'en-BD' } };
    el('routineApp').dataset.canManage = 'true';
    let responder = async () => ({ success: true, data: null }), uuid = 0;
    const window = { document, confirm: () => true, addEventListener() {},
        EduOSFormGuard: { create() {
            const guard = { dirty: false, allow: false, cleanCount: 0,
                isDirty() { return this.dirty; },
                confirmDiscard() { return this.allow; },
                markClean() { this.dirty = false; this.cleanCount++; } };
            guards.push(guard); return guard;
        } } };
    const fetch = async (url, options = {}) => {
        requests.push({ url, options });
        const result = await responder(url, options);
        if (result?.ok !== undefined) return result;
        return { ok: true, async json() { return result; } };
    };
    const context = { window, document, fetch, confirm: window.confirm,
        crypto: { randomUUID() { return `request-${++uuid}`; } },
        URLSearchParams, Date, console, setTimeout: fn => fn() };
    const source = read('EduOS.App/wwwroot/js/academic/routines.js').replace(/\s*init\(\);\s*\}\)\(\);\s*$/,
        `globalThis.__ux03 = { state, load, loadCatalog, loadSlots, loadOfferings, loadAssignments,
            loadTeachers, changeBatch, saveSlot, saveAssignment, saveEntry, deactivate, teacherTimetable };})();`);
    vm.runInNewContext(source, context);
    assert.ok(context.__ux03, 'Expose the routines IIFE without executing page bootstrap');
    return { el, body, guards, handlers, requests, api: context.__ux03,
        setResponder(fn) { responder = fn; },
        post() { return requests.filter(x => x.options.method === 'POST'); } };
}

function ready(ctx) {
    ctx.api.state.batches = [{ id: 5, name: 'Batch 5', campusId: 10,
        academicYearId: 1, startDate: '2026-01-01', endDate: '2026-12-31' },
    { id: 6, name: 'Batch 6', campusId: 10, academicYearId: 1,
        startDate: '2026-01-01', endDate: '2026-12-31' }];
    ctx.api.state.rooms = [{ id: 50, name: 'Room 50', code: 'R50', campusId: 10 }];
    ctx.api.state.assignments = [{ id: 31, employeeId: 11, employeeName: 'Teacher A',
        subjectOfferingReference: 'offering-1', subjectOfferingId: 30, isActive: true }];
    ctx.el('routineBatch').value = '5';ctx.api.state.appliedBatch = '5';
    ctx.el('routineAssignFrom').value = '2026-04-01';ctx.el('routineAssignTo').value = '2026-11-30';
    ctx.el('routineTeacher').value = 'teacher-1';ctx.el('routineSubject').value = 'offering-1';
    ctx.el('routineAssignment').value = '31';ctx.el('routineSlot').value = '9';
    ctx.el('routineDay').value = '2';ctx.el('routineRoom').value = '50';
    ctx.el('routineEntryFrom').value = '2026-04-01';ctx.el('routineEntryTo').value = '2026-11-30';
    ctx.el('routineSlotName').value = 'First';ctx.el('routineStartTime').value = '09:00';
    ctx.el('routineEndTime').value = '10:00';
}

function readback(url) {
    if (url.includes('/assignments/page?')) return { success: true, data: { items: [], page: 1, totalCount: 0 } };
    if (url.includes('/instructors/page?')) return { success: true, data: { items: [], page: 1, totalCount: 0 } };
    if (url.includes('/lesson-offerings?')) return { success: true, data: { items: [], page: 1, totalCount: 0 } };
    if (url.includes('/batches/') && url.includes('/page?'))
        return { success: true, data: { items: [], page: 1, totalCount: 0 } };
    if (url.includes('/time-slots')) return { success: true, data: [] };
    return { success: true, data: null };
}

async function until(check) {
    for (let i = 0; i < 15; i++) {
        if (check()) return;
        await new Promise(resolve => setImmediate(resolve));
    }
    throw new Error('Expected async request did not arrive');
}

test('Academic Routines loads the shared transactional guard before application JS', () => {
    const html = read('EduOS.App/Views/AcademicRoutines/Index.cshtml');
    assert.match(html, /form-guard\.js[\s\S]*academic\/routines\.js/);
});

test('Routine batch switch rejects unsaved assignment and entry drafts without losing the selected batch', () => {
    const ctx = fixture();ready(ctx);
    ctx.guards[1].dirty = true;
    ctx.el('routineBatch').value = '6';ctx.api.changeBatch();
    assert.equal(ctx.el('routineBatch').value, '5');
    assert.equal(ctx.guards[1].dirty, true);
    assert.equal(ctx.el('routineAssignmentForm').resets || 0, 0);
    ctx.guards[1].dirty = false;ctx.guards[2].dirty = true;
    ctx.el('routineBatch').value = '6';ctx.api.changeBatch();
    assert.equal(ctx.el('routineBatch').value, '5');
    assert.equal(ctx.guards[2].dirty, true);
});

test('Routine assignment write is single-flight, preserves retry key, then rotates for a changed draft', async () => {
    const ctx = fixture();ready(ctx);
    const posts = [];
    ctx.setResponder((url, opt) => opt.method === 'POST'
        ? new Promise(resolve => posts.push({ resolve, payload: JSON.parse(opt.body) })) : readback(url));
    const event = { preventDefault() {}, currentTarget: ctx.el('routineAssignmentForm') };
    const first = ctx.api.saveAssignment(event);
    await ctx.api.saveAssignment(event);
    assert.equal(posts.length, 1);
    event.currentTarget = null; // DOM resets currentTarget after async event dispatch returns.
    assert.equal(ctx.el('routineFilters').inert, true);
    posts[0].resolve({ ok: false, status: 503, async json() { return { success: false, message: 'Offline' }; } });
    await first;
    assert.equal(ctx.el('routineFilters').inert, false);
    assert.equal(ctx.el('routineAssignmentForm').resets || 0, 0);
    event.currentTarget = ctx.el('routineAssignmentForm');
    const retry = ctx.api.saveAssignment(event);
    event.currentTarget = null;
    assert.equal(posts[1].payload.clientRequestId, posts[0].payload.clientRequestId);
    posts[1].resolve({ ok: false, status: 503, async json() { return { success: false, message: 'Offline' }; } });
    await retry;
    ctx.el('routineAssignFrom').value = '2026-05-01';
    event.currentTarget = ctx.el('routineAssignmentForm');
    const changed = ctx.api.saveAssignment(event);
    event.currentTarget = null;
    assert.notEqual(posts[2].payload.clientRequestId, posts[1].payload.clientRequestId);
    posts[2].resolve({ success: true, data: { id: 88 } });
    await changed;
    assert.equal(ctx.el('routineAssignmentForm').resets, 1);
    assert.equal(ctx.el('routineAlert').className, 'alert alert-success');
    assert.equal(ctx.api.state.assignmentRequest, null);
    assert.equal(ctx.el('routineAssignFrom').value, '2026-01-01');
    assert.equal(ctx.el('routineAssignTo').value, '2026-12-31');
});

test('Routine entry creation is single-flight and reuses the same key after uncertain POST failure', async () => {
    const ctx = fixture();ready(ctx);
    const posts = [];
    ctx.setResponder((url, opt) => opt.method === 'POST'
        ? new Promise(resolve => posts.push({ resolve, payload: JSON.parse(opt.body) })) : readback(url));
    const event = { preventDefault() {}, currentTarget: ctx.el('routineEntryForm') };
    const first = ctx.api.saveEntry(event);
    await ctx.api.saveEntry(event);
    assert.equal(posts.length, 1);
    event.currentTarget = null;
    posts[0].resolve({ ok: false, status: 503, async json() { return { success: false, message: 'Offline' }; } });
    await first;
    event.currentTarget = ctx.el('routineEntryForm');
    const retry = ctx.api.saveEntry(event);
    event.currentTarget = null;
    assert.equal(posts[1].payload.clientRequestId, posts[0].payload.clientRequestId);
    posts[1].resolve({ success: true, data: { id: 60 } });
    await retry;
    assert.equal(ctx.el('routineEntryForm').resets, 1);
    assert.equal(ctx.api.state.entryRequest, null);
    assert.equal(ctx.el('routineEntryFrom').value, '2026-01-01');
});

test('Routine time slot preserves the form on failure and warns when confirmed write cannot reload', async () => {
    const ctx = fixture();ready(ctx);
    const event = { preventDefault() {}, currentTarget: ctx.el('routineSlotForm') };
    ctx.setResponder((url, opt) => opt.method === 'POST'
        ? { ok: false, status: 503, async json() { return { success: false, message: 'Offline' }; } }
        : readback(url));
    await ctx.api.saveSlot(event);
    assert.equal(ctx.el('routineSlotForm').resets || 0, 0);
    assert.match(ctx.el('routineAlert').textContent, /Check existing time slots/);
    ctx.setResponder((url, opt) => opt.method === 'POST' ? { success: true, data: { id: 9 } }
        : { ok: false, status: 503, async json() { return { success: false, message: 'Cannot refresh' }; } });
    await ctx.api.saveSlot(event);
    assert.equal(ctx.el('routineSlotForm').resets, 1);
    assert.equal(ctx.el('routineAlert').className, 'alert alert-warning');
});

test('Routine refresh preserves dirty dates and previously selected paged teacher and offering', async () => {
    const ctx = fixture();ready(ctx);
    ctx.guards[1].dirty = true;ctx.guards[2].dirty = true;
    ctx.setResponder(url => readback(url));
    assert.equal(await ctx.api.load(), true);
    assert.equal(ctx.el('routineAssignFrom').value, '2026-04-01');
    assert.equal(ctx.el('routineEntryFrom').value, '2026-04-01');
    assert.equal(ctx.el('routineTeacher').value, 'teacher-1');
    assert.equal(ctx.el('routineSubject').value, 'offering-1');
    assert.equal(ctx.el('routineAssignment').value, '31');
});

test('Routine ignores outdated teacher/assignment pages after another batch is selected', async () => {
    const ctx = fixture();ready(ctx);
    const pending = deferred();ctx.setResponder(() => pending.promise);
    const load = ctx.api.loadAssignments(1);
    ctx.el('routineBatch').value = '6';ctx.api.state.appliedBatch = '6';
    pending.resolve({ success: true, data: { items: [{ id: 70, employeeName: 'Foreign' }], page: 1 } });
    assert.equal(await load, false);
    assert.equal(ctx.api.state.assignments[0].employeeName, 'Teacher A');
});

test('Routine refuses to apply a successful response from a switched session', async () => {
    const ctx = fixture();ready(ctx);
    const pending = deferred();ctx.setResponder(() => pending.promise);
    const writing = ctx.api.saveAssignment({ preventDefault() {}, currentTarget: ctx.el('routineAssignmentForm') });
    ctx.body.dataset.authSessionReference = 'session-2';
    pending.resolve({ success: true, data: { id: 88 } });
    await writing;
    assert.notEqual(ctx.api.state.assignmentRequest, null);
    assert.equal(ctx.el('routineAssignmentForm').resets || 0, 0);
    assert.equal(ctx.el('routineFilters').inert, false);
});

test('Routine deactivation cannot submit twice while the first write is pending', async () => {
    const ctx = fixture();ready(ctx);
    ctx.api.state.entries = [{ id: 41, rowVersion: 'AQID' }];
    const pending = deferred();ctx.setResponder((url, opt) => opt.method === 'POST' ? pending.promise : readback(url));
    const first = ctx.api.deactivate(41);
    await ctx.api.deactivate(41);
    assert.equal(ctx.post().length, 1);
    pending.resolve({ success: true, data: true });
    await first;
    assert.equal(ctx.el('routineAlert').className, 'alert alert-success');
});
