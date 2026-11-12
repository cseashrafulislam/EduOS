const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.resolve(__dirname, '../..');
const guardSource = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/platform/session-context-guard.js'), 'utf8');
const reference = 'A'.repeat(64);
const valid = (sessionReference = reference, tenantId = 42) => ({
    status: 200, ok: true,
    json: async () => ({ success: true, data: { sessionReference, tenantId } })
});
const settle = async () => {
    for (let i = 0; i < 5; i++) await new Promise(resolve => setTimeout(resolve, 5));
};

function harness(fetchHandler) {
    const events = { window: {}, document: {}, retry: {} };
    const classes = new Set();
    const attributes = new Set();
    const calls = [];
    const redirects = [];
    let focusCount = 0;
    const shield = {
        hidden: true,
        classList: {
            toggle(name, enabled) { if (enabled) classes.add(name); else classes.delete(name); },
            contains(name) { return classes.has(name); },
            remove(name) { classes.delete(name); }
        }
    };
    const shell = {
        setAttribute(name) { attributes.add(name); },
        removeAttribute(name) { attributes.delete(name); }
    };
    const message = { textContent: '' };
    const retry = {
        focus() { focusCount++; },
        addEventListener(name, callback) { events.retry[name] = callback; }
    };
    const elements = { eduosSessionShield: shield, eduosSessionShieldMessage: message, eduosSessionRetry: retry };
    const document = {
        visibilityState: 'visible',
        documentElement: { lang: 'en-BD' },
        body: { dataset: { authSessionReference: reference, authTenantId: '42' } },
        getElementById(id) { return elements[id]; },
        querySelector(selector) { return selector === '.app-shell' ? shell : null; },
        addEventListener(name, callback) { events.document[name] = callback; }
    };
    const window = {
        navigator: { onLine: true },
        location: { replace(url) { redirects.push(url); } },
        addEventListener(name, callback) { events.window[name] = callback; },
        setTimeout(callback, delay) { return setTimeout(callback, delay === 350 ? 0 : delay); },
        clearTimeout
    };
    const fetch = (...args) => {
        calls.push(args);
        return fetchHandler(...args);
    };
    vm.runInNewContext(guardSource, { document, window, fetch, AbortController }, {
        filename: 'session-context-guard.js', timeout: 1000
    });
    return {
        document, window, shield, message, calls, redirects,
        classes, attributes,
        get focusCount() { return focusCount; },
        emit(target, name, event = {}) {
            assert.equal(typeof events[target][name], 'function', target + '.' + name);
            events[target][name](event);
        }
    };
}

test('tab visibility restore verifies once, without a Retry modal or focus theft', async () => {
    const h = harness(async () => valid());
    h.emit('window', 'focus'); // Initial load is not a tab return.
    assert.equal(h.calls.length, 0);
    h.document.visibilityState = 'hidden';
    h.emit('document', 'visibilitychange');
    assert.equal(h.shield.hidden, false);
    assert.equal(h.classes.has('auth-session-shield--quiet'), true);
    assert.equal(h.attributes.has('inert'), true);
    assert.equal(h.focusCount, 0);
    h.document.visibilityState = 'visible';
    h.emit('document', 'visibilitychange');
    h.emit('window', 'focus'); // The same activation must not issue another request.
    await settle();
    assert.equal(h.calls.length, 1);
    assert.equal(h.shield.hidden, true);
    assert.equal(h.attributes.has('inert'), false);
    assert.equal(h.focusCount, 0);
    assert.deepEqual(h.redirects, []);
});

test('genuine window blur/focus verifies once without a blocking dialog', async () => {
    const h = harness(async () => valid());
    h.emit('window', 'blur');
    h.emit('window', 'focus');
    h.emit('window', 'focus');
    await settle();
    assert.equal(h.calls.length, 1);
    assert.equal(h.shield.hidden, true);
    assert.equal(h.focusCount, 0);
});

test('transient verification error is retried before showing recovery UI', async () => {
    let attempts = 0;
    const h = harness(async () => {
        if (++attempts === 1) throw Error('temporary network error');
        return valid();
    });
    h.emit('window', 'blur');
    h.emit('window', 'focus');
    await settle();
    assert.equal(attempts, 2);
    assert.equal(h.shield.hidden, true);
    assert.equal(h.focusCount, 0);
});

test('persistent network errors keep stale forms covered and allow explicit recovery', async () => {
    let fail = true;
    const h = harness(async () => {
        if (fail) throw Error('offline');
        return valid();
    });
    h.emit('window', 'blur');
    h.emit('window', 'focus');
    await settle();
    assert.equal(h.calls.length, 2);
    assert.equal(h.shield.hidden, false);
    assert.equal(h.classes.has('auth-session-shield--quiet'), false);
    assert.equal(h.attributes.has('inert'), true);
    assert.match(h.message.textContent, /Unable to verify/);
    assert.ok(h.focusCount > 0);
    fail = false;
    h.emit('retry', 'click');
    await settle();
    assert.equal(h.shield.hidden, true);
    assert.equal(h.attributes.has('inert'), false);
});

test('tenant changes and revoked sessions redirect without revealing old content', async () => {
    for (const [serverReply, destination] of [
        [valid('B'.repeat(64), 43), '/Dashboard/Index'],
        [{ status: 401, ok: false }, '/Account/Login']
    ]) {
        const h = harness(async () => serverReply);
        h.document.visibilityState = 'hidden';
        h.emit('document', 'visibilitychange');
        h.document.visibilityState = 'visible';
        h.emit('document', 'visibilitychange');
        await settle();
        assert.deepEqual(h.redirects, [destination]);
        assert.equal(h.shield.hidden, false);
        assert.equal(h.attributes.has('inert'), true);
    }
});

test('quiet CSS does not bypass the explicit fail-closed recovery card', () => {
    const css = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/css/site.css'), 'utf8');
    assert.match(css, /\.auth-session-shield--quiet \.auth-session-shield-card \{ display: none; \}/);
    assert.match(css, /\.auth-session-shield\[hidden\]/);
    assert.doesNotMatch(guardSource, /localStorage|sessionStorage|innerHTML/);
});
