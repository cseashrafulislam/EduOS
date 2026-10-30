const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

const source = fs.readFileSync(path.resolve(__dirname, '../../EduOS.App/wwwroot/service-worker.js'), 'utf8');
const ORIGIN = 'https://eduos.example';

function headers(values = {}) {
    const normalized = new Map(Object.entries(values).map(([key, value]) => [key.toLowerCase(), value]));
    return {
        has: key => normalized.has(key.toLowerCase()),
        get: key => normalized.get(key.toLowerCase()) ?? null
    };
}
function request(pathname, opts = {}) {
    return {
        url: pathname.startsWith('http') ? pathname : ORIGIN + pathname,
        method: opts.method || 'GET', mode: opts.mode || 'same-origin',
        cache: opts.cache || 'default', headers: headers(opts.headers)
    };
}
function response(body, opts = {}) {
    return {
        body, ok: opts.ok ?? true, type: opts.type || 'basic',
        redirected: opts.redirected || false,
        headers: headers({ 'Content-Type': opts.contentType || 'text/javascript', ...opts.headers }),
        clone() { return response(body, opts); }
    };
}
function deferred() {
    let resolve, reject;
    const promise = new Promise((res, rej) => { resolve = res; reject = rej; });
    return { promise, resolve, reject };
}
function worker() {
    const handlers = {}, stores = new Map(), network = [], queued = [];
    let skipWaiting = 0, claim = 0;
    let putBarrier = null;
    const key = req => typeof req === 'string' ? new URL(req, ORIGIN).href : req.url;
    function store(name) {
        if (!stores.has(name)) stores.set(name, new Map());
        const entries = stores.get(name);
        return {
            match: async req => entries.get(key(req)),
            put: async (req, res) => { if (putBarrier) await putBarrier.promise; entries.set(key(req), res); },
            delete: async req => entries.delete(key(req)),
            keys: async () => [...entries.keys()].map(url => ({ url })),
            addAll: async paths => { for (const p of paths) entries.set(key(p), response('public:' + p, {
                contentType: p.endsWith('.css') ? 'text/css' : p.endsWith('.svg') ? 'image/svg+xml' :
                    p.endsWith('.html') ? 'text/html' : p.endsWith('.webmanifest') ? 'application/json' : 'text/javascript'
            })); }
        };
    }
    const caches = {
        open: async name => store(name),
        keys: async () => [...stores.keys()],
        delete: async name => stores.delete(name),
        match: async req => {
            for (const entries of stores.values()) if (entries.has(key(req))) return entries.get(key(req));
            return undefined;
        }
    };
    const fetch = (req, opts) => {
        network.push({ req, opts });
        if (!queued.length) return Promise.reject(new Error('No network response queued'));
        const item = queued.shift();
        return item instanceof Error ? Promise.reject(item) : Promise.resolve(item);
    };
    const self = {
        location: { origin: ORIGIN },
        addEventListener: (name, fn) => { handlers[name] = fn; },
        skipWaiting: () => { skipWaiting++; },
        clients: { claim: () => { claim++; } }
    };
    vm.runInNewContext(source, { self, caches, fetch, URL, Promise, Set, console });
    function dispatch(name, values) {
        const waiters = [];
        let handled;
        const event = {
            ...values,
            waitUntil: promise => waiters.push(Promise.resolve(promise)),
            respondWith: promise => { handled = Promise.resolve(promise); }
        };
        handlers[name](event);
        return { handled, wait: () => Promise.all(waiters) };
    }
    async function doFetch(req) {
        const event = dispatch('fetch', { request: req });
        return event.handled ? event.handled : undefined;
    }
    async function clear() { await dispatch('message', { data: { type: 'EDUOS_CLEAR_RUNTIME_STATIC' } }).wait(); }
    async function message(data) { await dispatch('message', { data }).wait(); }
    return {
        caches, store, stores, queued, network, doFetch, clear, message,
        setPutBarrier: value => { putBarrier = value; },
        install: () => dispatch('install', {}).wait(),
        activate: () => dispatch('activate', {}).wait(),
        get skipWaiting() { return skipWaiting; },
        get claim() { return claim; }
    };
}

