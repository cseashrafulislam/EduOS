const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.resolve(__dirname, '../../EduOS.App/wwwroot/js/facilities/spaces.js'), 'utf8');
const read = file => fs.readFileSync(path.resolve(__dirname, '../..', file), 'utf8');
const { guideFor } = require('../../EduOS.App/wwwroot/js/platform/page-context-help.js');
const flush = async () => { await new Promise(resolve => setImmediate(resolve)); await new Promise(resolve => setImmediate(resolve)); };
const response = (data, status = 200) => ({
    ok: status >= 200 && status < 300, status,
    json: async () => ({ success: status < 400, data, message: typeof data === 'string' ? data : null })
});
function deferred() {
    let resolve;
    const promise = new Promise(yes => { resolve = yes; });
    return { promise, resolve };
}
function browser({ bn = false, manage = true, token = 'token', sessionOk = true, initial } = {}) {
    const events = {};
    const nodes = {};
    const make = () => ({
        value: '', checked: false, disabled: false, textContent: '', className: '', children: [],
        dataset: {}, attributes: {}, listeners: {},
        append(child) { this.children.push(child); },
        replaceChildren() { this.children = []; },
        setAttribute(name, value) { this.attributes[name] = value; },
        addEventListener(name, cb) { this.listeners[name] = cb; },
        scrollIntoView() {}, focus() { this.focused = true; },
        querySelectorAll() { return []; }
    });
    for (const id of ['facilitySpaces','facilityAlert','facilityRows','facilityPage','facilityPrev',
        'facilityNext','facilitySearchForm','facilityCampusFilter','facilitySearch','facilityForm',
        'facilityReset','facilityId','facilityVersion','facilityCampus','facilityCode','facilityName',
        'facilityAddress','facilityActive','eduosSessionShield']) nodes[id] = make();
    const root = nodes.facilitySpaces;
    root.dataset = { bn: String(bn), manage: String(manage) };
    const shield = nodes.eduosSessionShield;
    shield.hidden = true;
    const saveButton = make();
    nodes.facilityForm.querySelector = selector => selector === 'button[type="submit"]' ? saveButton : null;
    nodes.facilityForm.querySelectorAll = () => [saveButton, nodes.facilityReset,
        nodes.facilityCampus, nodes.facilityCode, nodes.facilityName, nodes.facilityAddress];
    nodes.facilityForm.reportValidity = () => true;
    nodes.facilityForm.reset = () => {
        for (const id of ['facilityCampus','facilityCode','facilityName','facilityAddress']) nodes[id].value = '';
        nodes.facilityActive.checked = true;
    };
    const document = {
        body: { dataset: { authSessionReference: 'A'.repeat(64), authTenantId: '12' } },
        getElementById: id => nodes[id] || null,
        querySelector: selector => selector.includes('request-verification-token') ? { content: token } : null,
        createElement: make
    };
    const queue = [], calls = [];
    queue.push(initial === undefined ? response(page) : initial);
    const fetch = (url, options) => {
        calls.push({ url, options });
        if (!queue.length) throw new Error('Unexpected network call: ' + url);
        return queue.shift();
    };
    const window = {
        confirm: () => true, addEventListener: (name, cb) => { events[name] = cb; },
        EduOSSessionTransitionPending: () => false,
        EduOSVerifySessionContext: async () => { shield.hidden = sessionOk; }
    };
    vm.runInNewContext(source, { document, window, fetch, URLSearchParams, AbortController, console });
    const submit = (id, event = {}) => nodes[id].listeners.submit({ preventDefault() {}, ...event });
    const findEdit = () => nodes.facilityRows.children[0]?.children.at(-1)?.children[0];
    return { nodes, calls, queue, window, events, submit, findEdit, saveButton,
        setSessionOk: value => { sessionOk = value; }, flush };
}
const page = { items: [
    { id: 5, campusId: 12, code: 'LAB-A', name: 'Lab A', address: 'North', isActive: true, rowVersion: 'rv1' }
], totalPages: 1 };
test('reviewed facility space manual matches protected route and existing list/editor', () => {
    const view = read('EduOS.App/Views/FacilitySpaces/Index.cshtml');
    const controller = read('EduOS.App/Controllers/FacilitySpacesController.cs');
    const api = read('EduOS.App/Controllers/Api/FacilitySpacesApiController.cs');
    const service = read('EduOS.Service/Services/Facilities/FacilitySpaceService.cs');
    assert.match(controller, /Authorize\(Roles = "TenantAdmin,Principal,FacilitiesManager,FacilitiesTechnician,FacilitiesApprover"\)/);
    assert.match(controller, /RequireModule\("FACILITIES"\)/);
    assert.match(api, /AutoValidateAntiforgeryToken/);
    assert.match(service, /AllowedCampusIds\(\)/);
    assert.match(service, /MatchesVersion\(existing.RowVersion, request.RowVersion\)/);
    for (const id of ['facilitySearchForm','facilityRows','facilityPage','facilityForm','facilityVersion'])
        assert.ok(view.includes('id="' + id + '"'), id);
    for (const locale of ['en-BD','bn-BD']) {
        const guide = guideFor('/FacilitySpaces/Index', locale);
        assert.ok(guide, locale);
        assert.equal(guide.steps.length, 5);
        assert.match(guide.rules, locale === 'en-BD' ? /not a work-order approval/ : /অনুমোদন/);
        assert.deepEqual(guide.related.map(link => link.path), ['/FacilityWorkOrders/Index']);
    }
});
test('facility list is authorized-session verified before rendering and has accessible edit focus', async () => {
    const b = browser();
    await b.flush();
    assert.equal(b.nodes.facilityRows.children.length, 1);
    assert.equal(b.nodes.facilityRows.children[0].children[1].textContent, 'LAB-A');
    assert.equal(b.nodes.facilitySpaces.attributes['aria-busy'], 'false');
    const edit = b.findEdit();
    assert.match(edit.attributes['aria-label'], /LAB-A/);
    edit.listeners.click();
    assert.equal(b.nodes.facilityVersion.value, 'rv1');
    assert.equal(b.nodes.facilityName.focused, true);
});
test('rapid searches discard old responses even when an aborted response resolves late', async () => {
    const old = deferred();
    const b = browser({ initial: old.promise });
    b.nodes.facilitySearch.value = 'new';
    b.queue.push(response({ items: [{ ...page.items[0], code: 'NEW' }], totalPages: 1 }));
    b.submit('facilitySearchForm');
    await b.flush();
    assert.equal(b.nodes.facilityRows.children[0].children[1].textContent, 'NEW');
    old.resolve(response(page));
    await b.flush();
    assert.equal(b.nodes.facilityRows.children[0].children[1].textContent, 'NEW');
    assert.equal(b.calls[0].options.signal.aborted, true);
});
test('missing CSRF token and duplicate save clicks never send unsafe writes', async () => {
    const absent = browser({ token: '' });
    await absent.flush();
    absent.nodes.facilityCampus.value = '12';
    absent.nodes.facilityCode.value = 'LAB-A';
    absent.nodes.facilityName.value = 'Lab A';
    await absent.submit('facilityForm');
    assert.equal(absent.calls.filter(c => c.options.method === 'POST').length, 0);
    assert.match(absent.nodes.facilityAlert.textContent, /Security token missing/);

    const b = browser();
    await b.flush();
    b.nodes.facilityCampus.value = '12';
    b.nodes.facilityCode.value = 'LAB-A';
    b.nodes.facilityName.value = 'Lab A';
    const pending = deferred();
    b.queue.push(pending.promise);
    const first = b.submit('facilityForm');
    await b.submit('facilityForm');
    await b.flush();
    assert.equal(b.calls.filter(c => c.options.method === 'POST').length, 1);
    assert.equal(b.saveButton.disabled, true);
    b.queue.push(response(page));
    pending.resolve(response(page.items[0], 201));
    await first;
    await b.flush();
    const writes = b.calls.filter(c => c.options.method === 'POST');
    assert.equal(writes.length, 1);
    assert.equal(writes[0].options.headers.RequestVerificationToken, 'token');
    assert.equal(b.saveButton.disabled, false);
});
test('session mismatch fails closed, clears rows and blocks later writes', async () => {
    const b = browser({ sessionOk: false });
    await b.flush();
    assert.equal(b.nodes.facilityRows.children.length, 0);
    assert.equal(b.nodes.facilityForm.querySelectorAll()[0].disabled, true);
    assert.match(b.nodes.facilityAlert.textContent, /session changed/);
    b.nodes.facilityCampus.value = '12';
    await b.submit('facilityForm');
    assert.equal(b.calls.filter(c => c.options.method !== 'GET').length, 0);
});
test('known concurrency conflict uses Bangla recovery and retains editor values', async () => {
    const b = browser({ bn: true });
    await b.flush();
    b.findEdit().listeners.click();
    b.queue.push(response('Space changed; reload.', 409));
    await b.submit('facilityForm');
    assert.match(b.nodes.facilityAlert.textContent, /পরিবর্তিত হয়েছে/);
    assert.equal(b.nodes.facilityVersion.value, 'rv1');
    assert.equal(b.calls.filter(c => c.options.method === 'PUT').length, 1);
});

test('invalid campus filter clears prior rows instead of showing stale results', async () => {
    const b = browser();
    await b.flush();
    assert.equal(b.nodes.facilityRows.children[0].children[1].textContent, 'LAB-A');
    b.nodes.facilityCampusFilter.value = '0';
    b.submit('facilitySearchForm');
    await b.flush();
    assert.match(b.nodes.facilityAlert.textContent, /valid campus ID/);
    assert.match(b.nodes.facilityRows.children[0].children[0].textContent, /No spaces found/);
    assert.equal(b.calls.length, 1);
});
test('session switch clears an edited space before locking the old page', async () => {
    const b = browser();
    await b.flush();
    b.findEdit().listeners.click();
    assert.equal(b.nodes.facilityName.value, 'Lab A');
    b.setSessionOk(false);
    b.queue.push(response(page));
    b.submit('facilitySearchForm');
    await b.flush();
    assert.equal(b.nodes.facilityName.value, '');
    assert.equal(b.nodes.facilityId.value, '');
    assert.equal(b.nodes.facilityRows.children.length, 0);
    assert.equal(b.saveButton.disabled, true);
});
