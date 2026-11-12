const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const script = fs.readFileSync(path.resolve(__dirname, '../../EduOS.App/wwwroot/js/tenant/smart-setup.js'), 'utf8');

function createElement(tag = 'div', id = '') {
    return {
        tag, id, children: [], listeners: {}, attributes: {}, dataset: {},
        checked: false, disabled: false, className: '', textContent: '', value: '',
        append(...values) { this.children.push(...values); },
        replaceChildren(...values) { this.children = [...values]; },
        setAttribute(key, value) { this.attributes[key] = String(value); },
        addEventListener(name, handler) { this.listeners[name] = handler; },
        querySelectorAll(selector) {
            const all = [];
            function walk(node) {
                if (selector === 'input[type=checkbox]' && node.tag === 'input' && node.type === 'checkbox')
                    all.push(node);
                for (const child of node.children || []) walk(child);
            }
            walk(this);
            return all;
        },
        focus() {}
    };
}
function find(node, match) {
    if (match(node)) return node;
    for (const item of node.children || []) {
        const hit = find(item, match);
        if (hit) return hit;
    }
    return null;
}
function setup() {
    const ids = ['smartSetupWorkspace', 'smartSetupNotice', 'smartSetupInstitution',
        'smartSetupVersion', 'smartSetupReload', 'smartSetupReadinessSummary',
        'smartSetupModules', 'smartSetupSelection', 'smartSetupApply',
        'smartSetupConfirm', 'smartSetupForm'];
    const elems = new Map(ids.map(id => [id, createElement('div', id)]));
    const root = elems.get('smartSetupWorkspace');
    root.dataset.language = 'en-BD';
    root.querySelector = () => ({ value: 'csrf-token' });
    const body = { dataset: { authTenantId: '25', authSessionReference: 'session' } };
    const data = {
        institutionTypeId: 5, institutionTypeCode: 'SCHOOL', institutionTypeName: 'School and College',
        countryCode: 'BD', templateVersion: '2026.11.1',
        modules: [
            { code: 'ACADEMIC', name: 'Academic references', nameBangla: 'একাডেমিক',
                isAvailable: true, supportsSafeApply: true, entries: [
                    { kind: 'Medium', code: 'BANGLA', name: 'Bangla', status: 'Missing' },
                    { kind: 'Medium', code: 'ENGLISH', name: 'English', status: 'Existing' }
                ] },
            { code: 'HR', name: 'HR', nameBangla: 'এইচআর',
                isAvailable: true, supportsSafeApply: true, entries: [
                    { kind: 'OrganizationUnit', code: 'ADMIN', name: 'Administration', status: 'Missing' },
                    { kind: 'OrganizationUnit', code: 'ACADEMIC', name: 'Academics', status: 'Missing' }
                ] }
        ]
    };
    const requests = [];
    async function fetch(url, init = {}) {
        requests.push({ url, init });
        if (url === '/api/smart-setup/apply') {
            const request = JSON.parse(init.body);
            for (const entry of request.entries) {
                const row = data.modules.find(x => x.code === entry.module)?.entries
                    .find(x => x.kind === entry.kind && x.code === entry.code);
                if (row) row.status = 'Existing';
            }
            return { ok: true, json: async () => ({ success: true, data: {
                createdCount: request.entries.length, existingCount: 0
            } }) };
        }
        const value = url === '/api/smart-setup/preview' ? data :
            data.modules.map(m => ({ moduleCode: m.code, isAvailable: true,
                hasChecklist: false, requirements: [] }));
        return { ok: true, json: async () => ({ success: true, data: value }) };
    }
    const context = {
        document: { body, getElementById: id => elems.get(id) || null,
            createElement },
        window: { EduOSSessionTransitionPending: () => false,
            location: { search: '', origin: 'http://localhost' } },
        URL, URLSearchParams, Uint8Array, fetch,
        crypto: { randomUUID: () => '983c3a58-1d81-43ac-8be1-2423c6ebd1fa' },
        console
    };
    new vm.Script(script, { filename: 'smart-setup.js' }).runInNewContext(context);
    const ready = async () => {
        for (let i = 0; i < 8; i++)
            await new Promise(resolve => setImmediate(resolve));
    };
    const selectedBox = key => find(elems.get('smartSetupModules'),
        node => node.attributes?.['data-smart-row'] === key);
    return { elems, data, requests, ready, selectedBox };
}
test('reviewed missing rows are rendered as pre-filled readonly columns, with no automatic POST', async () => {
    const h = setup();
    await h.ready();
    const table = find(h.elems.get('smartSetupModules'), x => x.tag === 'table');
    assert.ok(table);
    const bangla = find(table, x => x.tag === 'input' && x.value === 'BANGLA');
    const english = find(table, x => x.tag === 'input' && x.value === 'ENGLISH');
    assert.ok(bangla);
    assert.equal(bangla.readOnly, true);
    assert.equal(english, null);
    assert.ok(h.selectedBox('ACADEMIC|Medium|BANGLA'));
    assert.equal(h.selectedBox('ACADEMIC|Medium|ENGLISH'), null);
    assert.equal(h.requests.filter(r => r.init.method === 'POST').length, 0);
});
test('only ticked rows are submitted and server-side template values are not edited', async () => {
    const h = setup();
    await h.ready();
    const checkbox = h.selectedBox('HR|OrganizationUnit|ADMIN');
    checkbox.checked = true;
    checkbox.listeners.change();
    const confirmation = h.elems.get('smartSetupConfirm');
    confirmation.checked = true;
    confirmation.listeners.change();
    assert.equal(h.elems.get('smartSetupApply').disabled, false);
    await h.elems.get('smartSetupForm').listeners.submit({ preventDefault() {} });
    const posted = h.requests.filter(r => r.init.method === 'POST');
    assert.equal(posted.length, 1);
    const sent = JSON.parse(posted[0].init.body);
    assert.deepEqual(Array.from(sent.modules), ['HR']);
    assert.deepEqual(JSON.parse(JSON.stringify(sent.entries)), [
        { module: 'HR', kind: 'OrganizationUnit', code: 'ADMIN' }
    ]);
    assert.equal(posted[0].init.headers.RequestVerificationToken, 'csrf-token');
    assert.equal(h.data.modules[1].entries[1].status, 'Missing');
});
test('select all only checks missing rows and cannot write without final confirmation', async () => {
    const h = setup();
    await h.ready();
    const all = find(h.elems.get('smartSetupModules'),
        n => n.attributes?.['data-smart-all'] === 'HR');
    assert.ok(all);
    all.checked = true;
    all.listeners.change();
    assert.equal(h.selectedBox('HR|OrganizationUnit|ADMIN').checked, true);
    assert.equal(h.selectedBox('HR|OrganizationUnit|ACADEMIC').checked, true);
    assert.equal(h.elems.get('smartSetupApply').disabled, true);
    assert.equal(h.requests.filter(r => r.init.method === 'POST').length, 0);
});
