const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

test('connectivity notice reacts to browser offline and online events without storing data', () => {
    const handlers = {};
    const notice = { hidden: true };
    const document = {
        querySelector: selector => selector === '[data-connectivity-notice]' ? notice : null,
        querySelectorAll: () => [],
        getElementById: () => null,
        body: { dataset: { workspacePreferences: 'disabled' } },
        documentElement: { dataset: {} },
        addEventListener: () => {}
    };
    const navigator = { onLine: true };
    const window = {
        fetch: () => Promise.resolve(),
        addEventListener: (name, handler) => { handlers[name] = handler; },
        matchMedia: () => ({ matches: false, addEventListener: () => {} }),
        location: { href: 'https://eduos.example.test/', origin: 'https://eduos.example.test' }
    };
    vm.runInNewContext(read('EduOS.App/wwwroot/js/site.js'),
        { window, document, navigator, Request, URL, Headers, HTMLElement: class {} });
    assert.equal(notice.hidden, true);
    navigator.onLine = false;
    handlers.offline();
    assert.equal(notice.hidden, false);
    navigator.onLine = true;
    handlers.online();
    assert.equal(notice.hidden, true);
});

test('service worker excludes private, redirected, authenticated and non-static responses', () => {
    const source = read('EduOS.App/wwwroot/service-worker.js');
    assert.match(source, /response\.redirected/);
    assert.match(source, /request\.headers\.has\('Authorization'\)/);
    assert.match(source, /private\|no-store\|no-cache/);
    assert.match(source, /isPublicStaticResponse\(request, response\)/);
    const context = {
        self: { addEventListener: () => {}, location: { origin: 'https://eduos.example.test' } },
        caches: {}, URL
    };
    vm.runInNewContext(source, context);
    const request = { url: 'https://eduos.example.test/css/site.css', headers: new Headers(), cache: 'default' };
    const response = (cacheControl = 'public, max-age=300', contentType = 'text/css', redirected = false) => ({
        ok: true, type: 'basic', redirected,
        headers: new Headers({ 'Cache-Control': cacheControl, 'Content-Type': contentType })
    });
    assert.equal(context.isCacheableStaticPath('/css/site.css'), true);
    assert.equal(context.isCacheableStaticPath('/api/students'), false);
    assert.equal(context.isCacheableStaticPath('/images/tenant/private-record'), false);
    assert.equal(context.isPublicStaticResponse(request, response()), true);
    assert.equal(context.isPublicStaticResponse(request, response('private, max-age=300')), false);
    assert.equal(context.isPublicStaticResponse(request, response('no-store')), false);
    assert.equal(context.isPublicStaticResponse(request, response('public', 'text/html')), false);
    assert.equal(context.isPublicStaticResponse(request, response('public', 'text/css', true)), false);
    request.headers.set('Authorization', 'Bearer test');
    assert.equal(context.isPublicStaticResponse(request, response()), false);
});

test('logout clears runtime cache without removing public offline shell', async () => {
    const handlers = {};
    const removed = [];
    const entries = [
        { url: 'https://eduos.example.test/offline.html' },
        { url: 'https://eduos.example.test/css/site.css' },
        { url: 'https://eduos.example.test/js/platform/page-context-help.js?v=abc' }
    ];
    const cache = {
        keys: async () => entries,
        delete: async request => { removed.push(request.url); return true; }
    };
    const context = {
        self: {
            addEventListener: (name, callback) => { handlers[name] = callback; },
            location: { origin: 'https://eduos.example.test' }
        },
        caches: { open: async () => cache }, URL
    };
    vm.runInNewContext(read('EduOS.App/wwwroot/service-worker.js'), context);
    let pending;
    handlers.message({ data: { type: 'EDUOS_CLEAR_RUNTIME_STATIC' }, waitUntil: promise => { pending = promise; } });
    await pending;
    assert.deepEqual(removed, ['https://eduos.example.test/js/platform/page-context-help.js?v=abc']);
    assert.equal(handlers.message({ data: { type: 'UNTRUSTED' }, waitUntil: () => { throw Error('Unexpected purge'); } }), undefined);
    const pageLogout = read('EduOS.App/Controllers/AccountController.cs');
    const apiLogout = read('EduOS.App/Controllers/Api/AuthController.cs');
    assert.match(pageLogout, /Clear-Site-Data/);
    assert.match(apiLogout, /Clear-Site-Data/);
    assert.match(read('EduOS.App/wwwroot/js/site.js'), /EDUOS_CLEAR_RUNTIME_STATIC/);
});

test('authenticated and no-store static requests bypass existing CacheStorage hits', () => {
    const handlers = {};
    const context = {
        self: { addEventListener: (name, fn) => { handlers[name] = fn; },
            location: { origin: 'https://eduos.example.test' } },
        caches: { match: () => { throw Error('Cache must not be read'); } }, URL,
        fetch: () => { throw Error('Fetch is browser-owned without respondWith'); }
    };
    vm.runInNewContext(read('EduOS.App/wwwroot/service-worker.js'), context);
    for (const cacheMode of ['no-store', 'reload']) {
        let handled = false;
        handlers.fetch({ request: { method: 'GET', url: 'https://eduos.example.test/css/site.css',
            headers: new Headers(), cache: cacheMode, mode: 'cors' },
            respondWith: () => { handled = true; } });
        assert.equal(handled, false);
    }
    let handled = false;
    handlers.fetch({ request: { method: 'GET', url: 'https://eduos.example.test/css/site.css',
        headers: new Headers({ Authorization: 'Bearer test' }), cache: 'default', mode: 'cors' },
        respondWith: () => { handled = true; } });
    assert.equal(handled, false);
    const request = { url: 'https://eduos.example.test/css/site.css',
        headers: new Headers(), cache: 'default' };
    const response = { ok: true, type: 'basic', redirected: false,
        headers: new Headers({ 'Cache-Control': 'public', 'Content-Type': 'text/css', Vary: 'Cookie' }) };
    assert.equal(context.isPublicStaticResponse(request, response), false);
});
