const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.resolve(__dirname, '../../');
const script = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/account/profile.js'), 'utf8');
const auth = fs.readFileSync(path.join(root, 'EduOS.App/Controllers/Api/AuthController.cs'), 'utf8');
const begin = script.indexOf('async function apiRequest(url, options = {})');
const end = script.indexOf('// ============================================================\n// ANTI FORGERY', begin);
assert.ok(begin >= 0 && end > begin);
const requestFunction = script.slice(begin, end);
const reference = 'A'.repeat(64);

function client(serverReference, serverTenant, pending = false) {
    let verifyCount = 0;
    const context = {
        document: { body: { dataset: { authSessionReference: reference, authTenantId: '100' } } },
        window: {
            EduOSSessionTransitionPending: () => pending,
            EduOSVerifySessionContext: () => { verifyCount++; }
        },
        fetch: async (_url, options) => {
            assert.equal(options.sessionBound, undefined);
            return {
                status: 200, ok: true,
                headers: { get: key => ({
                    'content-type': 'application/json',
                    'X-EduOS-Session-Reference': serverReference,
                    'X-EduOS-Tenant-Id': serverTenant
                })[key] ?? null },
                json: async () => ({ success: true, data: [{ sessionId: 'redacted' }] })
            };
        }
    };
    return {
        request: vm.runInNewContext(requestFunction + '\napiRequest', context),
        verifications: () => verifyCount
    };
}

test('account session readbacks accept only matching cookie and institution', async () => {
    const ok = client(reference, '100');
    assert.equal((await ok.request('/api/auth/sessions', { sessionBound: true })).success, true);
    assert.equal(ok.verifications(), 0);
    for (const [sid, tenant, pending] of [
        [reference, '200', false],
        ['B'.repeat(64), '100', false],
        [null, '100', false],
        [reference, '100', true]
    ]) {
        const stale = client(sid, tenant, pending);
        await assert.rejects(stale.request('/api/auth/sessions', { sessionBound: true }),
            /Account session changed/);
        assert.equal(stale.verifications(), 1);
    }
});

test('profile, MFA status and active device list request session-bound responses', () => {
    assert.match(script, /apiRequest\('\/api\/auth\/profile', \{ cache: 'no-store', sessionBound: true \}\)/);
    assert.match(script, /apiRequest\('\/api\/auth\/mfa\/status', \{ cache: 'no-store', sessionBound: true \}\)/);
    assert.match(script, /apiRequest\('\/api\/auth\/sessions', \{ cache: 'no-store', sessionBound: true \}\)/);
    assert.match(auth, /private void BindAccountReadbackSession\(\)/);
    assert.equal((auth.match(/BindAccountReadbackSession\(\);/g) || []).length, 5);
});

test('MFA recovery-code response is never returned to a stale institution or session', async () => {
    const start = script.indexOf('async function authSecurityPost(');
    const end = script.indexOf('function initializeAuthSecurityControls()', start);
    assert.ok(start >= 0 && end > start);
    assert.match(script, /authSecurityPost\(url, \{ currentPassword, code, useRecoveryCode \}, true\)/);
    const helper = script.slice(start, end);
    async function run(serverReference, serverTenant) {
        let verificationCount = 0;
        const context = {
            document: { body: { dataset: { authSessionReference: reference, authTenantId: '100' } } },
            window: {
                EduOSSessionTransitionPending: () => false,
                EduOSVerifySessionContext: () => { verificationCount++; }
            },
            getAntiForgeryToken: () => 'test-csrf-token',
            fetch: async (_url, options) => {
                assert.equal(options.method, 'POST');
                assert.equal(options.sessionBound, undefined);
                assert.equal(options.headers['X-CSRF-TOKEN'], 'test-csrf-token');
                return {
                    status: 200, ok: true,
                    headers: { get: key => ({
                        'content-type': 'application/json',
                        'X-EduOS-Session-Reference': serverReference,
                        'X-EduOS-Tenant-Id': serverTenant
                    })[key] ?? null },
                    json: async () => ({ success: true, data: { recoveryCodes: ['one-time-private-code'] } })
                };
            }
        };
        const post = vm.runInNewContext(requestFunction + helper + '\nauthSecurityPost', context);
        try {
            const result = await post('/api/auth/mfa/recovery-codes/regenerate',
                { currentPassword: 'password', code: '123456' }, true);
            return { codes: result.data.recoveryCodes, verificationCount };
        } catch (error) {
            return { error: error.message, verificationCount };
        }
    }
    const accepted = await run(reference, '100');
    assert.equal(accepted.codes[0], 'one-time-private-code');
    assert.equal(accepted.verificationCount, 0);
    for (const [sid, tenant] of [['B'.repeat(64), '100'], [reference, '200'], [null, '100']]) {
        const denied = await run(sid, tenant);
        assert.match(denied.error, /Account session changed/);
        assert.equal(denied.verificationCount, 1);
        assert.equal(denied.codes, undefined);
    }
});
