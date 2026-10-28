const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');
const reference = 'A'.repeat(64);
const nextReference = 'B'.repeat(64);
const flush = () => new Promise(resolve => setImmediate(resolve));

function boot(headers, options = {}) {
    const site = read('EduOS.App/wwwroot/js/site.js');
    const start = site.indexOf('    const workspaceChoices = {');
    assert.ok(start > 0);
    const fragment = site.slice(start, site.lastIndexOf('})();'));
    const document = {
        body: { dataset: { workspacePreferences: 'enabled', authSessionReference: reference, authTenantId: '12' } },
        documentElement: { dataset: {} },
        getElementById(id) { return options.editor && id === 'workspacePreferencesForm' ? {} : null; }
    };
    let verified = 0, events = 0, fetches = 0, resolveJson;
    const window = {
        dispatchEvent() { events++; },
        EduOSVerifySessionContext() { verified++; }
    };
    const response = {
        ok: true, headers: { get(name) { return headers[name] ?? null; } },
        json: options.delayedJson
            ? () => new Promise(resolve => { resolveJson = resolve; })
            : async () => ({ success: true, data: { theme: 'dark', palette: 'teal' } })
    };
    vm.runInNewContext('(() => {' + fragment + '})();', {
        window, document, fetch: async () => { fetches++; return response; }, CustomEvent: class {
            constructor(name, options) { this.type = name; this.detail = options.detail; }
        }
    });
    return {
        window, document,
        finishJson() {
            assert.ok(resolveJson, 'response body should have started parsing');
            resolveJson({ success: true, data: { theme: 'dark', palette: 'teal' } });
        },
        get fetches() { return fetches; },
        get verified() { return verified; },
        get events() { return events; }
    };
}

test('same-session and same-institution response applies only whitelisted appearance', async () => {
    const b = boot({ 'X-EduOS-Session-Reference': reference, 'X-EduOS-Tenant-Id': '12' });
    await flush(); await flush();
    assert.equal(b.document.documentElement.dataset.workspaceTheme, 'dark');
    assert.equal(b.events, 1);
    assert.equal(b.verified, 0);
});

for (const [name, headers] of [
    ['other institution', { 'X-EduOS-Session-Reference': reference, 'X-EduOS-Tenant-Id': '99' }],
    ['rotated session', { 'X-EduOS-Session-Reference': nextReference, 'X-EduOS-Tenant-Id': '12' }],
    ['missing session headers', {}]
]) {
    test('workspace response from ' + name + ' cannot update a stale page', async () => {
        const b = boot(headers);
        await flush(); await flush();
        assert.equal(b.document.documentElement.dataset.workspaceTheme, undefined);
        assert.equal(b.events, 0);
        assert.equal(b.verified, 1);
    });
}

test('controller binds read, save and reset to a server-issued session reference', () => {
    const source = read('EduOS.App/Controllers/WorkspacePreferencesController.cs');
    assert.match(source, /AuthSessionRegistry\.ToPublicReference\(sid\)/);
    assert.match(source, /X-EduOS-Session-Reference/);
    assert.match(source, /X-EduOS-Tenant-Id/);
    // GET binds directly; both mutations bind through the shared serialized write guard.
    assert.equal(source.split('if (!BindCurrentSession())').length - 1, 2);
    assert.ok(source.includes('IsolationLevel.Serializable'));
    assert.equal(source.split('if (!await SessionStillValidAsync())').length - 1, 3);
    assert.ok(source.includes('WriteInCurrentSessionAsync(ct => service.SaveAsync(request, ct)'));
    assert.ok(source.includes('WriteInCurrentSessionAsync(ct => service.ResetAsync(request, ct)'));
    const page = read('EduOS.App/Views/WorkspacePreferences/Index.cshtml');
    assert.match(page, /workspaceHelp/);
    assert.match(page, /page-header-help-panel/);
    const editor = read('EduOS.App/wwwroot/js/platform/workspace-preferences.js');
    assert.match(editor, /EduOSWorkspaceResponseMatchesSession/);
    assert.match(editor, /error\.sessionChanged/);
});


test('a delayed response body cannot apply appearance after a session rotation', async () => {
    const b = boot({ 'X-EduOS-Session-Reference': reference, 'X-EduOS-Tenant-Id': '12' },
        { delayedJson: true });
    await flush();
    b.document.body.dataset.authSessionReference = nextReference;
    b.finishJson();
    await flush(); await flush();
    assert.equal(b.document.documentElement.dataset.workspaceTheme, undefined);
    assert.equal(b.events, 0);
    assert.equal(b.verified, 1);
});

test('a delayed response body cannot apply appearance after an institution change', async () => {
    const b = boot({ 'X-EduOS-Session-Reference': reference, 'X-EduOS-Tenant-Id': '12' },
        { delayedJson: true });
    await flush();
    b.document.body.dataset.authTenantId = '99';
    b.finishJson();
    await flush(); await flush();
    assert.equal(b.document.documentElement.dataset.workspaceTheme, undefined);
    assert.equal(b.events, 0);
    assert.equal(b.verified, 1);
});

test('appearance editor avoids duplicate shell fetch and stale preview overwrite', async () => {
    const b = boot({ 'X-EduOS-Session-Reference': reference, 'X-EduOS-Tenant-Id': '12' },
        { editor: true });
    await flush(); await flush();
    assert.equal(b.fetches, 0);
    assert.equal(b.events, 0);
});

test('editor verifies the institution session both before and after JSON decoding', () => {
    const editor = read('EduOS.App/wwwroot/js/platform/workspace-preferences.js');
    const request = editor.slice(editor.indexOf('    async function request('),
        editor.indexOf('    async function load('));
    assert.equal((request.match(/requireCurrentSession\(response\)/g) || []).length, 2);
    assert.match(request, /const body = await response\.json/);
});


test('cross-tab transition rejects otherwise matching workspace readback', async () => {
    const b = boot({ 'X-EduOS-Session-Reference': reference, 'X-EduOS-Tenant-Id': '12' });
    b.window.EduOSSessionTransitionPending = () => true;
    await flush(); await flush();
    assert.equal(b.document.documentElement.dataset.workspaceTheme, undefined);
    assert.equal(b.events, 0);
    assert.equal(b.verified, 1);
});

test('editor locks stale sessions and bounds network operations', () => {
    const editor = read('EduOS.App/wwwroot/js/platform/workspace-preferences.js');
    assert.match(editor, /sessionInvalidated = true;/);
    assert.match(editor, /if \(busy \|\| sessionInvalidated\) return;/);
    assert.match(editor, /const disabled = value \|\| sessionInvalidated;/);
    assert.match(editor, /if \(!sessionInvalidated\) window\.EduOSApplyWorkspacePreferences/);
    assert.match(editor, /const controller = new AbortController\(\);/);
    assert.match(editor, /window\.setTimeout\(\(\) => controller\.abort\(\), 10000\)/);
    assert.match(editor, /window\.clearTimeout\(timeout\)/);
});
