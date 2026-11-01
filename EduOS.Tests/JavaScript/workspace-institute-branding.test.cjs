const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.resolve(__dirname,
    '../../EduOS.App/wwwroot/js/platform/workspace-institute-branding.js'), 'utf8');

function element() {
    return {
        hidden: true, disabled: false, textContent: '', style: {},
        classList: { toggle() {} },
        setAttribute() {}, removeAttribute(key) { delete this[key]; },
        addEventListener() {}
    };
}

async function mount({ sessionMatches = true, admin = true, profileOk = true } = {}) {
    const status = element(), retry = element(), content = element();
    const name = element(), logo = element();
    const rows = ['primary', 'secondary', 'accent'].map(key => {
        const value = element(), swatch = element();
        return {
            dataset: { brandingColor: key }, value, swatch,
            querySelector(selector) {
                return selector === '[data-branding-value]' ? value : swatch;
            }
        };
    });
    const nodes = {
        '[data-branding-status]': status, '[data-branding-retry]': retry,
        '[data-branding-content]': content, '[data-branding-name]': name,
        '[data-branding-logo]': logo
    };
    const section = {
        dataset: { loading: 'Loading', error: 'Failed to load branding' },
        querySelector: selector => nodes[selector],
        querySelectorAll: () => rows
    };
    const calls = [];
    let sessionChecks = 0;
    const fetch = async url => {
        calls.push(url);
        if (url === '/api/tenant-profile') {
            return { ok: profileOk, json: async () => ({
                success: profileOk, data: { name: 'Fictional School', logoUrl: '/uploads/school.png' }
            }) };
        }
        if (url.startsWith('/api/tenant-settings')) {
            return { ok: true, json: async () => ({ success: true, data: {
                items: [{ key: 'Branding.PrimaryColor', value: '#123456' },
                        { key: 'Branding.AccentColor', value: 'url(unsafe)' }]
            } }) };
        }
        return { ok: true, headers: { get: () => sessionMatches ? 'current' : 'other' } };
    };
    const window = {
        setTimeout: () => 1, clearTimeout() {},
        EduOSWorkspaceResponseMatchesSession: response =>
            response.headers.get('X-EduOS-Session-Reference') === 'current',
        EduOSVerifySessionContext: () => { sessionChecks++; }
    };
    vm.runInNewContext(source, {
        document: { getElementById: () => admin ? section : null },
        window, fetch, AbortController, module: { exports: {} }
    });
    await new Promise(resolve => setImmediate(resolve));
    return { calls, status, retry, content, name, logo, rows, sessionChecks };
}

test('TenantAdmin sees sanitized institute branding only after session proof', async () => {
    const result = await mount();
    assert.deepEqual(result.calls, [
        '/api/tenant-profile',
        '/api/tenant-settings?category=Branding&page=1&pageSize=50',
        '/api/workspace-preferences'
    ]);
    assert.equal(result.content.hidden, false);
    assert.equal(result.name.textContent, 'Fictional School');
    assert.equal(result.logo.src, '/uploads/school.png');
    assert.equal(result.rows[0].value.textContent, '#123456');
    assert.equal(result.rows[1].value.textContent, '#64748B');
    assert.equal(result.rows[2].value.textContent, '#F59E0B');
    assert.equal(result.sessionChecks, 0);
});

test('A switched session cannot render a new institute under the old page', async () => {
    const result = await mount({ sessionMatches: false });
    assert.equal(result.content.hidden, true);
    assert.equal(result.name.textContent, '');
    assert.equal(result.logo.src, undefined);
    assert.equal(result.retry.hidden, true);
    assert.equal(result.sessionChecks, 1);
    assert.equal(result.status.textContent, 'Failed to load branding');
});

test('Other roles do not issue institute branding requests; failed reads remain retryable', async () => {
    const other = await mount({ admin: false });
    assert.deepEqual(other.calls, []);
    const failed = await mount({ profileOk: false });
    assert.equal(failed.content.hidden, true);
    assert.equal(failed.retry.hidden, false);
    assert.equal(failed.status.textContent, 'Failed to load branding');
});
