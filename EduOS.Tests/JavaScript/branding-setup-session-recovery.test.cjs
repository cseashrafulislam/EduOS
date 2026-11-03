const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.resolve(__dirname,
    '../../EduOS.App/wwwroot/js/account/branding-setup.js'), 'utf8');
const view = fs.readFileSync(path.resolve(__dirname,
    '../../EduOS.App/Views/Account/BrandingSetup.cshtml'), 'utf8');
const en = fs.readFileSync(path.resolve(__dirname,
    '../../EduOS.App/Resources/Localization/SharedResource.resx'), 'utf8');
const bn = fs.readFileSync(path.resolve(__dirname,
    '../../EduOS.App/Resources/Localization/SharedResource.bn-BD.resx'), 'utf8');

function element() {
    const listeners = {};
    return {
        listeners, value: '', disabled: false, hidden: false, textContent: '',
        className: '', dataset: {}, children: [],
        addEventListener(type, callback) { listeners[type] = callback; },
        replaceChildren(...children) { this.children = children; },
        setAttribute() {}, focus() {}, checkValidity() { return true; }
    };
}

async function mount({ onboardingOk = true, sessionMatches = true, token = 'csrf-token',
    failColorAt = 0, tenant = '1', switchDuringRead = false,
    verificationAvailable = true, missingMatcher = false } = {}) {
    const ids = ['alertContainer', 'subdomainInput', 'availabilityMsg',
        'saveSubdomainBtn', 'saveBrandingBtn', 'retryBrandingLoadBtn',
        'brandingForm', 'logoPreview', 'faviconPreview', 'logoInput',
        'faviconInput', 'uploadLogoBtn', 'uploadFaviconBtn',
        'removeLogoBtn', 'removeFaviconBtn',
        'primaryColor', 'primaryColorText', 'secondaryColor', 'secondaryColorText',
        'accentColor', 'accentColorText'];
    const nodes = Object.fromEntries(ids.map(id => [id, element()]));
    nodes.saveBrandingBtn.dataset.idleLabel = 'Save and Continue';
    nodes.brandingSetupStrings = element();
    nodes.brandingSetupStrings.textContent = JSON.stringify({
        loading: 'Loading', loadFailed: 'Could not load branding',
        partialSaveRecovery: 'Some colors may have saved; review and retry',
        saveFailed: 'Save failed', subdomainSaved: 'Saved',
        saveAndContinue: 'Save and Continue', saving: 'Saving',
        dashboard: 'Dashboard', continueSetup: 'Continue',
        logoAlt: 'Logo', faviconAlt: 'Favicon'
    });
    const callbacks = {}, lifecycle = {};
    let reloads = 0;
    const calls = [], navigations = [], savedRows = [];
    const body = { dataset: { authTenantId: tenant, authSessionReference: 'session-1' } };
    let verificationUp = verificationAvailable;
    let colorWrites = 0, sessionChecks = 0;
    let onboardingAvailable = onboardingOk;
    const document = {
        body,
        getElementById: id => nodes[id] || null,
        querySelector: selector => selector.includes('__RequestVerificationToken')
            ? (token ? { value: token } : null) : null,
        addEventListener: (name, callback) => { callbacks[name] = callback; },
        createElement: () => element()
    };
    const response = (data, ok = true, status = ok ? 200 : 409, headers = {}) => ({
        ok, status, headers: { get: key => headers[key] || null },
        json: async () => data
    });
    const fetch = async (url, options = {}) => {
        calls.push({ url, method: options.method || 'GET', headers: options.headers || {} });
        if (url === '/api/tenant-profile') {
            if (switchDuringRead) body.dataset.authTenantId = 'other';
            return response({ success: true, data: {
                subdomain: 'demo-school', rowVersion: 'rv1', logoUrl: null, faviconUrl: null
            } });
        }
        if (url === '/api/tenant-settings?category=Branding&page=1&pageSize=50')
            return response({ success: true, data: { items: savedRows.map(x => ({ ...x })) } });
        if (url === '/api/onboarding/status')
            return response(onboardingAvailable
                ? { success: true, data: { currentStage: 8, isComplete: false } }
                : { success: false }, onboardingAvailable, onboardingAvailable ? 200 : 500);
        if (url === '/api/workspace-preferences')
            return response({ success: verificationUp, data: {} }, verificationUp,
                verificationUp ? 200 : 503,
                { 'X-EduOS-Session-Reference': sessionMatches ? 'current' : 'other' });
        if (url === '/api/tenant-settings' && options.method === 'POST') {
            colorWrites++;
            if (colorWrites === failColorAt)
                return response({ success: false, message: 'Concurrent edit' }, false);
            const body = JSON.parse(options.body);
            const row = { ...body, id: colorWrites, rowVersion: 'version-' + colorWrites };
            savedRows.push(row);
            return response({ success: true, data: row });
        }
        if (url === '/api/onboarding/complete-step' && options.method === 'POST')
            return response({ success: true, data: {} });
        throw new Error('Unexpected request: ' + url);
    };
    const window = {
        location: { origin: 'https://school.example', assign: url => navigations.push(url), reload: () => { reloads++; } },
        addEventListener: (name, callback) => { lifecycle[name] = callback; },
        clearTimeout() {}, setTimeout() { return 1; }, confirm() { return true; },
        EduOSWorkspaceResponseMatchesSession: missingMatcher ? undefined : result =>
            result.headers.get('X-EduOS-Session-Reference') === 'current',
        EduOSVerifySessionContext() { sessionChecks++; },
        EduOSSessionTransitionPending() { return false; },
        eduosOnboardingStageUrl: () => '/Account/BrandingSetup'
    };
    vm.runInNewContext(source, { document, window, fetch, URL, FormData, console });
    await callbacks.DOMContentLoaded();
    return {
        nodes, calls, navigations, savedRows,
        lifecycle,
        get reloads() { return reloads; },
        setOnboardingOk(value) { onboardingAvailable = value; },
        setVerificationUp(value) { verificationUp = value; },
        get colorWrites() { return colorWrites; },
        get sessionChecks() { return sessionChecks; },
        async submit() {
            await nodes.brandingForm.listeners.submit({ preventDefault() {} });
        },
        async retry() { await nodes.retryBrandingLoadBtn.listeners.click(); }
    };
}