test('install and activate retain only the current public shell cache', async () => {
    const w = worker();
    await w.store('eduos-static-v9').put(request('/js/legacy.js'), response('legacy'));
    await w.store('unrelated-app-cache').put(request('/js/other.js'), response('other'));
    await w.install();
    await w.activate();
    assert.deepEqual([...w.stores.keys()].sort(), ['eduos-static-v11', 'unrelated-app-cache']);
    assert.equal(w.skipWaiting, 1);
    assert.equal(w.claim, 1);
    assert.ok(await w.store('eduos-static-v11').match(request('/offline.html')));
});

test('private response from unrelated cache is never returned for a public-looking URL', async () => {
    const w = worker();
    const url = '/images/student.jpg';
    await w.store('unrelated-app-cache').put(request(url), response('TENANT_A_PRIVATE', { contentType: 'image/jpeg' }));
    w.queued.push(response('PUBLIC_IMAGE', { contentType: 'image/jpeg' }));
    const got = await w.doFetch(request(url));
    assert.equal(got.body, 'PUBLIC_IMAGE');
    assert.equal(w.network.length, 1);
    assert.equal((await w.store('eduos-static-v11').match(request(url))).body, 'PUBLIC_IMAGE');
});

test('poisoned private cached response is evicted before the next institution session reads it', async () => {
    const w = worker();
    const url = '/images/student.jpg';
    await w.store('eduos-static-v11').put(request(url), response('TENANT_A_PRIVATE', {
        contentType: 'image/jpeg', headers: { 'Cache-Control': 'private, no-store' }
    }));
    w.queued.push(response('TENANT_B_PUBLIC', { contentType: 'image/jpeg' }));
    assert.equal((await w.doFetch(request(url))).body, 'TENANT_B_PUBLIC');
    assert.equal((await w.store('eduos-static-v11').match(request(url))).body, 'TENANT_B_PUBLIC');
});

test('public static cache hit is reused but tenant switch clears runtime assets and retains shell', async () => {
    const w = worker();
    await w.install();
    const req = request('/images/public-banner.jpg');
    w.queued.push(response('public-v1', { contentType: 'image/jpeg' }));
    assert.equal((await w.doFetch(req)).body, 'public-v1');
    assert.equal((await w.doFetch(req)).body, 'public-v1');
    assert.equal(w.network.length, 1);
    await w.clear();
    assert.equal(await w.store('eduos-static-v11').match(req), undefined);
    assert.ok(await w.store('eduos-static-v11').match(request('/offline.html')));
    w.queued.push(response('public-v2', { contentType: 'image/jpeg' }));
    assert.equal((await w.doFetch(req)).body, 'public-v2');
    assert.equal(w.network.length, 2);
});

test('old-session in-flight static fetch cannot repopulate cache after switch', async () => {
    const w = worker();
    const waiting = deferred();
    const req = request('/images/campus-banner.jpg');
    w.queued.push(waiting.promise);
    const pending = w.doFetch(req);
    await w.clear();
    waiting.resolve(response('OLD_CAMPUS', { contentType: 'image/jpeg' }));
    assert.equal((await pending).body, 'OLD_CAMPUS');
    assert.equal(await w.store('eduos-static-v11').match(req), undefined);
    w.queued.push(response('NEW_CAMPUS', { contentType: 'image/jpeg' }));
    assert.equal((await w.doFetch(req)).body, 'NEW_CAMPUS');
});

