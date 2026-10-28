const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.resolve(__dirname, '../../EduOS.App/wwwroot/js/platform/session-context-guard.js'), 'utf8');
const reference = 'A'.repeat(64);
const other = 'B'.repeat(64);
const flush = () => new Promise(resolve => setImmediate(resolve));
const deferred = () => {
    let resolve, reject;
    const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
    return { promise, resolve, reject };
};
function browser() {
    const handlers = { window: {}, document: {}, retry: {} };
    const calls = [], redirects = [], timers = [];
    const shield = { hidden: true };
    const message = { textContent: '' };
    const retry = { focus() {}, addEventListener(name, cb) { handlers.retry[name] = cb; } };
    const shell = { inert: false, setAttribute(name) { if (name === 'inert') this.inert = true; }, removeAttribute(name) { if (name === 'inert') this.inert = false; } };
    const body = { dataset: { authSessionReference: reference, authTenantId: '12' } };
    const document = {
        body, documentElement: { lang: 'en-BD' }, visibilityState: 'visible',
        getElementById(id) { return ({ eduosSessionShield: shield, eduosSessionShieldMessage: message, eduosSessionRetry: retry })[id]; },
        querySelector() { return shell; },
        addEventListener(name, cb) { handlers.document[name] = cb; }
    };
    const window = {
        location: { replace(url) { redirects.push(url); } },
        addEventListener(name, cb) { handlers.window[name] = cb; },
        setTimeout(cb, ms) { const timer = { cb, ms }; timers.push(timer); return timer; },
        clearTimeout(timer) { timer.cleared = true; }
    };
    class BroadcastChannel {
        constructor() { window.channel = this; }
        postMessage() {}
    }
    const fetch = (url, options) => {
        assert.equal(url, '/api/auth/session-context');
        assert.equal(options.cache, 'no-store');
        assert.equal(options.credentials, 'same-origin');
        assert.ok(options.signal);
        const d = deferred();
        options.signal.addEventListener('abort', () => d.reject(new Error('aborted')), { once: true });
        calls.push(d);
        return d.promise;
    };
    vm.runInNewContext(source, { document, window, fetch, AbortController, BroadcastChannel, console });
    const respond = (index, sessionReference = reference, tenantId = 12, status = 200) => calls[index].resolve({
        status, ok: status === 200,
        json: async () => ({ success: true, data: { sessionReference, tenantId } })
    });
    return { handlers, calls, redirects, timers, shield, message, shell, body, window, respond };
}

test('overlapping focus and visibility checks never reveal stale institution data', async () => {
    const b = browser();
    b.handlers.window.focus();
    assert.equal(b.shield.hidden, false);
    b.handlers.document.visibilitychange();
    assert.equal(b.calls.length, 1);
    b.respond(0);
    await flush(); await flush();
    assert.equal(b.shield.hidden, false, 'the queued second check must keep the privacy shield');
    assert.equal(b.calls.length, 2);
    b.respond(1, other);
    await flush(); await flush();
    assert.deepEqual(b.redirects, ['/Dashboard/Index']);
});

test('tenant mismatch redirects even if the session reference is unchanged', async () => {
    const b = browser();
    b.handlers.window.focus();
    b.respond(0, reference, 99);
    await flush(); await flush();
    assert.equal(b.shield.hidden, false);
    assert.deepEqual(b.redirects, ['/Dashboard/Index']);
});

test('network failure remains shielded and retry restores a verified session', async () => {
    const b = browser();
    b.handlers.window.focus();
    b.calls[0].reject(new Error('offline'));
    await flush(); await flush();
    assert.equal(b.shield.hidden, false);
    assert.match(b.message.textContent, /Reconnect and retry/);
    b.handlers.retry.click();
    b.respond(1);
    await flush(); await flush();
    assert.equal(b.shield.hidden, true);
    assert.equal(b.shell.inert, false);
});

test('same-tenant MFA rotation ignores stale in-flight response and revalidates', async () => {
    const b = browser();
    b.handlers.window.focus();
    assert.equal(b.window.EduOSAdoptRotatedSession(other, 12), true);
    b.respond(0, reference, 12);
    await flush(); await flush();
    assert.deepEqual(b.redirects, []);
    assert.equal(b.shield.hidden, false);
    assert.equal(b.calls.length, 2);
    b.respond(1, other, 12);
    await flush(); await flush();
    assert.equal(b.shield.hidden, true);
    assert.deepEqual(b.redirects, []);
    assert.equal(b.body.dataset.authSessionReference, other);
});

test('focus always revalidates, including within five seconds of a prior success', async () => {
    const b = browser();
    b.handlers.window.focus();
    b.respond(0);
    await flush(); await flush();
    assert.equal(b.shield.hidden, true);
    b.handlers.window.focus();
    assert.equal(b.shield.hidden, false);
    assert.equal(b.calls.length, 2);
    b.respond(1);
    await flush(); await flush();
    assert.equal(b.shield.hidden, true);
});