test('Branding editor requires verified profile, colors, onboarding and session before writes', async () => {
    const app = await mount();
    assert.equal(app.nodes.saveBrandingBtn.disabled, false);
    assert.equal(app.nodes.subdomainInput.value, 'demo-school');
    assert.equal(app.nodes.retryBrandingLoadBtn.hidden, true);
    assert.equal(app.calls.filter(x => x.url === '/api/workspace-preferences').length, 2);
    await app.submit();
    assert.equal(app.colorWrites, 3);
    assert.deepEqual(app.navigations, ['/Account/GeneralSettings']);
    const writes = app.calls.filter(x => x.method !== 'GET');
    assert.equal(writes.length, 4);
    for (const call of writes)
        assert.equal(call.headers.RequestVerificationToken, 'csrf-token');
});

test('Failed onboarding read blocks all writes and offers a retry', async () => {
    const app = await mount({ onboardingOk: false });
    assert.equal(app.nodes.saveBrandingBtn.disabled, true);
    assert.equal(app.nodes.uploadLogoBtn.disabled, true);
    assert.equal(app.nodes.retryBrandingLoadBtn.hidden, false);
    assert.equal(app.nodes.subdomainInput.value, '');
    await app.submit();
    assert.equal(app.colorWrites, 0);
    assert.deepEqual(app.navigations, []);
    app.setOnboardingOk(true);
    await app.retry();
    assert.equal(app.nodes.saveBrandingBtn.disabled, false);
    assert.equal(app.nodes.retryBrandingLoadBtn.hidden, true);
});

test('A switched tenant session cannot display profile or issue branding writes', async () => {
    const app = await mount({ sessionMatches: false });
    assert.equal(app.sessionChecks, 1);
    assert.equal(app.nodes.saveBrandingBtn.disabled, true);
    assert.equal(app.nodes.retryBrandingLoadBtn.hidden, true);
    assert.equal(app.nodes.subdomainInput.value, '');
    await app.submit();
    assert.equal(app.colorWrites, 0);
});

test('Interrupted sequential color save never advances onboarding and reconciles saved values', async () => {
    const app = await mount({ failColorAt: 2 });
    app.nodes.primaryColorText.value = '#123456';
    app.nodes.secondaryColorText.value = '#234567';
    app.nodes.accentColorText.value = '#345678';
    await app.submit();
    assert.equal(app.colorWrites, 2);
    assert.equal(app.savedRows.length, 1);
    assert.equal(app.nodes.primaryColorText.value, '#123456');
    assert.equal(app.nodes.secondaryColorText.value, '#64748B');
    assert.equal(app.nodes.accentColorText.value, '#F59E0B');
    assert.match(app.nodes.alertContainer.textContent, /Some colors may have saved/);
    assert.equal(app.nodes.saveBrandingBtn.disabled, false);
    assert.equal(app.calls.filter(x => x.url === '/api/onboarding/complete-step').length, 0);
    assert.deepEqual(app.navigations, []);
});

