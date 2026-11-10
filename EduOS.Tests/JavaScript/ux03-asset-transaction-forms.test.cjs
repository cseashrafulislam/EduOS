'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

function keys() {
    const source = read('EduOS.App/wwwroot/js/inventory/assets-api.js');
    const relevant = source.slice(source.indexOf('const pending = new Map();'), source.indexOf('export function notice'))
        .replace(/export /g, '');
    let sequence = 0;
    const context = { crypto: { randomUUID: () => 'asset-key-' + ++sequence } };
    vm.runInNewContext(relevant + '\nthis.api = { requestKey, clearKey };', context);
    return context.api;
}

function harness(moduleName) {
    const elements = new Map();
    const get = id => {
        if (!elements.has(id)) elements.set(id, {
            value: '', checked: false, inert: false, disabled: false, handlers: {},
            classList: { add() {}, contains() { return false; } },
            addEventListener(name, callback) { this.handlers[name] = callback; },
            reset() { for (const row of elements.values()) row.value = ''; }
        });
        return elements.get(id);
    };
    const submissions = [];
    let attempts = 0;
    const api = async (path, method, body) => {
        if (method !== 'POST') throw Error('Expected a canonical asset POST');
        submissions.push({ path, body: structuredClone(body) });
        attempts++;
        if (attempts <= 2) throw Error('Network uncertain');
        return {};
    };
    const saved = keys();
    const context = { api, el: get, canManage: true, notice() {},
        requestKey: saved.requestKey, clearKey: saved.clearKey, dateToday: () => '2026-11-10',
        money: value => Number(value), selected: { reference: 'asset-A' },
        loadHistory: async () => {}, loadMaintenanceHistory: async () => {}, loadMaintenanceDue: async () => {},
        assignmentGuard: { markClean() {} }, maintenanceGuard: { markClean() {} },
        transferGuard: { markClean() {}, confirmDiscard: () => true }
    };
    const script = read(`EduOS.App/wwwroot/js/inventory/${moduleName}.js`)
        .replace(/^import .*;\s*$/gm, '').replace(/^export /gm, '');
    vm.runInNewContext(script, context, { filename: moduleName });
    return { get, context, submissions };
}

test('Fixed-asset assignment rejects repeat clicks and preserves retry key until the command changes', async () => {
    const { get, context, submissions } = harness('assets-assignment-form');
    context.bindAssignmentForm(async () => {});
    const form = get('assetAssignForm');
    get('assetAssigneeType').value = 'employee';
    get('assetAssignee').value = 'employee-A';
    get('assetAssignedOn').value = '2026-11-10';
    await form.handlers.submit({ preventDefault() {} });
    assert.equal(form.inert, false);
    assert.equal(get('assetAssignee').value, 'employee-A');
    await form.handlers.submit({ preventDefault() {} });
    assert.equal(submissions[0].body.clientRequestId, submissions[1].body.clientRequestId);
    get('assetAssignee').value = 'employee-B';
    await form.handlers.submit({ preventDefault() {} });
    assert.notEqual(submissions[1].body.clientRequestId, submissions[2].body.clientRequestId);
    assert.equal(get('assetAssignee').value, '');
    assert.equal(form.inert, false);
});

test('Fixed-asset maintenance preserves failed cost/narrative and rotates retry key on changed value', async () => {
    const { get, context, submissions } = harness('assets-maintenance-form');
    context.bindMaintenanceForm();
    const form = get('assetMaintenanceForm');
    get('assetWork').value = 'Electrical repair';
    get('assetMaintenanceCost').value = '1250.00';
    await form.handlers.submit({ preventDefault() {} });
    assert.equal(get('assetWork').value, 'Electrical repair');
    await form.handlers.submit({ preventDefault() {} });
    assert.equal(submissions[0].body.clientRequestId, submissions[1].body.clientRequestId);
    get('assetMaintenanceCost').value = '1280.00';
    await form.handlers.submit({ preventDefault() {} });
    assert.notEqual(submissions[1].body.clientRequestId, submissions[2].body.clientRequestId);
    assert.equal(form.inert, false);
    assert.equal(get('assetWork').value, '');
});

