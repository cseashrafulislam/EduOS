'use strict';
// Keep authenticated staging/production smoke strictly read-only. This module
// has no credentials and is unit-testable without a browser or network.
const assert = require('node:assert/strict');

function validateTarget(rawBase, rawExpectedHost) {
    const base = String(rawBase || '').trim().replace(/\/$/, '');
    const expectedHost = String(rawExpectedHost || '').trim().toLowerCase();
    assert(base && expectedHost, 'Explicit target origin and approved hostname required');
    assert(/^[a-z0-9.-]+$/.test(expectedHost), 'Invalid approved hostname');
    let url;
    try { url = new URL(base); } catch { throw Error('Invalid authenticated smoke origin'); }
    const loopback = ['localhost', '127.0.0.1'].includes(url.hostname);
    assert((url.protocol === 'https:' || (loopback && url.protocol === 'http:')) &&
        url.origin === base && url.hostname.toLowerCase() === expectedHost &&
        !url.username && !url.password && !url.search && !url.hash &&
        (url.pathname === '/' || url.pathname === ''),
        'Refusing non-canonical or unapproved authenticated smoke origin');
    return url.origin;
}

function isAllowedReadOnlyRequest(rawUrl, method, origin) {
    try {
        const url = new URL(rawUrl);
        if (url.origin !== origin || url.username || url.password || url.hash) return false;
        const verb = String(method).toUpperCase();
        if (['GET', 'HEAD', 'OPTIONS'].includes(verb)) return true;
        return verb === 'POST' && url.pathname.toLowerCase() === '/api/auth/login' &&
            !url.search && rawUrl.startsWith(origin + '/');
    } catch { return false; }
}
module.exports = { validateTarget, isAllowedReadOnlyRequest };
