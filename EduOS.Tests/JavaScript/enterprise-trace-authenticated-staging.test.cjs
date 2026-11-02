'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { validateEnvironment, makeAuthenticatedFetcher } =
    require('../../tests/browser/enterprise-trace-live.cjs');
const env = {
    EDUOS_E2E_BASE_URL: 'https://qa.eduos.example',
    EDUOS_E2E_STAGING_HOST: 'qa.eduos.example',
    EDUOS_E2E_ISOLATED_STAGING_ACK: 'I_CONFIRM_DISPOSABLE_SYNTHETIC',
    EDUOS_E2E_A_USER: 'owner@example.invalid',
    EDUOS_E2E_A_PASSWORD: 'synthetic-owner',
    EDUOS_E2E_A_TENANT_ID: '11',
    EDUOS_E2E_B_USER: 'foreign@example.invalid',
    EDUOS_E2E_B_PASSWORD: 'synthetic-foreign',
    EDUOS_E2E_B_TENANT_ID: '22',
    EDUOS_E2E_RESTRICTED_USER: 'viewer@example.invalid',
    EDUOS_E2E_RESTRICTED_PASSWORD: 'synthetic-viewer',
    EDUOS_E2E_RESTRICTED_TENANT_ID: '11',
    EDUOS_E2E_STOCK_REFERENCE: '11111111-1111-4111-8111-111111111111',
    EDUOS_E2E_ASSET_REFERENCE: '22222222-2222-4222-8222-222222222222',
    EDUOS_E2E_PROCUREMENT_REFERENCE: '33333333-3333-4333-8333-333333333333',
    EDUOS_E2E_SUPPLIER_CONFIRMATION_ID: '7',
    EDUOS_E2E_FROM: '2026-10-01',
    EDUOS_E2E_TO: '2026-10-31',
    EDUOS_E2E_ASSET_CAMPUS_ID: '10',
    EDUOS_E2E_OTHER_CAMPUS_ID: '20',
    EDUOS_E2E_A_LANGUAGE: 'en',
    EDUOS_E2E_B_LANGUAGE: 'bn'
};
test('staging browser refuses production, shared accounts, unsafe dates and incomplete scope', () => {
    assert.equal(validateEnvironment(env).users.B.tenant, '22');
    for (const change of [
        { EDUOS_E2E_BASE_URL: 'https://production.eduos.example',
            EDUOS_E2E_STAGING_HOST: 'production.eduos.example' },
        { EDUOS_E2E_BASE_URL: 'http://qa.eduos.example' },
        { EDUOS_E2E_BASE_URL: 'https://qa.eduos.example/evil' },
        { EDUOS_E2E_BASE_URL: 'https://admin:password@qa.eduos.example' },
        { EDUOS_E2E_ISOLATED_STAGING_ACK: '' },
        { EDUOS_E2E_B_TENANT_ID: '11' },
        { EDUOS_E2E_RESTRICTED_TENANT_ID: '22' },
        { EDUOS_E2E_B_USER: 'owner@example.invalid' },
        { EDUOS_E2E_ASSET_REFERENCE: env.EDUOS_E2E_STOCK_REFERENCE },
        { EDUOS_E2E_SUPPLIER_CONFIRMATION_ID: '0' },
        { EDUOS_E2E_FROM: '2026-02-30' },
        { EDUOS_E2E_OTHER_CAMPUS_ID: '10' },
        { EDUOS_E2E_B_LANGUAGE: 'en' }
    ]) assert.throws(() => validateEnvironment({ ...env, ...change }));
});
test('browser cookie jar is used without transmitting placeholder session labels', async () => {
    let received;
    const fake = { request: { get: async (url, options) => {
        received = { url, options };
        return { status: () => 200,
            headers: () => ({ 'content-type': 'application/json',
                'cache-control': 'private, no-store' }),
            body: async () => Buffer.from('{"success":true}') };
    } } };
    const fetch = makeAuthenticatedFetcher('https://qa.eduos.example', { owner: fake });
    const response = await fetch('https://qa.eduos.example/api/v3/enterprise/ledger-trace/register',
        { method: 'GET', headers: { Cookie: 'owner' } });
    assert.equal(response.status, 200);
    assert.equal(await response.text(), '{"success":true}');
    assert.equal(received.options.headers.Cookie, undefined);
    assert.equal(received.options.maxRedirects, 0);
    await assert.rejects(fetch('https://evil.example/api/v3/enterprise/ledger-trace/register',
        { method: 'GET', headers: { Cookie: 'owner' } }));
    await assert.rejects(fetch('https://qa.eduos.example/api/v3/enterprise/ledger-trace/register',
        { method: 'POST', headers: { Cookie: 'owner' } }));
    await assert.rejects(fetch('https://qa.eduos.example/api/v3/enterprise/ledger-trace/register',
        { method: 'GET', headers: { Cookie: 'unrecognized' } }));
});
test('authenticated evidence response is bounded', async () => {
    const fake = { request: { get: async () => ({
        status: () => 200, headers: () => ({}),
        body: async () => Buffer.alloc(2_000_001)
    }) } };
    const fetch = makeAuthenticatedFetcher('https://qa.eduos.example', { owner: fake });
    await assert.rejects(fetch('https://qa.eduos.example/api/v3/enterprise/ledger-trace/register',
        { method: 'GET', headers: { Cookie: 'owner' } }), /Oversized/);
});
