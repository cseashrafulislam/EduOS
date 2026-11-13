const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

const pages = [
    'InstitutionManagement', 'TenantDomains', 'TenantTerminology',
    'TenantSettings', 'SmartSetup', 'NumberSeries',
    'TenantModules', 'SubscriptionBilling', 'IntegrationAdministration'
];

test('Institution & Settings uses the shared topbar heading without duplicate page titles or Refresh buttons', () => {
    const layout = read('EduOS.App/Views/Shared/_Layout.cshtml');
    assert.match(layout, /css\/institution-settings-workspace\.css/);
    for (const name of pages) {
        const markup = read(`EduOS.App/Views/${name}/Index.cshtml`);
        assert.match(markup, /institution-settings-workspace/, name + ' must use compact layout');
        assert.doesNotMatch(markup, /<h1\b/i, name + ' must not repeat the topbar title');
        assert.doesNotMatch(markup, /id="(?:reloadInstitution|refreshInstitutionHistory|domainRefresh|smartSetupReload|smartSetupInlineRefresh|sbRefresh)"/,
            name + ' must not show redundant Refresh controls');
    }
});

test('Institution & Settings preserves actionable forms, notices and accessible checkbox labels', () => {
    const settings = read('EduOS.App/Views/TenantSettings/Index.cshtml');
    const integrations = read('EduOS.App/Views/IntegrationAdministration/Index.cshtml');
    const smart = read('EduOS.App/Views/SmartSetup/Index.cshtml');
    assert.match(settings, /<label for="tsSensitive" class="form-check-label"/);
    assert.match(integrations, /<label for="iaWebhookClear" class="form-check-label"/);
    assert.match(integrations, /id="iaWebhookForm"/);
    assert.match(integrations, /id="iaCredentialForm"/);
    assert.equal((integrations.match(/<details\b/g) || []).length,
        (integrations.match(/<\/details>/g) || []).length);
    for (const id of ['smartSetupForm', 'smartSetupConfirm', 'smartSetupModules', 'smartSetupFrame']) {
        assert.match(smart, new RegExp('id="' + id + '"'));
    }
});

test('Institution & Settings compact styles respect narrow devices without changing other pages', () => {
    const css = read('EduOS.App/wwwroot/css/institution-settings-workspace.css');
    assert.match(css, /\.institution-settings-workspace/);
    assert.match(css, /@media \(max-width: 767\.98px\)/);
    assert.match(css, /\.form-check-input/);
    assert.match(css, /overflow-x: auto/);
});
