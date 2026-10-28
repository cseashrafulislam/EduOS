const CACHE_PREFIX = 'eduos-static-';
const CACHE_NAME = `${CACHE_PREFIX}v10`;
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
    if (/(?:^|,)\s*(?:cookie|authorization)\s*(?:,|$)/i.test(vary)) return false;
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

// Logout discards runtime assets without deleting the public offline shell.
self.addEventListener('message', event => {
    if (event.data?.type !== 'EDUOS_CLEAR_RUNTIME_STATIC') return;
    event.waitUntil(caches.open(CACHE_NAME).then(cache =>
        cache.keys().then(requests => Promise.all(requests
            .filter(request => !CORE_ASSETS.includes(new URL(request.url).pathname))
            .map(request => cache.delete(request))))));
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
                .catch(() => caches.match(OFFLINE_URL))
        );
        return;
    }

    if (!isCacheableStaticPath(url.pathname)) return;

    event.respondWith(
        caches.match(request).then(cached => {
            if (cached) return cached;

            return fetch(request).then(response => {
                if (!isPublicStaticResponse(request, response)) return response;

                const copy = response.clone();
                caches.open(CACHE_NAME).then(cache => cache.put(request, copy));
                return response;
            });
        })
    );
});
