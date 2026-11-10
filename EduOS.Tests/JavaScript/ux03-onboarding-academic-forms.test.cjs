'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname,
    '../../EduOS.App/wwwroot/js/account/academic-setup.js'), 'utf8');

function setup() {
    const nodes = new Map(), documentEvents = {}, calls = [], guards = [], modals = new Map();
    const newNode = id => ({ id, value: '', checked: false, dataset: {}, textContent: '',
        disabled: false, children: [], handlers: {}, className: '',
        classList: { add() {}, remove() {} },
        addEventListener(event, handler) { this.handlers[event] = handler; },
        setAttribute() {}, removeAttribute() {}, focus() {}, reset() { this.resetCount = (this.resetCount || 0) + 1; },
        replaceChildren(...nodes) { this.children = nodes; }, append(...nodes) { this.children.push(...nodes); }
    });
    const el = id => {
        if (!nodes.has(id)) nodes.set(id, newNode(id));
        return nodes.get(id);
    };
    el('academicSetupStrings').textContent = '{}';
    for (const [id, value] of Object.entries({ yearName: '2026', yearStartDate: '2026-01-01',
        yearEndDate: '2026-12-31', termName: 'Semester 1', termYearId: '1',
        termStartDate: '2026-01-01', termEndDate: '2026-06-30' })) el(id).value = value;
    const document = { body: { dataset: { authTenantId: '7', authSessionReference: 's1' } },
        getElementById: el,
        addEventListener(event, handler) { documentEvents[event] = handler; },
        createElement: newNode,
        querySelector: () => ({ value: 'csrf' }), querySelectorAll: () => [] };
    let handler = async (url, options) => ({ ok: true, status: 200,
        async json() { return { success: true, data: [] }; } });
    const fetch = (url, options) => { calls.push({ url, options }); return handler(url, options); };
    const EduOSFormGuard = {
        create() {
            const guard = { dirty: false, allowed: false, cleans: 0,
                isDirty() { return this.dirty; }, confirmDiscard() { return this.allowed; },
                markClean() { this.cleans++; this.dirty = false; } };
            guards.push(guard);
            return guard;
        },
        protectModal(modal, guard, isSaving) {
            modal.handlers.hide = event => {
                if (isSaving() || !guard.confirmDiscard()) event.preventDefault();
                else guard.markClean();
            };
        }
    };
    const window = { EduOSFormGuard, confirm: () => true,
        location: { assign() {} } };
    const bootstrap = { Modal: { getOrCreateInstance: modal => {
        if (!modals.has(modal)) modals.set(modal, { shown: 0, hidden: 0,
            show() { this.shown++; }, hide() { this.hidden++; } });
        return modals.get(modal);
    } } };
    window.bootstrap = bootstrap;
    vm.runInNewContext(source, { window, document, fetch, bootstrap, console, Date,
        Number, Object }, { filename: 'academic-setup.js' });
    const tick = () => new Promise(resolve => setImmediate(resolve));
    const submit = id => el(id).handlers.submit({ preventDefault() {} });
    return { el, document, guards, calls, submit, tick, modals,
        boot: () => documentEvents.DOMContentLoaded(),
        setFetch(fn) { handler = fn; },
        expire() { document.body.dataset.authTenantId = '8'; } };
}

test('onboarding modal refuses to close and discard a dirty academic editor', async () => {
    const ctx = setup();
    await ctx.boot();
    ctx.guards[0].dirty = true;
    let prevented = 0;
    const event = { preventDefault() { prevented++; } };
    ctx.el('academicYearModal').handlers.hide(event);
    assert.equal(prevented, 1);
    ctx.guards[0].allowed = true;
    ctx.el('academicYearModal').handlers.hide(event);
    assert.equal(prevented, 1);
    assert.equal(ctx.guards[0].dirty, false);
});

test('onboarding year save is single-flight and identifies committed write after readback failure', async () => {
    const ctx = setup();
    await ctx.boot();
    ctx.calls.length = 0;
    let release;
    ctx.setFetch((url, options) => options?.method === 'POST'
        ? new Promise(resolve => { release = resolve; })
        : Promise.reject(Error('readback offline')));
    const first = ctx.submit('academicYearForm');
    await ctx.submit('academicYearForm');
    assert.equal(ctx.calls.filter(x => x.options?.method === 'POST').length, 1);
    assert.equal(ctx.el('saveYearBtn').disabled, true);
    release({ ok: true, status: 200, json: async () => ({ success: true }) });
    await first;
    assert.equal(ctx.el('saveYearBtn').disabled, false);
    assert.ok(ctx.guards[0].cleans > 0);
    assert.match(ctx.el('alertContainer').textContent, /saved, but the list could not refresh/);
    assert.equal(ctx.modals.get(ctx.el('academicYearModal')).hidden, 1);
});

test('onboarding term save does not clear draft on network failure or submit in an expired session', async () => {
    const ctx = setup();
    await ctx.boot();
    ctx.setFetch(() => Promise.reject(Error('offline')));
    await ctx.submit('termForm');
    assert.equal(ctx.el('termForm').resetCount || 0, 0);
    assert.equal(ctx.guards[1].cleans, 0);
    assert.equal(ctx.el('saveTermBtn').disabled, false);
    ctx.expire();
    const count = ctx.calls.length;
    await ctx.submit('termForm');
    assert.equal(ctx.calls.length, count);
});

test('stale onboarding list response cannot render after institution switch', async () => {
    const ctx = setup();
    const pending = [];
    ctx.setFetch(() => new Promise(resolve => pending.push(resolve)));
    const started = ctx.boot();
    await ctx.tick();
    ctx.expire();
    pending.forEach(resolve => resolve({ ok: true, status: 200,
        json: async () => ({ success: true, data: [{ id: 1, name: 'Other tenant year' }] }) }));
    await started;
    assert.equal(ctx.el('yearList').children.length, 0);
});

test('account academic onboarding opts into the shared form guard before its editor', () => {
    const view = fs.readFileSync(path.join(__dirname,
        '../../EduOS.App/Views/Account/AcademicSetup.cshtml'), 'utf8');
    assert.match(view, /platform\/form-guard\.js"[\s\S]*account\/academic-setup\.js"/);
});
