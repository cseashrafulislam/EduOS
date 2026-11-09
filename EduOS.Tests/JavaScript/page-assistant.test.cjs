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

test('global UI is authenticated, local-only and cannot submit or write ERP fields', () => {
    const layout = fs.readFileSync(path.resolve(__dirname, '../../EduOS.App/Views/Shared/_Layout.cshtml'), 'utf8');
    const source = fs.readFileSync(path.resolve(__dirname, '../../EduOS.App/wwwroot/js/platform/page-assistant.js'), 'utf8');
    assert.match(layout, /id="pageAssistantDialog"/);
    assert.match(layout, /js\/platform\/page-assistant\.js/);
    assert.match(layout, /@if \(User\?\.Identity\?\.IsAuthenticated == true\)/);
    assert.doesNotMatch(source, /\bfetch\s*\(|\bXMLHttpRequest\b|\.submit\s*\(|\.requestSubmit\s*\(|localStorage|sessionStorage/);
    assert.doesNotMatch(source, /(?:target|field|node)\.value\s*=/);
});

function createNode() {
    const listeners = new Map();
    return {
        children: [], value: '', textContent: '', isConnected: true,
        addEventListener(name, listener) { listeners.set(name, listener); },
        click() { listeners.get('click')?.({ preventDefault() {} }); },
        keydown(key) { listeners.get('keydown')?.({ key, preventDefault() {} }); },
        append(...items) { this.children.push(...items); },
        replaceChildren(...items) { this.children = [...items]; },
        focus() { this.focusCount = (this.focusCount || 0) + 1; },
        scrollIntoView() {},
        showModal() { this.open = true; },
        close() { this.open = false; listeners.get('close')?.(); },
        getAttribute(name) { return this.attributes?.[name] || null; },
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

test('large page treatments retain canonical form and preference controls', () => {
    const prefs = fs.readFileSync(path.resolve(__dirname, '../../EduOS.App/Views/WorkspacePreferences/Index.cshtml'), 'utf8');
    const dashboard = fs.readFileSync(path.resolve(__dirname, '../../EduOS.App/Views/Dashboard/Index.cshtml'), 'utf8');
    const drafts = fs.readFileSync(path.resolve(__dirname, '../../EduOS.App/wwwroot/js/admissions/transcript-draft.js'), 'utf8');
    for (const id of ['workspacePreferencesForm', 'workspaceSave', 'workspaceDashboardPanels', 'workspaceTableCustomize'])
        assert.ok(prefs.includes(`id="${id}"`), id);
    assert.match(dashboard, /@if \(!Model\.OnboardingComplete\)/);
    assert.match(dashboard, /data-dashboard-panel="setup-checklist"/);
    assert.match(dashboard, /href="\/Admissions\/Applications"/);
    assert.match(dashboard, /href="\/FeeOperations\/Ledger"/);
    assert.match(drafts, /id:'admissionDraftDisclosure'/);
    assert.match(drafts, /id:'admissionReadinessDisclosure'/);
});
