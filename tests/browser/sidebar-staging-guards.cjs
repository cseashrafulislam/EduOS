'use strict';
// Pure, fail-closed response/request checks for disposable authenticated staging.
const assert = require('node:assert/strict');

function isAllowedRequest(url, method, origin) {
    try {
        const target = new URL(url);
        if (target.origin !== origin) return false;
        const verb = String(method).toUpperCase();
        return ['GET', 'HEAD', 'OPTIONS'].includes(verb) ||
            (verb === 'POST' && target.pathname.toLowerCase() === '/api/auth/login' &&
                !target.search && !target.hash && url.startsWith(origin + '/'));
    } catch { return false; }
}
// Filter presentation-only group toggles, then require canonical, same-origin
// menu destinations. Never follow foreign URL or access a logout endpoint.
function canonicalMenuPaths(links, origin) {
    assert(Array.isArray(links), 'Sidebar navigation inventory must be an array');
    const paths = new Set();
    for (const raw of links) {
        if (raw === null || raw === '#' || (typeof raw === 'string' && raw.startsWith('#')))
            continue;
        assert(typeof raw === 'string' && raw.startsWith('/'),
            'Sidebar route must be a relative same-origin path');
        const url = new URL(raw, origin);
        assert(url.origin === origin && !url.username && !url.password &&
            !url.hash && !/%(?:00|25|2e|2f|5c)/i.test(url.pathname) &&
            !/^\/(?:account\/logout|api\/auth\/logout)\/?$/i.test(url.pathname),
            'Unsafe or non-page sidebar route');
        paths.add(url.pathname + url.search);
    }
    assert(paths.size >= 3 && paths.size <= 250, 'Unexpected authorized sidebar route count');
    return [...paths];
}
function assertNoStore(headers, label) {
    const value = String(headers?.['cache-control'] || '');
    assert(/(?:^|,)\s*no-store(?:\s*,|\s*$)/i.test(value), label + ': no-store required');
    assert(!/(?:^|,)\s*public(?:\s*,|\s*$)/i.test(value), label + ': public cache forbidden');
}
function assertDeniedHtml(html, forbiddenTenant) {
    assert.equal(typeof html, 'string', 'Denial must have an inspectable text response');
    assert(Buffer.byteLength(html, 'utf8') <= 512 * 1024, 'Oversized denial body');
    for (const id of ['feeAgingApp', 'brandingForm', 'tenantUsersApp', 'profileForm']) {
        assert(!new RegExp("\\bid\\s*=\\s*['\"]" + id + "['\"]", 'i').test(html),
            'Restricted application DOM exposed: ' + id);
    }
    if (forbiddenTenant) {
        assert(/^[1-9]\d*$/.test(forbiddenTenant), 'Invalid forbidden tenant marker');
        assert(!new RegExp("\\bdata-auth-tenant-id\\s*=\\s*['\"]" + forbiddenTenant + "['\"]", 'i').test(html),
            'Foreign tenant scope exposed');
    }
}
module.exports = { isAllowedRequest, assertNoStore, assertDeniedHtml, canonicalMenuPaths };
