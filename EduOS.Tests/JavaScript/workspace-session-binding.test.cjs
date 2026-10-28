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

function boot(headers) {
    const site = read('EduOS.App/wwwroot/js/site.js');
    const start = site.indexOf('    const workspaceChoices = {');
    assert.ok(start > 0);
    const fragment = site.slice(start, site.lastIndexOf('})();'));
    const document = {
        body: { dataset: { workspacePreferences: 'enabled', authSessionReference: reference, authTenantId: '12' } },
        documentElement: { dataset: {} }
    };
    let verified = 0, events = 0;
    const window = {
        dispatchEvent() { events++; },
        EduOSVerifySessionContext() { verified++; }
    };
    const response = {
        ok: true, headers: { get(name) { return headers[name] ?? null; } },
        json: async () => ({ success: true, data: { theme: 'dark', palette: 'teal' } })
    };
    vm.runInNewContext('(() => {' + fragment + '})();', {
        window, document, fetch: async () => response, CustomEvent: class {
            constructor(name, options) { this.type = name; this.detail = options.detail; }
        }
    });
    return { window, document, get verified() { return verified; }, get events() { return events; } };
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
    assert.equal((source.match(/if \(!BindCurrentSession\(\)\)/g) || []).length, 3);
    const page = read('EduOS.App/Views/WorkspacePreferences/Index.cshtml');
    assert.match(page, /workspaceHelp/);
    assert.match(page, /page-header-help-panel/);
    const editor = read('EduOS.App/wwwroot/js/platform/workspace-preferences.js');
    assert.match(editor, /EduOSWorkspaceResponseMatchesSession/);
    assert.match(editor, /error\.sessionChanged/);
});
