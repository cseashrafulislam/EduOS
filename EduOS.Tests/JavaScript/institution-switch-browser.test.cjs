const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.resolve(__dirname, '../../EduOS.App/wwwroot/js/platform/institution-switch.js'), 'utf8');

function response(data, status = 200) {
    return { ok: status >= 200 && status < 300, status, json: async () => data };
}
function browser() {
    const handlers = { modal: {}, select: {}, submit: {} };
    const requests = [], queue = [], redirects = [], cacheMessages = [];
    const modal = { addEventListener(name, fn) { handlers.modal[name] = fn; } };
    const select = {
        value: '', disabled: false, options: [],
        replaceChildren() { this.options = []; this.value = ''; },
        append(option) { this.options.push(option); },
        addEventListener(name, fn) { handlers.select[name] = fn; }
    };
    const submit = { disabled: false, addEventListener(name, fn) { handlers.submit[name] = fn; } };
    const status = { textContent: '', className: '' };
    const document = {
        documentElement: { lang: 'en-BD' },
        getElementById(id) {
            return ({ institutionSwitchModal: modal, institutionSwitchSelect: select,
                institutionSwitchSubmit: submit, institutionSwitchStatus: status })[id];
        },
        createElement(tag) { assert.equal(tag, 'option'); return { value: '', textContent: '' }; }
    };
    let verified = 0, notified = 0;
    const window = {
        location: {
            replace(url) { redirects.push(['replace', url]); },
            assign(url) { redirects.push(['assign', url]); }
        },
        EduOSVerifySessionContext: async () => { verified++; },
        EduOSNotifySessionChanged: () => { notified++; }
    };
    const navigator = { serviceWorker: { controller: { postMessage(value) { cacheMessages.push(value); } } } };
    const fetch = (url, options) => {
        requests.push({ url, options });
        if (!queue.length) throw new Error('Unexpected network request');
        const next = queue.shift();
        return next instanceof Error ? Promise.reject(next) : Promise.resolve(next);
    };
    vm.runInNewContext(source, { document, window, navigator, fetch, console });
    async function open() {
        queue.push(response({ success: true, data: {
            currentTenantId: 10, institutions: [{ id: 10, name: 'A' }, { id: 20, name: 'B' }]
        } }));
        await handlers.modal['show.bs.modal']();
        select.value = '20';
        handlers.select.change();
        assert.equal(submit.disabled, false);
    }
    return {
        window, requests, queue, redirects, cacheMessages, status, submit, open,
        click: () => handlers.submit.click(),
        get verified() { return verified; },
        get notified() { return notified; }
    };
}

test('successful authorized switch clears PWA runtime and navigates only to dashboard', async () => {
    const b = browser();
    await b.open();
    b.queue.push(response({ success: true, data: { tenantId: 20, redirectUrl: '/Dashboard/Index' } }));
    await b.click();
    assert.deepEqual(b.redirects, [['replace', '/Dashboard/Index']]);
    assert.equal(b.notified, 1);
    assert.deepEqual(b.cacheMessages, [{ type: 'EDUOS_CLEAR_RUNTIME_STATIC' }]);
    assert.equal(b.verified, 0);
    assert.equal(b.requests[1].options.credentials, 'same-origin');
    assert.equal(b.requests[1].options.cache, 'no-store');
});

test('lost switch response immediately reconciles the session before allowing retry', async () => {
    const b = browser();
    await b.open();
    b.queue.push(new Error('Connection interrupted'));
    await b.click();
    assert.equal(b.verified, 1);
    assert.equal(b.submit.disabled, false);
    assert.match(b.status.textContent, /Connection interrupted/);
});

test('malformed success response cannot leave an unverified old institution page', async () => {
    const b = browser();
    await b.open();
    b.queue.push(response({ success: true, data: { tenantId: 999, redirectUrl: '/Dashboard/Index' } }));
    await b.click();
    assert.equal(b.verified, 1);
    assert.equal(b.notified, 0);
    assert.deepEqual(b.redirects, []);
});

test('invalid redirect is rejected and triggers reconciliation rather than navigating to it', async () => {
    const b = browser();
    await b.open();
    b.queue.push(response({ success: true, data: { tenantId: 20, redirectUrl: '//evil.example' } }));
    await b.click();
    assert.equal(b.verified, 1);
    assert.deepEqual(b.redirects, []);
});

test('revoked or conflicting switch returns to sign-in without replaying the write', async () => {
    for (const status of [401, 409]) {
        const b = browser();
        await b.open();
        b.queue.push(response({ success: false }, status));
        await b.click();
        assert.deepEqual(b.redirects, [['assign', '/Account/Login']]);
        assert.equal(b.verified, 0);
        assert.equal(b.requests.filter(x => x.url.endsWith('/switch')).length, 1);
    }
});

test('when the guard is unavailable an ambiguous response fails closed to a safe page', async () => {
    const b = browser();
    await b.open();
    delete b.window.EduOSVerifySessionContext;
    b.queue.push(new Error('Connection interrupted'));
    await b.click();
    assert.deepEqual(b.redirects, [['replace', '/Dashboard/Index']]);
});

test('rapid double click does not submit two institution switch writes', async () => {
    const b = browser();
    await b.open();
    let finish;
    b.queue.push(new Promise(resolve => { finish = resolve; }));
    const first = b.click();
    await b.click();
    assert.equal(b.requests.filter(x => x.url.endsWith('/switch')).length, 1);
    finish(response({ success: true, data: { tenantId: 20, redirectUrl: '/Dashboard/Index' } }));
    await first;
    assert.deepEqual(b.redirects, [['replace', '/Dashboard/Index']]);
});