test('Missing antiforgery token blocks all mutation requests', async () => {
    const app = await mount({ token: '' });
    await app.submit();
    assert.equal(app.colorWrites, 0);
    assert.deepEqual(app.navigations, []);
});

test('Shared antiforgery and localized partial-save recovery remain mandatory', async () => {
    assert.match(view, /id="retryBrandingLoadBtn"[^>]*hidden>@T\["Retry"\]/);
    assert.match(view, /partialSaveRecovery = T\["BrandingPartialSaveRecovery"\]/);
    assert.match(view, /id="saveBrandingBtn"[^>]*disabled/);
    assert.match(en, /name="BrandingPartialSaveRecovery"/);
    assert.match(bn, /name="BrandingPartialSaveRecovery"/);
    assert.match(source, /await verifySession\(\)/);
    assert.match(source, /window\.EduOSWorkspaceResponseMatchesSession/);
    assert.match(source, /await reloadBranding\(\)/);
    assert.doesNotMatch(source, /innerHTML|localStorage|sessionStorage/);
});

test('Branding setup verifies before privileged reads and blocks missing context/matcher', async () => {
    for (const options of [{ tenant: '' }, { missingMatcher: true }]) {
        const app = await mount(options);
        assert.equal(app.nodes.saveBrandingBtn.disabled, true);
        assert.equal(app.nodes.retryBrandingLoadBtn.hidden, true);
        assert.equal(app.calls.some(x => x.url === '/api/tenant-profile'), false);
        await app.submit();
        assert.equal(app.colorWrites, 0);
    }
});

test('Branding setup discards profile loaded after a tenant switch', async () => {
    const app = await mount({ switchDuringRead: true });
    assert.equal(app.nodes.saveBrandingBtn.disabled, true);
    assert.equal(app.nodes.subdomainInput.value, '');
    assert.equal(app.nodes.retryBrandingLoadBtn.hidden, true);
    await app.submit();
    assert.equal(app.colorWrites, 0);
});

test('Unavailable session verifier clears privileged UI and only retries after recovery', async () => {
    const app = await mount({ verificationAvailable: false });
    assert.equal(app.nodes.saveBrandingBtn.disabled, true);
    assert.equal(app.nodes.retryBrandingLoadBtn.hidden, false);
    assert.equal(app.calls.some(x => x.url === '/api/tenant-profile'), false);
    app.setVerificationUp(true);
    await app.retry();
    assert.equal(app.nodes.saveBrandingBtn.disabled, false);
});

test('Branding onboarding clears private values on pagehide and forces a fresh authenticated document on BFCache return', async () => {
    const app = await mount();
    assert.equal(app.nodes.subdomainInput.value, 'demo-school');
    assert.equal(app.nodes.saveBrandingBtn.disabled, false);
    app.lifecycle.pagehide();
    assert.equal(app.nodes.subdomainInput.value, '');
    assert.equal(app.nodes.primaryColorText.value, '#1E40AF');
    assert.equal(app.nodes.saveBrandingBtn.disabled, true);
    assert.equal(app.nodes.uploadLogoBtn.disabled, true);
    assert.equal(app.nodes.retryBrandingLoadBtn.hidden, true);
    assert.equal(app.nodes.alertContainer.textContent, '');
    await app.submit();
    assert.equal(app.colorWrites, 0);
    assert.deepEqual(app.navigations, []);
    app.lifecycle.pageshow({ persisted: true });
    assert.equal(app.reloads, 1);
    app.lifecycle.pageshow({ persisted: false });
    assert.equal(app.reloads, 1);
});

test('TenantAdmin onboarding pages disable HTTP caching and branding enforces lifecycle privacy', () => {
    const controller = fs.readFileSync(path.resolve(__dirname,
        '../../EduOS.App/Controllers/AccountController.cs'), 'utf8');
    for (const action of ['InstitutionProfile', 'CampusSetup', 'AcademicSetup',
        'ModuleSetup', 'BrandingSetup', 'GeneralSettings', 'GatewaySetup']) {
        const signature = new RegExp('\\[ResponseCache\\(NoStore = true, Location = ResponseCacheLocation\\.None\\)\\]\\s+public IActionResult ' + action + '\\(\\)');
        assert.match(controller, signature, action + ' must not be shared-cached');
    }
    assert.match(source, /window\.addEventListener\('pagehide'/);
    assert.match(source, /event\.persisted\) window\.location\.reload\(\)/);
    assert.match(source, /return pageActive && !!originalTenant/);
});