test('revoked session sends the tab to sign-in without revealing old content', async () => {
    const b = browser();
    b.handlers.window.focus();
    b.respond(0, reference, 12, 401);
    await flush(); await flush();
    assert.deepEqual(b.redirects, ['/Account/Login']);
    assert.equal(b.shield.hidden, false);
});

test('a hung session check times out to an accessible retry state', async () => {
    const b = browser();
    b.handlers.window.focus();
    const timeout = b.timers.find(t => t.ms === 10000);
    assert.ok(timeout);
    timeout.cb();
    await flush(); await flush();
    assert.equal(b.shield.hidden, false);
    assert.match(b.message.textContent, /Reconnect and retry/);
    b.handlers.retry.click();
    b.respond(1);
    await flush(); await flush();
    assert.equal(b.shield.hidden, true);
});

test('an uncertain institution switch can invoke immediate session reconciliation', async () => {
    const b = browser();
    assert.equal(typeof b.window.EduOSVerifySessionContext, 'function');
    const verification = b.window.EduOSVerifySessionContext();
    assert.equal(b.shield.hidden, false);
    b.respond(0, other, 99);
    await verification;
    await flush();
    assert.deepEqual(b.redirects, ['/Dashboard/Index']);
    assert.equal(b.shield.hidden, false);
});

test('cross-tab logout stays shielded while server still reports the old session', async () => {
    const b = browser();
    b.window.channel.onmessage({ data: { type: 'session-changed' } });
    assert.equal(b.shield.hidden, false);
    assert.equal(b.calls.length, 1);
    b.respond(0);
    await flush(); await flush();
    assert.equal(b.shield.hidden, false, 'old cookie response must not reveal private content');
    assert.match(b.message.textContent, /Another tab is changing your session/);
    const delayed = b.timers.find(t => t.ms === 1200);
    assert.ok(delayed);
    delayed.cb();
    b.respond(1);
    await flush(); await flush();
    assert.equal(b.shield.hidden, false, 'second old cookie response must also remain shielded');
    b.handlers.retry.click();
    b.respond(2);
    await flush(); await flush();
    assert.equal(b.shield.hidden, true, 'explicit retry and fresh verification may restore the page');
});

test('cross-tab session change during in-flight verification cannot reveal old data', async () => {
    const b = browser();
    b.handlers.window.focus();
    b.window.channel.onmessage({ data: { type: 'session-changed' } });
    assert.equal(b.calls.length, 1);
    b.respond(0);
    await flush(); await flush();
    assert.equal(b.shield.hidden, false);
    assert.equal(b.calls.length, 2);
    b.respond(1, other, 99);
    await flush(); await flush();
    assert.deepEqual(b.redirects, ['/Dashboard/Index']);
    assert.equal(b.shield.hidden, false);
});


test('terminal redirect cannot be undone by queued checks, retry or MFA adoption', async () => {
    const b = browser();
    b.handlers.window.focus();
    b.handlers.document.visibilitychange();
    b.respond(0, other, 99);
    await flush(); await flush();
    assert.deepEqual(b.redirects, ['/Dashboard/Index']);
    assert.equal(b.window.EduOSSessionTransitionPending(), true);
    assert.equal(b.window.EduOSAdoptRotatedSession(reference, 12), false);
    b.handlers.retry.click();
    b.handlers.window.focus();
    assert.equal(b.calls.length, 1);
    assert.equal(b.shield.hidden, false);
    assert.equal(b.shell.inert, true);
});

test('revoked session redirect remains terminal when focus rechecks were queued', async () => {
    const b = browser();
    b.handlers.window.focus();
    b.handlers.document.visibilitychange();
    b.respond(0, reference, 12, 401);
    await flush(); await flush();
    b.handlers.retry.click();
    assert.deepEqual(b.redirects, ['/Account/Login']);
    assert.equal(b.calls.length, 1);
    assert.equal(b.shield.hidden, false);
});

test('cross-tab transition is visible to private workspace consumers', async () => {
    const b = browser();
    assert.equal(b.window.EduOSSessionTransitionPending(), false);
    b.window.channel.onmessage({ data: { type: 'session-changed' } });
    assert.equal(b.window.EduOSSessionTransitionPending(), true);
    b.respond(0);
    await flush(); await flush();
    assert.equal(b.window.EduOSSessionTransitionPending(), true);
    b.handlers.retry.click();
    assert.equal(b.window.EduOSSessionTransitionPending(), false);
    b.respond(1);
    await flush(); await flush();
    assert.equal(b.shield.hidden, true);
});
