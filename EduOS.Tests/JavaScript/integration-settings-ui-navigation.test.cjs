'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const sidebar = require('../../EduOS.App/wwwroot/js/sidebar-navigation.js');

test('subscription usage and integration navigation share no duplicated sidebar tools', () => {
    const layout = read('EduOS.App/Views/Shared/_Layout.cshtml');
    const js = read('EduOS.App/wwwroot/js/sidebar-navigation.js');
    assert.equal(sidebar.groupFor('setup', '/UsageHistory/Index'), 'subscription');
    assert.equal(sidebar.groupFor('setup', '/SubscriptionBilling/Index'), 'subscription');
    assert.equal(sidebar.groupFor('setup', '/IntegrationAdministration/Index'), 'institution');
    assert.equal(sidebar.scoreActive('/IntegrationAdministration/Index', '/WebhookTests/Index'), 10000);
    assert.equal((layout.match(/href="\/UsageHistory\/Index"/g) || []).length, 1);
    assert.equal((layout.match(/href="\/IntegrationAdministration\/Index"/g) || []).length, 1);
    assert.doesNotMatch(layout, /href="\/WebhookTests\/Index"/);
    assert.match(js, /Subscription & Usage/);
    const usageAnchor = layout.indexOf('href="/UsageHistory/Index"');
    const gate = layout.slice(0, usageAnchor).split('@if').at(-1);
    assert.match(gate, /User\?\.IsInRole\("TenantAdmin"\) == true/);
});

test('integration, usage and webhook workspaces are compact and preserve all server actions', () => {
    const integration = read('EduOS.App/Views/IntegrationAdministration/Index.cshtml');
    const usage = read('EduOS.App/Views/UsageHistory/Index.cshtml');
    const webhook = read('EduOS.App/Views/WebhookTests/Index.cshtml');
    for (const view of [integration, usage, webhook])
        assert.doesNotMatch(view, /<h1\b|<h2>Integration Administration<\/h2>|<h2>Webhook Test Delivery<\/h2>/);
    for (const id of ['iaWebhookForm', 'iaWebhookEditor', 'iaWebhookRows', 'iaDeliveryRows',
        'iaDeliveryExport', 'iaCredentialForm', 'iaOneTimeKey', 'iaKeyPanel', 'iaCopyApiExample'])
        assert.match(integration, new RegExp('id="' + id + '"'));
    assert.match(integration, /href="\/WebhookTests\/Index"/);
    assert.match(integration, /href="\/UsageHistory\/Index"/);
    for (const id of ['uhQuotaRows', 'uhCapture', 'uhRows', 'uhAutomationStatus', 'uhFrom', 'uhTo'])
        assert.match(usage, new RegExp('id="' + id + '"'));
    assert.match(usage, /method="get" action="\/api\/usage-history\/export"/);
    assert.match(usage, /Html\.AntiForgeryToken/);
    assert.doesNotMatch(usage, /id="uhRefresh"/);
    assert.match(webhook, /asp-action="Test" method="post" data-confirm-webhook/);
    assert.match(webhook, /asp-action="Retry" method="post" data-confirm-webhook data-confirm-retry/);
    assert.match(webhook, /Html\.AntiForgeryToken/);
    assert.match(webhook, /delivery\.AttemptCount < 3/);
});

test('manual send and retry require separate user confirmation and cannot silently dispatch', () => {
    const source = read('EduOS.App/wwwroot/js/integration-admin/webhook-tests.js');
    const forms = [false, true].map(retry => ({
        handlers: {},
        hasAttribute: attr => retry && attr === 'data-confirm-retry',
        addEventListener(type, handler) { this.handlers[type] = handler; }
    }));
    const asked = [];
    const document = {
        documentElement: { lang: 'en-BD' },
        querySelectorAll: () => forms
    };
    vm.runInNewContext(source, { document, confirm: message => { asked.push(message); return false; } });
    for (const form of forms) {
        let prevented = false;
        form.handlers.submit({ preventDefault() { prevented = true; } });
        assert.equal(prevented, true);
    }
    assert.match(asked[0], /Send a signed webhook test/);
    assert.match(asked[1], /Retry this signed webhook test/);
    const admin = read('EduOS.App/wwwroot/js/integration-admin/admin.js');
    const usageJs = read('EduOS.App/wwwroot/js/usage-history/index.js');
    new Function(admin);
    new Function(usageJs);
    assert.match(admin, /\$\("iaWebhookEditor"\)\.open=true/);
    assert.match(admin, /\$\("iaWebhookAdd"\)\.addEventListener\("click"/);
    assert.doesNotMatch(usageJs, /uhRefresh/);
});
