const CACHE_PREFIX = 'eduos-static-';
const CACHE_NAME = `${CACHE_PREFIX}v11`;
const OFFLINE_URL = '/offline.html';
const CORE_ASSETS = [
    OFFLINE_URL,
    '/manifest.webmanifest',
    '/css/site.css',
    '/js/site.js',
    '/images/eduos-icon.svg',
    '/images/eduos-icons.svg',
    '/lib/bootstrap/dist/css/bootstrap.min.css',
    '/lib/bootstrap/dist/js/bootstrap.bundle.min.js',
    '/lib/jquery/dist/jquery.min.js'
];

// Public runtime assets are an optimization, never a source of user/session data.
// A tenant switch invalidates any fetch started under the old browser session.
let cacheGeneration = 0;
const pendingWrites = new Set();

self.addEventListener('install', event => {
    event.waitUntil(
        caches.open(CACHE_NAME)
            .then(cache => cache.addAll(CORE_ASSETS))
            .then(() => self.skipWaiting())
    );
});

self.addEventListener('activate', event => {
    event.waitUntil(
        caches.keys()
            .then(keys => Promise.all(
                keys
                    .filter(key => key.startsWith(CACHE_PREFIX) && key !== CACHE_NAME)
                    .map(key => caches.delete(key))
            ))
            .then(() => self.clients.claim())
    );
});

function isCacheableStaticPath(pathname) {
    if (pathname === OFFLINE_URL || pathname === '/manifest.webmanifest') return true;
    if (!['/css/', '/js/', '/lib/', '/images/'].some(prefix => pathname.startsWith(prefix)))
        return false;
    // A route under a static prefix is not necessarily a public static file.
    return /\.(?:css|js|svg|png|jpe?g|webp|ico|woff2?)$/i.test(pathname);
}

function isPublicStaticResponse(request, response) {
    if (!response.ok || response.type !== 'basic' || response.redirected) return false;
    if (request.headers.has('Authorization') || request.cache === 'no-store') return false;
    const cacheControl = response.headers.get('Cache-Control') || '';
    if (/\b(?:private|no-store|no-cache)\b/i.test(cacheControl)) return false;
    const vary = response.headers.get('Vary') || '';
    if (/(?:^|,)\s*(?:cookie|authorization|\*)\s*(?:,|$)/i.test(vary)) return false;
    const contentType = (response.headers.get('Content-Type') || '').toLowerCase();
    const path = new URL(request.url).pathname;
    if (path.endsWith('.css')) return contentType.includes('text/css');
    if (path.endsWith('.js')) return contentType.includes('javascript');
    if (path.endsWith('.svg')) return contentType.includes('image/svg+xml');
    if (path.endsWith('.webmanifest')) return contentType.includes('json');
    if (path.endsWith('.html')) return contentType.includes('text/html');
    if (/\.(?:png|jpe?g|webp|ico)$/i.test(path)) return contentType.startsWith('image/');
    if (/\.woff2?$/i.test(path)) return contentType.includes('font/') || contentType.includes('application/font');
    return false;
}

// Logout/institution switch cannot allow a delayed previous-session fetch to
// repopulate runtime caches after they have been cleared.
self.addEventListener('message', event => {
    if (event.data?.type !== 'EDUOS_CLEAR_RUNTIME_STATIC') return;
    ++cacheGeneration;
    event.waitUntil((async () => {
        await Promise.allSettled([...pendingWrites]);
        const cache = await caches.open(CACHE_NAME);
        const requests = await cache.keys();
        await Promise.all(requests
            .filter(request => !CORE_ASSETS.includes(new URL(request.url).pathname))
            .map(request => cache.delete(request)));
    })());
});

self.addEventListener('fetch', event => {
    const request = event.request;
    if (request.method !== 'GET') return;
    // A personalized/explicit-refresh request must never receive an earlier public cache hit.
    if (request.headers.has('Authorization') || request.cache === 'no-store' || request.cache === 'reload') return;

    const url = new URL(request.url);
    if (url.origin !== self.location.origin) return;

    // Never cache API responses or authenticated HTML. This prevents tenant and
    // personal data from being served to another user of a shared device.
    if (request.mode === 'navigate') {
        event.respondWith(
            fetch(request, { cache: 'no-store' })
                .catch(() => caches.open(CACHE_NAME).then(cache => cache.match(OFFLINE_URL)))
        );
        return;
    }

    // Query strings can carry institution, session or signed-file identifiers.
    // They must never become persistent CacheStorage keys, even under /js or /images.
    if (url.search || !isCacheableStaticPath(url.pathname)) return;

    const generation = cacheGeneration;
    event.respondWith((async () => {
        // Search only our own public cache. caches.match() searches unrelated caches,
        // which could contain a private response under the same URL.
        const cache = await caches.open(CACHE_NAME);
        const cached = await cache.match(request);
        if (cached && isPublicStaticResponse(request, cached)) return cached;
        if (cached) await cache.delete(request);

        const response = await fetch(request);
        if (!isPublicStaticResponse(request, response) || generation !== cacheGeneration)
            return response;

        const write = cache.put(request, response.clone());
        pendingWrites.add(write);
        try { await write; }
        finally { pendingWrites.delete(write); }
        return response;
    })());
});
