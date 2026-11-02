'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const {isAllowedRequest, assertNoStore, assertDeniedHtml} =
    require('../../tests/browser/sidebar-staging-guards.cjs');

test('staging network guard rejects off-origin reads and all non-login writes', () => {
    const origin = 'https://disposable.example.test';
    assert(isAllowedRequest(origin + '/workspace', 'GET', origin));
    assert(isAllowedRequest(origin + '/api/auth/login', 'POST', origin));
    for (const [url, method] of [
        ['https://external.example.test/workspace', 'GET'],
        [origin + '/api/workspace-preferences', 'PUT'],
        [origin + '/api/auth/logout', 'POST'],
        [origin + '/api/finance/post', 'POST'],
        ['not-a-url', 'GET']
    ]) assert.equal(isAllowedRequest(url, method, origin), false);
});
test('private authorized responses must be explicitly non-cacheable', () => {
    assert.doesNotThrow(() => assertNoStore({'cache-control':'private, no-store, no-cache'}, 'users'));
    for (const cache of ['', 'private, max-age=0', 'public, no-store'])
        assert.throws(() => assertNoStore({'cache-control':cache}, 'users'));
});
test('denied pages cannot render privileged widgets or foreign tenant shell', () => {
    assert.doesNotThrow(() => assertDeniedHtml('<body data-auth-tenant-id="22"><h1>Denied</h1></body>', '11'));
    for (const html of [
        '<div id="feeAgingApp"></div>',
        "<form id='brandingForm'></form>",
        '<section id="tenantUsersApp"></section>',
        '<body data-auth-tenant-id="11"></body>',
        'x'.repeat(512 * 1024 + 1)
    ]) assert.throws(() => assertDeniedHtml(html, '11'));
});
test('TenantUsers page is server-authorized and explicitly no-store', () => {
    const controller = fs.readFileSync(path.join(__dirname, '../../EduOS.App/Controllers/TenantUsersController.cs'), 'utf8');
    assert.match(controller, /\[Authorize\(Roles\s*=\s*"TenantAdmin"\)\]/);
    assert.match(controller, /\[HttpGet\]\s*\[ResponseCache\(NoStore\s*=\s*true,\s*Location\s*=\s*ResponseCacheLocation\.None\)\]\s*public IActionResult Index\(\)/);
});

test('account dropdown localizes identity links and derives the visible role from issued claims', () => {
    const layout = fs.readFileSync(path.join(__dirname, '../../EduOS.App/Views/Shared/_Layout.cshtml'), 'utf8');
    assert.match(layout, /claim\.Type == System\.Security\.Claims\.ClaimTypes\.Role/);
    assert.match(layout, /<span class="role">@roleCaption<\/span>/);
    assert.doesNotMatch(layout, /"SuperAdmin"\s*:\s*T\["TenantAdmin"\]\.Value/);
    for (const [route, en, bn] of [
        ['/LoginActivity/Index', 'Sign-in activity', 'সাইন-ইন কার্যক্রম'],
        ['/LegalDocuments/My', 'My Legal Agreements', 'আমার আইনি সম্মতিপত্র']
    ]) {
        assert(layout.includes('href="'+route+'">@(currentCulture == "bn-BD" ? "'+bn+'" : "'+en+'")'),
            'Account dropdown locale mismatch: '+route);
    }
});
test('staging Chromium checks real antiforgery session binding, help and role identity without business writes', () => {
    const browser = fs.readFileSync(path.join(__dirname, '../../tests/browser/sidebar-role-staging.cjs'), 'utf8');
    for (const needle of [
        "fetch('/api/auth/antiforgery'", 'proof.payload?.data?.tenantId',
        'proof.payload?.data?.sessionReference', 'verifyContextHelp(page,name)',
        'sessions.map(x=>x.roleCaption)', 'Distinct synthetic role accounts required',
        "assertNoStore(proof.headers,'Antiforgery readback')"
    ]) assert(browser.includes(needle), 'Missing staging acceptance: '+needle);
});
