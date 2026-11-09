const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const assistant = require('../../EduOS.App/wwwroot/js/platform/page-assistant.js');

test('page assistant resolves only a unique visible label, not an arbitrary guess', () => {
    const fields = [
        { labels: ['Applicant name', 'আবেদনকারীর নাম'], id: 1 },
        { labels: ['Guardian name', 'অভিভাবকের নাম'], id: 2 },
        { labels: ['Guardian mobile *'], id: 3 }
    ];
    assert.equal(assistant.matchLabel(fields, 'guardian mobile').entry.id, 3);
    assert.equal(assistant.matchLabel(fields, 'অভিভাবকের নাম').entry.id, 2);
    assert.equal(assistant.matchLabel(fields, 'name').kind, 'ambiguous');
    assert.equal(assistant.matchLabel(fields, 'x').kind, 'invalid');
    assert.equal(assistant.matchLabel(fields, 'financial posting').kind, 'missing');
});

test('page assistant rejects ambiguous exact labels, including duplicate forms', () => {
    const entries = [{ labels: ['Mobile'] }, { labels: ['Mobile'] }];
    assert.equal(assistant.matchLabel(entries, 'mobile').kind, 'ambiguous');
    assert.equal(assistant.matchLabel(entries, 'mobile').count, 2);
    assert.equal(assistant.normalizeLabel('  মোবাইল :  '), 'মোবাইল');
});

test('page assistant never offers hidden, disabled or read-only form fields', () => {
    const make = overrides => ({
        disabled: false, readOnly: false, type: 'text', closest: () => null,
        getClientRects: () => [1], ...overrides
    });
    assert.equal(assistant.isAvailableField(make({})), true);
    assert.equal(assistant.isAvailableField(make({ disabled: true })), false);
    assert.equal(assistant.isAvailableField(make({ readOnly: true })), false);
    assert.equal(assistant.isAvailableField(make({ type: 'hidden' })), false);
    assert.equal(assistant.isAvailableField(make({ closest: () => ({ hidden: true }) })), false);
    assert.equal(assistant.isAvailableField(make({ getClientRects: () => [] })), false);
});

test('collapsing a permitted sidebar section does not remove its destination from page assistant', () => {
    const collapsed = {
        dataset: { sidebarSection: 'academics' },
        matches: selector => selector === 'nav[data-sidebar-section]',
        parentElement: { closest: () => null }
    };
    const link = ancestor => ({ closest: () => ancestor });
    assert.equal(assistant.isAvailableNavigationLink(link(collapsed)), true);
    assert.equal(assistant.isAvailableNavigationLink(link(null)), true);
    assert.equal(assistant.isAvailableNavigationLink(link({ matches: () => false })), false);
    const concealed = { ...collapsed, parentElement: { closest: () => ({ hidden: true }) } };
    assert.equal(assistant.isAvailableNavigationLink(link(concealed)), false);
    const inert = { ...collapsed, dataset: { sidebarSection: 'other' } };
    assert.equal(assistant.isAvailableNavigationLink(link(inert)), false);
});

test('on-device voice requires an already installed local language pack, never remote fallback', async () => {
    let availableCalls = 0;
    let downloadCalls = 0;
    class LocalSpeech {
        constructor() { this.processLocally = false; }
        static async available(options) {
            availableCalls++;
            assert.deepEqual(options, { langs: ['bn-BD'], processLocally: true });
            return this.status;
        }
        static async install() { downloadCalls++; }
    }
    LocalSpeech.status = 'downloadable';
    assert.equal(await assistant.createOnDeviceRecognizer({ SpeechRecognition: LocalSpeech }, 'bn-BD'), null);
    LocalSpeech.status = 'available';
    const recognition = await assistant.createOnDeviceRecognizer({ SpeechRecognition: LocalSpeech }, 'bn-BD');
    assert.equal(recognition.processLocally, true);
    assert.equal(recognition.lang, 'bn-BD');
    assert.equal(recognition.interimResults, false);
    assert.equal(recognition.continuous, false);
    assert.equal(availableCalls, 2);
    assert.equal(downloadCalls, 0);
    assert.equal(await assistant.createOnDeviceRecognizer({ SpeechRecognition: function RemoteOnly() {} }, 'bn-BD'), null);
});

