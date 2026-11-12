const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.resolve(__dirname, '../..');
const script = fs.readFileSync(path.join(root,
    'EduOS.App/wwwroot/js/tenant/smart-setup-handoff.js'), 'utf8');

function element(tag = 'div', id = '') {
    return {
        tag, id, children: [], listeners: {}, textContent: '', value: '',
        className: '', checked: false, disabled: false,
        append(...items) { this.children.push(...items); },
        prepend(...items) { this.children.unshift(...items); },
        replaceChildren(...items) { this.children = [...items]; },
        setAttribute() {},
        addEventListener(event, callback) { this.listeners[event] = callback; },
        dispatchEvent(event) { this.listeners[event.type]?.(event); },
        scrollIntoView() {}, focus() {}
    };
}

function harness({ url = '/HrSetup/Index?smartSetup=HR', edit = false, entries = null, requirements = null } = {}) {
    const parsed = new URL('http://localhost' + url);
    const main = element('main', 'main-content');
    const body = { dataset: { authTenantId: '11', authSessionReference: 'session-1' } };
    const controls = new Map([['main-content', main]]);
    const ids = [
        'hrSetupDepartmentForm', 'hrSetupDepartmentId', 'hrSetupDepartmentCode', 'hrSetupDepartmentName',
        'hrSetupDesignationForm', 'hrSetupDesignationId', 'hrSetupDesignationCode', 'hrSetupDesignationName'
    ];
    for (const id of ids) controls.set(id, element(id.endsWith('Form') ? 'form' : 'input', id));
    if (edit) {
        controls.get('hrSetupDepartmentCode').value = 'USER-DRAFT';
        controls.get('hrSetupDepartmentName').value = 'Custom department';
    }
    const calls = [];
    let reloadCount = 0;
    const previews = [
        { kind: 'OrganizationUnit', code: 'ADMIN', name: 'Administration', status: 'Missing' },
        { kind: 'OrganizationUnit', code: 'ACADEMIC', name: 'Academics', status: 'Missing' },
        { kind: 'Designation', code: 'OFFICE-STAFF', name: 'Office Staff', status: 'Missing' }
    ];
    const payload = {
        institutionTypeId: 20, institutionTypeName: 'School and College',
        templateVersion: '2026.11.1',
        modules: [{ code: new URLSearchParams(parsed.search).get('smartSetup') || 'HR',
            isAvailable: true, supportsSafeApply: true, entries: entries || previews, name: 'Setup' }]
    };
    const fetch = async (route, options = {}) => {
        calls.push({ route, options });
        const data = route.endsWith('/preview') ? payload :
            route.includes('/readiness?') ?
                [{ moduleCode: new URLSearchParams(parsed.search).get('smartSetup') || 'HR',
                    isAvailable: true, requirements: requirements || [] }] :
                { createdCount: 3, existingCount: 0 };
        return { ok: true, json: async () => ({ success: true, data }) };
    };
    const context = {
        location: {
            pathname: parsed.pathname, search: parsed.search,
            reload() { reloadCount++; }
        },
        document: {
            body, documentElement: { lang: 'en-BD' },
            getElementById: id => controls.get(id),
            querySelector: query => query.includes('request-verification-token')
                ? { content: 'csrf-token' } : null,
            createElement: tag => element(tag)
        },
        window: { EduOSSessionTransitionPending: () => false },
        fetch, URLSearchParams, encodeURIComponent, Uint8Array,
        crypto: { randomUUID: () => 'c6b88ab9-41ca-40fb-bfe2-bd1621ca70e9' },
        Event: class Event { constructor(type) { this.type = type; } }
    };
    new vm.Script(script, { filename: 'smart-setup-handoff.js' }).runInNewContext(context);
    const ready = async () => {
        for (let i = 0; i < 7; i++) await new Promise(resolve => setImmediate(resolve));
    };
    return { controls, main, calls, ready, getReloadCount: () => reloadCount };
}

function descend(node, predicate) {
    if (predicate(node)) return node;
    for (const item of node.children || []) {
        const found = descend(item, predicate);
        if (found) return found;
    }
    return null;
}