test('private, vary-cookie, authorization-vary, wildcard-vary and redirect responses are not cached', async () => {
    for (const options of [
        { headers: { 'Cache-Control': 'private' } },
        { headers: { 'Cache-Control': 'no-store' } },
        { headers: { Vary: 'Accept, Cookie' } },
        { headers: { Vary: 'Authorization' } },
        { headers: { Vary: '*' } },
        { redirected: true },
        { type: 'opaque' },
        { contentType: 'text/html' }
    ]) {
        const w = worker();
        const req = request('/js/runtime.js');
        w.queued.push(response('sensitive', options));
        assert.equal((await w.doFetch(req)).body, 'sensitive');
        assert.equal(await w.store('eduos-static-v11').match(req), undefined);
    }
});

test('query-string tenant and signed-file tokens bypass persistent cache', async () => {
    const w = worker();
    for (const url of ['/images/logo.png?tenant=123', '/js/site.js?session=SECRET', '/images/file.jpg?signature=SECRET']) {
        assert.equal(await w.doFetch(request(url)), undefined);
    }
    assert.equal(w.network.length, 0);
    assert.equal(w.stores.size, 0);
});

test('API, private file, non-static extension, cross-origin and POST never enter cache interception', async () => {
    const w = worker();
    for (const req of [request('/api/lms/files/123'), request('/uploads/private.jpg'),
        request('/images/file.pdf'), request('https://another.example/images/1.jpg'),
        request('/images/public.jpg', { method: 'POST' }),
        request('/images/public.jpg', { headers: { Authorization: 'Bearer secret' } }),
        request('/images/public.jpg', { cache: 'no-store' }),
        request('/images/public.jpg', { cache: 'reload' })]) {
        assert.equal(await w.doFetch(req), undefined);
    }
    assert.equal(w.network.length, 0);
    assert.equal(w.stores.size, 0);
});

test('authenticated navigation is network-only and offline fallback contains no private data', async () => {
    const w = worker();
    await w.install();
    w.queued.push(response('TENANT_A_HTML', { contentType: 'text/html' }));
    const page = request('/HostelOperations/Index', { mode: 'navigate' });
    assert.equal((await w.doFetch(page)).body, 'TENANT_A_HTML');
    assert.equal(w.network[0].opts.cache, 'no-store');
    assert.equal(await w.store('eduos-static-v11').match(page), undefined);
    w.queued.push(new Error('offline'));
    assert.equal((await w.doFetch(page)).body, 'public:/offline.html');
});

test('offline fallback ignores a private offline page in an unrelated cache', async () => {
    const w = worker();
    await w.store('unrelated-app-cache').put(request('/offline.html'), response('TENANT_A_PRIVATE', { contentType: 'text/html' }));
    await w.install();
    w.queued.push(new Error('offline'));
    const got = await w.doFetch(request('/TransportOperations/Index', { mode: 'navigate' }));
    assert.equal(got.body, 'public:/offline.html');
});

test('clear waits for an already-started cache write and deletes its old-session bytes', async () => {
    const w = worker();
    const req = request('/images/campus-banner.jpg');
    const put = deferred();
    w.setPutBarrier(put);
    w.queued.push(response('OLD_CAMPUS', { contentType: 'image/jpeg' }));
    const oldFetch = w.doFetch(req);
    // Wait for cache.open and network resolution to reach the pending put.
    await new Promise(resolve => setImmediate(resolve));
    let cleared = false;
    const clear = w.clear().then(() => { cleared = true; });
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(cleared, false);
    put.resolve();
    await oldFetch;
    await clear;
    assert.equal(cleared, true);
    assert.equal(await w.store('eduos-static-v11').match(req), undefined);
});

test('unrelated service-worker messages do not clear public runtime assets', async () => {
    const w = worker();
    const req = request('/js/runtime.js');
    w.queued.push(response('public'));
    await w.doFetch(req);
    await w.message({ type: 'ANOTHER_MESSAGE' });
    assert.equal((await w.store('eduos-static-v11').match(req)).body, 'public');
});
