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
        removeAttribute(key) { delete this.attributes[key]; if (key === 'src') this.src = ''; },
        addEventListener(name, handler) { this.listeners[name] = handler; },
        scrollIntoView() {},
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
function setup({ hostel = false } = {}) {
    const ids = ['smartSetupWorkspace', 'smartSetupNotice', 'smartSetupInstitution',
        'smartSetupVersion', 'smartSetupReload', 'smartSetupReadinessSummary',
        'smartSetupModules', 'smartSetupSelection', 'smartSetupApply',
        'smartSetupConfirm', 'smartSetupForm', 'smartSetupSelectAll',
        'smartSetupClearSelected', 'smartSetupInline', 'smartSetupFrame',
        'smartSetupInlineTitle', 'smartSetupInlineClose', 'smartSetupInlineRefresh'];
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
    if (hostel) data.modules.push({
        code: 'HOSTEL', name: 'Hostel', nameBangla: 'হোস্টেল',
        isAvailable: true, supportsSafeApply: false, entries: []
    });
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
            data.modules.map(m => ({
                moduleCode: m.code, isAvailable: true,
                hasChecklist: hostel && m.code === 'HOSTEL',
                requirements: hostel && m.code === 'HOSTEL'
                    ? [
                        { code: 'HOSTEL_BUILDING', label: 'Hostel / residence',
                            labelBangla: 'হোস্টেল', configured: false,
                            actionUrl: '/HostelOperations/Setup' },
                        { code: 'HOSTEL_ROOM', label: 'Rooms and capacity',
                            labelBangla: 'কক্ষ ও ধারণক্ষমতা', configured: false,
                            actionUrl: '/HostelOperations/Setup' },
                        { code: 'HOSTEL_BED', label: 'Bed inventory',
                            labelBangla: 'শয্যা', configured: false,
                            actionUrl: '/HostelOperations/Setup' }
                    ] : []
            }));
        return { ok: true, json: async () => ({ success: true, data: value }) };
    }
    const context = {
        document: { body, getElementById: id => elems.get(id) || null,
            createElement, addEventListener() {} },
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
    return { elems, data, requests, ready, selectedBox, body };
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


test('select all missing initial records does not create data without confirmation', async () => {
    const h = setup();
    await h.ready();
    h.elems.get('smartSetupSelectAll').listeners.click();
    assert.equal(h.selectedBox('ACADEMIC|Medium|BANGLA').checked, true);
    assert.equal(h.selectedBox('HR|OrganizationUnit|ADMIN').checked, true);
    assert.equal(h.selectedBox('HR|OrganizationUnit|ACADEMIC').checked, true);
    assert.equal(h.selectedBox('ACADEMIC|Medium|ENGLISH'), null);
    assert.equal(h.elems.get('smartSetupApply').disabled, true);
    h.elems.get('smartSetupConfirm').checked = true;
    h.elems.get('smartSetupConfirm').listeners.change();
    assert.equal(h.elems.get('smartSetupApply').disabled, false);
    await h.elems.get('smartSetupForm').listeners.submit({ preventDefault() {} });
    const posts = h.requests.filter(x => x.init.method === 'POST');
    assert.equal(posts.length, 1);
    const selected = JSON.parse(posts[0].init.body);
    assert.equal(selected.entries.length, 3);
    assert.equal(selected.modules.length, 2);
    h.elems.get('smartSetupClearSelected').listeners.click();
    assert.equal(h.elems.get('smartSetupApply').disabled, true);
});

test('Hostel prerequisite opens original authorized setup form inside Smart Setup', async () => {
    const h = setup({ hostel: true });
    await h.ready();
    const button = find(h.elems.get('smartSetupModules'),
        x => x.attributes?.['data-smart-setup-inline'] === 'HOSTEL:HOSTEL_BUILDING');
    assert.ok(button);
    function countActions(node) {
        return (node.attributes?.['data-smart-setup-inline'] ? 1 : 0) +
            (node.children || []).reduce((sum, child) => sum + countActions(child), 0);
    }
    assert.equal(countActions(h.elems.get('smartSetupModules')), 1,
        'Three Hostel prerequisites sharing one URL must have one Add here action');
    button.listeners.click();
    assert.equal(h.elems.get('smartSetupInline').hidden, false);
    assert.equal(h.elems.get('smartSetupFrame').src,
        '/HostelOperations/Setup?smartSetupEmbedded=1');
    assert.equal(h.requests.filter(x => x.init.method === 'POST').length, 0);
    assert.ok(find(h.elems.get('smartSetupModules'),
        x => x.tag === 'a' && x.href === '/HostelOperations/Setup'));
    h.elems.get('smartSetupInlineClose').listeners.click();
    await h.ready();
    assert.equal(h.elems.get('smartSetupInline').hidden, true);
    assert.equal(h.elems.get('smartSetupFrame').src, '');
});

test('changed institution session cannot reopen an embedded Setup form', async () => {
    const h = setup({ hostel: true });
    await h.ready();
    h.body.dataset.authTenantId = '26';
    const button = find(h.elems.get('smartSetupModules'),
        x => x.attributes?.['data-smart-setup-inline'] === 'HOSTEL:HOSTEL_BUILDING');
    button.listeners.click();
    assert.notEqual(h.elems.get('smartSetupInline').hidden, false);
    assert.equal(h.elems.get('smartSetupFrame').src, undefined);
});