test('Asset A to B selection preserves A draft on cancelled discard and refuses to switch during a write', async () => {
    const elements = new Map();
    const get = id => {
        if (!elements.has(id)) elements.set(id, { value: '', inert: false, textContent: '', children: [],
            classList: { add() {}, remove() {} }, focus() {},
            reset() { this.value = ''; }, replaceChildren(...items) { this.children = items; }, append() {} });
        return elements.get(id);
    };
    let allowed = true, prompts = 0;
    const context = { el: get, canManage: true, notice() {}, clearKey() {},
        dateToday: () => '2026-11-10', td: value => value, empty() {}, addButton() {},
        api: async () => [], loadMaintenanceHistory: async () => {},
        assignmentGuard: { markClean() {} }, transferGuard: { markClean() {} }, maintenanceGuard: { markClean() {} },
        confirmDraftDiscard: () => { prompts++; return allowed; },
        document: { createElement: () => ({ append() {} }) }
    };
    get('assetAssignForm').reset = () => { get('assetAssignee').value = ''; };
    const script = read('EduOS.App/wwwroot/js/inventory/assets-custody.js')
        .replace(/^import .*;\s*$/gm, '').replace(/^export /gm, '');
    vm.runInNewContext(script + '\nthis.selectedAsset = () => selected;', context);
    const first = { reference: 'asset-A', assetTag: 'TAG-A', name: 'First', stateCode: 'Available' };
    const second = { reference: 'asset-B', assetTag: 'TAG-B', name: 'Second', stateCode: 'Available' };
    await context.selectAsset(first);
    get('assetAssignee').value = 'employee A draft';
    allowed = false;
    await context.selectAsset(second);
    assert.equal(context.selectedAsset().reference, 'asset-A');
    assert.equal(get('assetAssignee').value, 'employee A draft');
    get('assetAssignForm').inert = true;
    allowed = true;
    await context.selectAsset(second);
    assert.equal(context.selectedAsset().reference, 'asset-A', 'saving form blocks context switch');
    get('assetAssignForm').inert = false;
    await context.selectAsset(second);
    assert.equal(context.selectedAsset().reference, 'asset-B');
    assert.equal(get('assetAssignee').value, '', 'confirmed switch resets the previous asset assignment draft');
    assert.equal(prompts, 3, 'busy switch does not ask to discard');
});

test('Fixed-asset forms protect record switches, lock POSTs, preserve authoritative APIs and keep local business dates', () => {
    const view = read('EduOS.App/Views/AssetLifecycle/Index.cshtml');
    assert.match(view, /form-guard\.js"[\s\S]*assets\.js"/);
    const register = read('EduOS.App/wwwroot/js/inventory/assets-register.js');
    assert.match(register, /registerGuard\.confirmDiscard\(\)/);
    const category = read('EduOS.App/wwwroot/js/inventory/assets-categories.js');
    assert.match(category, /categoryGuard\.confirmDiscard\(\)/);
    const custody = read('EduOS.App/wwwroot/js/inventory/assets-custody.js');
    assert.match(custody, /confirmDraftDiscard\(\[assignmentGuard, transferGuard, maintenanceGuard\]\)/);
    assert.match(custody, /selected\?\.reference !== reference/);
    const transfer = read('EduOS.App/wwwroot/js/inventory/assets-transfer-form.js');
    assert.match(transfer, /requestKey\('transfer', \{ assignmentId: id, \.\.\.body \}\)/);
    assert.match(transfer, /form\.inert = true/);
    const api = read('EduOS.App/wwwroot/js/inventory/assets-api.js');
    assert.match(api, /value\.getFullYear\(\)/);
    assert.doesNotMatch(api, /dateToday = \(\) => new Date\(\)\.toISOString/);
    const idempotency = keys();
    const original = idempotency.requestKey('transfer', { assignmentId: 1, target: 'A' });
    assert.equal(original, idempotency.requestKey('transfer', { assignmentId: 1, target: 'A' }));
    assert.notEqual(original, idempotency.requestKey('transfer', { assignmentId: 1, target: 'B' }));
});