test('global UI is authenticated, local-only and cannot submit or write ERP fields', () => {
    const layout = fs.readFileSync(path.resolve(__dirname, '../../EduOS.App/Views/Shared/_Layout.cshtml'), 'utf8');
    const source = fs.readFileSync(path.resolve(__dirname, '../../EduOS.App/wwwroot/js/platform/page-assistant.js'), 'utf8');
    assert.match(layout, /id="pageAssistantDialog"/);
    assert.match(layout, /js\/platform\/page-assistant\.js/);
    assert.match(layout, /@if \(User\?\.Identity\?\.IsAuthenticated == true\)/);
    assert.doesNotMatch(source, /\bfetch\s*\(|\bXMLHttpRequest\b|\.submit\s*\(|\.requestSubmit\s*\(|localStorage|sessionStorage/);
    assert.doesNotMatch(source, /(?:target|field|node)\.value\s*=/);
    const securityHeaders = fs.readFileSync(path.resolve(__dirname, '../../EduOS.App/Middleware/SecurityHeadersMiddleware.cs'), 'utf8');
    assert.match(securityHeaders, /microphone=\(\), geolocation=\(\)/);
    assert.match(securityHeaders, /on-device-speech-recognition=\(self\)/);
    assert.match(securityHeaders, /context\.User\.Identity\?\.IsAuthenticated == true/);
    assert.match(securityHeaders, /ContentType\?\.StartsWith\("text\/html"/);
    assert.match(layout, /id="pageAssistantVoice"/);
    assert.match(layout, /id="pageAssistantVoiceStatus"/);
});

function createNode() {
    const listeners = new Map();
    return {
        children: [], value: '', textContent: '', isConnected: true,
        addEventListener(name, listener) { listeners.set(name, listener); },
        click() { return listeners.get('click')?.({ preventDefault() {} }); },
        keydown(key) { listeners.get('keydown')?.({ key, preventDefault() {} }); },
        append(...items) { this.children.push(...items); },
        replaceChildren(...items) { this.children = [...items]; },
        focus() { this.focusCount = (this.focusCount || 0) + 1; },
        scrollIntoView() {},
        showModal() { this.open = true; },
        close() { this.open = false; listeners.get('close')?.(); },
        getAttribute(name) { return this.attributes?.[name] || null; },
        setAttribute(name, value) { this.attributes ??= {}; this.attributes[name] = value; },
        closest() { return null; },
        getClientRects() { return [1]; }
    };
}

test('global locator focuses only after explicit review; session switch cancels pending work', () => {
    const ids = Object.fromEntries(['pageAssistantDialog', 'pageAssistantOpen', 'pageAssistantClose',
        'pageAssistantQuery', 'pageAssistantFind', 'pageAssistantResult', 'main-content',
        'sidebarMenu'].map(id => [id, createNode()]));
    const field = Object.assign(createNode(), { labels: [{ textContent: 'Guardian name' }], type: 'text' });
    const link = Object.assign(createNode(), { textContent: 'Student Register', attributes: { href: '/Student/Index' } });
    ids['main-content'].querySelectorAll = () => [field];
    ids.sidebarMenu.querySelectorAll = () => [link];
    const doc = {
        documentElement: { lang: 'en-BD' }, body: { dataset: { authTenantId: '22', authSessionReference: 'session-a' } },
        getElementById: id => ids[id], createElement: () => createNode()
    };
    const navigated = [];
    assistant.mount(doc, { location: { assign: href => navigated.push(href) }, addEventListener() {} });
    ids.pageAssistantOpen.click();
    assert.equal(ids.pageAssistantDialog.open, true);
    ids.pageAssistantQuery.value = 'Guardian name';
    ids.pageAssistantFind.click();
    assert.equal(field.focusCount || 0, 0);
    ids.pageAssistantResult.children[1].click();
    assert.equal(field.focusCount, 1);
    assert.equal(ids.pageAssistantDialog.open, false);

    ids.pageAssistantOpen.click();
    ids.pageAssistantQuery.value = 'Student Register';
    ids.pageAssistantFind.click();
    doc.body.dataset.authTenantId = 'another-tenant';
    ids.pageAssistantResult.children[1].click();
    assert.deepEqual(navigated, []);
    assert.match(ids.pageAssistantResult.children[0].textContent, /changed/i);
});

test('local speech creates an editable label draft without searching or changing a business field', async () => {
    const ids = Object.fromEntries(['pageAssistantDialog', 'pageAssistantOpen', 'pageAssistantClose',
        'pageAssistantQuery', 'pageAssistantFind', 'pageAssistantResult', 'pageAssistantVoice',
        'pageAssistantVoiceStatus', 'main-content', 'sidebarMenu'].map(id => [id, createNode()]));
    const field = Object.assign(createNode(), { labels: [{ textContent: 'Guardian name' }], type: 'text' });
    ids['main-content'].querySelectorAll = () => [field];
    ids.sidebarMenu.querySelectorAll = () => [];
    class InstalledSpeech {
        constructor() { this.processLocally = false; InstalledSpeech.current = this; }
        static async available() { return 'available'; }
        start() { this.started = true; }
        abort() { this.aborted = true; }
    }
    const doc = {
        documentElement: { lang: 'en-BD' }, body: { dataset: { authTenantId: '22', authSessionReference: 'session-a' } },
        getElementById: id => ids[id], createElement: () => createNode()
    };
    const navigated = [];
    assistant.mount(doc, { SpeechRecognition: InstalledSpeech,
        location: { assign: href => navigated.push(href) }, addEventListener() {} });
    ids.pageAssistantOpen.click();
    await ids.pageAssistantVoice.click();
    assert.equal(InstalledSpeech.current.started, true);
    assert.equal(InstalledSpeech.current.processLocally, true);
    InstalledSpeech.current.onresult({ resultIndex: 0, results: [[{ transcript: 'Guardian name' }]] });
    assert.equal(ids.pageAssistantQuery.value, 'Guardian name');
    assert.equal(ids.pageAssistantResult.children.length, 0);
    assert.equal(field.focusCount || 0, 0);
    assert.deepEqual(navigated, []);
    ids.pageAssistantFind.click();
    assert.equal(InstalledSpeech.current.aborted, true);
    assert.equal(field.focusCount || 0, 0);
    assert.equal(ids.pageAssistantResult.children.length, 2);
    ids.pageAssistantResult.children[1].click();
    assert.equal(field.focusCount, 1);

    ids.pageAssistantOpen.click();
    await ids.pageAssistantVoice.click();
    const staleMic = InstalledSpeech.current;
    doc.body.dataset.authTenantId = 'different-institution';
    staleMic.onresult({ resultIndex: 0, results: [[{ transcript: 'Guardian name' }]] });
    assert.equal(staleMic.aborted, true);
    assert.equal(ids.pageAssistantQuery.value, '', 'stale speech cannot populate the assistant');
});

test('large page treatments retain canonical form and preference controls', () => {
    const prefs = fs.readFileSync(path.resolve(__dirname, '../../EduOS.App/Views/WorkspacePreferences/Index.cshtml'), 'utf8');
    const dashboard = fs.readFileSync(path.resolve(__dirname, '../../EduOS.App/Views/Dashboard/Index.cshtml'), 'utf8');
    const drafts = fs.readFileSync(path.resolve(__dirname, '../../EduOS.App/wwwroot/js/admissions/transcript-draft.js'), 'utf8');
    for (const id of ['workspacePreferencesForm', 'workspaceSave', 'workspaceDashboardPanels', 'workspaceTableCustomize'])
        assert.ok(prefs.includes(`id="${id}"`), id);
    assert.match(dashboard, /@if \(!Model\.OnboardingComplete\)/);
    assert.match(dashboard, /data-dashboard-panel="setup-checklist"/);
    assert.match(dashboard, /dashboard-hero @\(Model\.OnboardingComplete \? "dashboard-hero--ready" : ""\)/);
    assert.match(dashboard, /class="dashboard-account-disclosure" open="@\(!Model\.OnboardingComplete\)"/);
    assert.match(dashboard, /class="dashboard-status-list"/);
    assert.match(dashboard, /href="\/Admissions\/Applications"/);
    assert.match(dashboard, /href="\/FeeOperations\/Ledger"/);
    assert.match(drafts, /id:'admissionDraftDisclosure'/);
    assert.match(drafts, /id:'admissionReadinessDisclosure'/);
});
