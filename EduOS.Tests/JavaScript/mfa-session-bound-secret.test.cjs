const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.resolve(__dirname, '../../EduOS.App/wwwroot/js/account/mfa-setup.js'), 'utf8');
const OLD = 'A'.repeat(64), NEXT = 'B'.repeat(64), OTHER = 'C'.repeat(64);
const response = (data, reference = OLD, tenant = '10', status = 200) => ({
    ok: status >= 200 && status < 300, status,
    headers: { get: name => name === 'X-EduOS-Session-Reference' ? reference :
        name === 'X-EduOS-Tenant-Id' ? tenant : null },
    json: async () => ({ success: status === 200, data })
});

async function browser(queue) {
    const elements = new Map();
    const el = id => {
        if (!elements.has(id)) elements.set(id, {
            id, dataset: {}, hidden: false, value: '', textContent: '',
            listeners: {}, children: [], focus() {},
            addEventListener(name, fn) { this.listeners[name] = fn; },
            setAttribute(name, value) { this[name] = value; },
            replaceChildren() { this.children = []; },
            appendChild(child) { this.children.push(child); }
        });
        return elements.get(id);
    };
    const form = el('mfaSetupForm');
    form.dataset.authSessionReference = OLD;
    form.dataset.networkMessage = 'Session verification failed';
    form.dataset.requiredMessage = 'Required';
    form.dataset.setupFailed = 'Setup failed';
    form.dataset.enableFailed = 'Enable failed';
    el('currentPassword').value = 'correct-password';
    el('mfaEnableCode').value = '123456';
    el('mfaKeyPanel').hidden = true;
    el('mfaRecoveryPanel').hidden = true;
    const meta = { content: 'old-token' };
    let onReady;
    const requests = [];
    const document = {
        body: { dataset: { authTenantId: '10' } },
        documentElement: { lang: 'en-BD' },
        addEventListener(name, fn) { if (name === 'DOMContentLoaded') onReady = fn; },
        getElementById: el,
        querySelector: selector => selector === 'meta[name="request-verification-token"]' ? meta : null,
        querySelectorAll: () => [],
        createElement: id => el('generated-' + id + '-' + Math.random())
    };
    const window = { EduOSNotifySessionChanged() {} };
    const fetch = async url => {
        requests.push(url);
        const next = queue.shift();
        assert.ok(next, 'unexpected request: ' + url);
        assert.equal(url, next.url);
        return next.value;
    };
    vm.runInNewContext(source, { document, window, fetch, console });
    await onReady();
    return {
        el, meta, requests,
        setup: () => el('mfaSetupButton').listeners.click(),
        enable: () => form.listeners.submit({ preventDefault() {} })
    };
}

test('MFA setup page rejects an initial status response from a different cookie', async () => {
    const b = await browser([{ url: '/api/auth/mfa/status',
        value: response({ enabled: false }, OTHER) }]);
    assert.equal(b.el('mfaSetupForm').hidden, true);
    assert.equal(b.el('mfaSetupButton').listeners.click, undefined);
    assert.equal(b.el('mfaKeyPanel').hidden, true);
});

test('same-tenant replacement cookie cannot disclose an old authenticator key', async () => {
    const b = await browser([
        { url: '/api/auth/mfa/status', value: response({ enabled: false }) },
        { url: '/api/auth/mfa/setup', value: response({
            sharedKey: 'secret', authenticatorUri: 'otpauth://totp/secret', sessionReference: NEXT
        }) },
        { url: '/api/auth/antiforgery', value: response({
            requestToken: 'new-token', sessionReference: OTHER, tenantId: 10
        }) }
    ]);
    await b.setup();
    assert.equal(b.el('mfaKeyPanel').hidden, true);
    assert.equal(b.el('mfaSharedKey').textContent, '');
    assert.equal(b.meta.content, 'old-token');
});

test('MFA recovery codes stay hidden if the rotated cookie cannot be verified', async () => {
    const b = await browser([
        { url: '/api/auth/mfa/status', value: response({ enabled: false }) },
        { url: '/api/auth/mfa/enable', value: response({
            recoveryCodes: ['private-recovery-code'], sessionReference: NEXT
        }) },
        { url: '/api/auth/antiforgery', value: response({
            requestToken: 'new-token', sessionReference: OTHER, tenantId: 10
        }) }
    ]);
    await b.enable();
    assert.equal(b.el('mfaRecoveryPanel').hidden, true);
    assert.equal(b.el('mfaRecoveryCodes').children.length, 0);
    assert.equal(b.el('mfaSetupForm').hidden, true);
    assert.equal(b.meta.content, 'old-token');
});

test('matching rotated cookie permits authenticator setup and one-time recovery display', async () => {
    const b = await browser([
        { url: '/api/auth/mfa/status', value: response({ enabled: false }) },
        { url: '/api/auth/mfa/setup', value: response({
            sharedKey: 'secret', authenticatorUri: 'otpauth://totp/secret', sessionReference: NEXT
        }) },
        { url: '/api/auth/antiforgery', value: response({
            requestToken: 'token-next', sessionReference: NEXT, tenantId: 10
        }) },
        { url: '/api/auth/mfa/enable', value: response({
            recoveryCodes: ['recovery-1', 'recovery-2'], sessionReference: OTHER
        }) },
        { url: '/api/auth/antiforgery', value: response({
            requestToken: 'token-other', sessionReference: OTHER, tenantId: 10
        }) }
    ]);
    await b.setup();
    assert.equal(b.el('mfaKeyPanel').hidden, false);
    assert.equal(b.el('mfaSharedKey').textContent, 'secret');
    assert.equal(b.meta.content, 'token-next');
    await b.enable();
    assert.equal(b.el('mfaRecoveryPanel').hidden, false);
    assert.equal(b.el('mfaRecoveryCodes').children.length, 2);
    assert.equal(b.meta.content, 'token-other');
});
