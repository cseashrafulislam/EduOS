'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const script = fs.readFileSync(path.join(__dirname,
    '../../EduOS.App/wwwroot/js/academic/curricula.js'), 'utf8');

function setup() {
    const nodes = new Map(), listeners = new Map(), calls = [], notices = [];
    let session = true, implementation = async url => url.includes('curricula?')
        ? { items: [], totalCount: 0 } : { isSubjectMappingLocked: false };
    let reload = async () => {};
    const make = tag => ({
        tag, value: '', textContent: '', disabled: false, children: [], options: [], handlers: {},
        classList: { add() {}, remove() {} },
        addEventListener(type, handler) { this.handlers[type] = handler; },
        append(...items) { this.children.push(...items); },
        replaceChildren(...items) { this.children = [...items]; this.options = [...items]; },
        add(option) { this.options.push(option); }
    });
    const el = id => {
        if (!nodes.has(id)) nodes.set(id, make(id));
        return nodes.get(id);
    };
    const window = {
        eduosAcademicManagement: {
            el, state: { catalog: { programs: [{ id: 1, name: 'Program' }] } },
            api: (url, options) => { calls.push({ url, options }); return implementation(url, options); },
            notice: (message, danger) => notices.push({ message, danger }),
            reload: () => reload(), sameSession: () => session
        },
        addEventListener(name, handler) { listeners.set(name, handler); }
    };
    class Option {
        constructor(text, value) { this.text = text; this.value = value; }
    }
    vm.runInNewContext(script, { window, document: { createElement: make }, Option,
        URLSearchParams, URL, Blob, confirm: () => true }, { filename: 'curricula.js' });
    const tick = () => new Promise(resolve => setImmediate(resolve));
    const search = async () => {
        el('curriculumSearchForm').handlers.submit({ preventDefault() {} });
        await tick();
    };
    const actions = (row, index = 0) => el('curriculumRows').children[row].children.at(-1).children[index];
    return { el, calls, notices, listeners, tick, search, actions,
        setSession(value) { session = value; }, setApi(fn) { implementation = fn; },
        setReload(fn) { reload = fn; } };
}

const row = (id, current = true) => ({ id, name: 'Curriculum ' + id,
    code: 'CUR' + id, academicProgramId: 1, versionNo: 1, effectiveFrom: '2026-01-01',
    rowVersion: 'v' + id, isCurrent: current, isActive: true, subjectCount: 1,
    subjectOfferingCount: 0, studentEnrollmentCount: 0 });
const detail = id => ({ ...row(id), subjects: [{ academicLevelName: 'Year 1',
    subjectName: 'Maths', fullMarks: 100, passMarks: 40, creditHours: 3,
    isOptional: false, isActive: true }] });

test('later curriculum selection wins if an older detail response arrives last', async () => {
    const ctx = setup(), pending = new Map();
    ctx.setApi(url => {
        if (url.includes('curricula?')) return Promise.resolve({ items: [row(1), row(2)], totalCount: 2 });
        const id = Number(/\/(\d+)$/.exec(url)?.[1]);
        return new Promise(resolve => pending.set(id, resolve));
    });
    await ctx.search();
    const old = ctx.actions(0).handlers.click();
    const latest = ctx.actions(1).handlers.click();
    pending.get(2)(detail(2));
    await latest;
    pending.get(1)(detail(1));
    await old;
    assert.match(ctx.el('curriculumDetails').children[0].textContent, /Curriculum 2/);
});

test('activation is single-flight and confirmed write survives a failed readback', async () => {
    const ctx = setup();
    let release, writes = 0;
    ctx.setApi((url, options) => {
        if (options?.method === 'PUT') {
            writes++;
            return new Promise(resolve => { release = resolve; });
        }
        return Promise.resolve({ items: [row(3, false)], totalCount: 1 });
    });
    ctx.setReload(() => Promise.reject(Error('network timeout')));
    await ctx.search();
    const activate = ctx.actions(0, 1);
    const first = activate.handlers.click();
    await activate.handlers.click();
    assert.equal(writes, 1);
    assert.equal(activate.disabled, true);
    release({});
    await first;
    assert.equal(activate.disabled, false);
    assert.equal(ctx.notices.at(-1).danger, true);
    assert.match(ctx.notices.at(-1).message, /activated, but the display could not refresh/i);
});

test('expired institution session ignores in-flight list and mapping replies', async () => {
    const ctx = setup();
    const replies = new Map();
    ctx.setApi(url => new Promise(resolve => replies.set(url, resolve)));
    const selection = ctx.el('curriculumSelection');
    selection.value = '4';
    ctx.listeners.get('eduos:academic-reloaded')();
    await ctx.tick();
    const check = replies.get('/api/academic-setup/curricula/4');
    assert.equal(typeof check, 'function');
    ctx.setSession(false);
    check({ isSubjectMappingLocked: false });
    for (const [url, resolve] of replies) {
        if (url.includes('curricula?')) resolve({ items: [row(4)], totalCount: 1 });
    }
    await ctx.tick();
    assert.equal(ctx.el('curriculumMappingSubmit').disabled, true);
    assert.equal(ctx.el('curriculumRows').children.length, 0);
    assert.equal(ctx.notices.length, 0);
});

test('mapping eligibility from prior selection cannot enable a newly selected locked curriculum', async () => {
    const ctx = setup(), pending = new Map();
    ctx.setApi(url => url.includes('curricula?')
        ? Promise.resolve({ items: [], totalCount: 0 })
        : new Promise(resolve => pending.set(url, resolve)));
    const select = ctx.el('curriculumSelection');
    select.value = '6';
    select.handlers.change();
    select.value = '7';
    select.handlers.change();
    pending.get('/api/academic-setup/curricula/7')({ isSubjectMappingLocked: true,
        subjectMappingLockReason: 'History locked' });
    await ctx.tick();
    pending.get('/api/academic-setup/curricula/6')({ isSubjectMappingLocked: false });
    await ctx.tick();
    assert.equal(ctx.el('curriculumMappingSubmit').disabled, true);
    assert.match(ctx.el('curriculumMappingNotice').textContent, /History locked/);
});