test('Smart Setup deep-link loads reviewed safe data into empty forms but never saves on load', async () => {
    const h = harness();
    await h.ready();
    assert.equal(h.controls.get('hrSetupDepartmentCode').value, 'ADMIN');
    assert.equal(h.controls.get('hrSetupDepartmentName').value, 'Administration');
    assert.equal(h.controls.get('hrSetupDesignationCode').value, 'OFFICE-STAFF');
    assert.equal(h.calls.filter(x => x.options.method === 'POST').length, 0);
    assert.ok(descend(h.main, x => x.textContent.includes('Academics (ACADEMIC)')));
});

test('Smart Setup deep-link does not overwrite an existing institution form draft', async () => {
    const h = harness({ edit: true });
    await h.ready();
    assert.equal(h.controls.get('hrSetupDepartmentCode').value, 'USER-DRAFT');
    assert.equal(h.controls.get('hrSetupDepartmentName').value, 'Custom department');
    assert.equal(h.controls.get('hrSetupDesignationCode').value, 'OFFICE-STAFF');
});

test('Smart Setup handoff rejects a module in the wrong route without fetching or writing', async () => {
    const h = harness({ url: '/AccountingSetup/Index?smartSetup=HR' });
    await h.ready();
    assert.equal(h.calls.length, 0);
    assert.equal(h.main.children.length, 0);
});

test('Smart Setup destination requires confirmation and uses original CSRF-protected apply API', async () => {
    const h = harness();
    await h.ready();
    const check = descend(h.main, x => x.tag === 'input' && x.type === 'checkbox');
    const save = descend(h.main, x => x.tag === 'button' &&
        x.textContent.startsWith('Add 3 missing'));
    assert.ok(check);
    assert.ok(save);
    assert.equal(save.disabled, true);
    check.checked = true;
    check.dispatchEvent({ type: 'change' });
    assert.equal(save.disabled, false);
    await save.listeners.click();
    const posts = h.calls.filter(x => x.options.method === 'POST');
    assert.equal(posts.length, 1);
    assert.equal(posts[0].route, '/api/smart-setup/apply');
    assert.equal(posts[0].options.headers.RequestVerificationToken, 'csrf-token');
    const request = JSON.parse(posts[0].options.body);
    assert.deepEqual(Array.from(request.modules), ['HR']);
    assert.equal(request.confirmed, true);
    assert.equal(h.getReloadCount(), 1);
});

test('completed academic defaults and work on another academic page do not show a panel', async () => {
    const h = harness({
        url: '/AcademicManagement/Years?smartSetup=ACADEMIC',
        entries: [
            { kind: 'Medium', code: 'BANGLA', name: 'Bangla', status: 'Existing' },
            { kind: 'Medium', code: 'ENGLISH', name: 'English', status: 'Existing' }
        ],
        requirements: [
            { configured: false, actionUrl: '/AcademicManagement/Curricula', label: 'Reviewed curriculum' },
            { configured: false, actionUrl: '/AcademicManagement/Sections', label: 'Sections' }
        ]
    });
    await h.ready();
    assert.equal(h.main.children[0].className.includes('d-none'), true);
    assert.equal(h.calls.filter(x => x.options.method === 'POST').length, 0);
});

test('academic curriculum shows only its own incomplete prerequisite', async () => {
    const h = harness({
        url: '/AcademicManagement/Curricula?smartSetup=ACADEMIC',
        entries: [
            { kind: 'Medium', code: 'BANGLA', name: 'Bangla', status: 'Existing' },
            { kind: 'Medium', code: 'ENGLISH', name: 'English', status: 'Existing' }
        ],
        requirements: [
            { configured: false, actionUrl: '/AcademicManagement/Curricula', label: 'Reviewed curriculum' },
            { configured: false, actionUrl: '/AcademicManagement/Sections', label: 'Sections' }
        ]
    });
    await h.ready();
    assert.equal(h.main.children[0].className.includes('d-none'), false);
    assert.ok(descend(h.main, x => x.textContent === 'Reviewed curriculum'));
    assert.equal(descend(h.main, x => x.textContent === 'Bangla (BANGLA)'), null);
    assert.equal(descend(h.main, x => x.textContent === 'Sections'), null);
});

test('only completed HR presets do not render another setup panel', async () => {
    const h = harness({
        entries: [{ kind: 'OrganizationUnit', code: 'ADMIN', name: 'Administration', status: 'Existing' }]
    });
    await h.ready();
    assert.equal(h.main.children[0].className.includes('d-none'), true);
    assert.equal(h.controls.get('hrSetupDepartmentCode').value, '');
});
