const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');

test('tenant communication gateway form exposes every existing SMS and SMTP setting without a fixed provider', () => {
    const view = read('EduOS.App/Views/CommunicationAdministration/_Gateways.cshtml');
    const script = read('EduOS.App/wwwroot/js/communication/admin.js');
    const dto = read('EduOS.Core/DTOs/Communication/CommunicationDtos.cs');
    const service = read('EduOS.Service/Services/Communication/CommunicationAdministrationService.GatewayCommands.cs');
    const fields = {
        smsSenderId: 'gatewaySmsSenderId', smtpHost: 'gatewaySmtpHost', smtpPort: 'gatewaySmtpPort',
        smtpUsername: 'gatewaySmtpUsername', fromEmail: 'gatewayFromEmail', fromName: 'gatewayFromName',
        useSsl: 'gatewayUseSsl', endpoint: 'gatewayEndpoint', credential: 'gatewayCredential'
    };
    for (const [field, id] of Object.entries(fields)) {
        assert.ok(view.includes(`id="${id}"`), `missing ${field} input`);
        assert.match(script, new RegExp(`${field}: '${id}'`), `missing ${field} client binding`);
        assert.match(dto, new RegExp(`\\b${field[0].toUpperCase()}${field.slice(1)}\\b`));
    }
    assert.match(view, /id="gatewayCode"[^>]*required/);
    assert.doesNotMatch(view, /value="BULKSMSBD"|value="TWILIO"|value="SMTP"/);
    assert.match(script, /function syncGatewayFields\(\)/);
    assert.match(script, /data-gateway-sms/);
    assert.match(script, /data-gateway-smtp/);
    assert.match(script, /field === 'smtpPort'/);
    assert.match(script, /field === 'useSsl'/);
    assert.match(script, /field === 'credential' \|\| field === 'clearCredential'/);
    assert.match(service, /_protector\.Protect\(request\.Credential\)/);
    assert.match(service, /request\.SmsSenderId/);
    assert.match(service, /request\.SmtpHost/);
    assert.match(service, /request\.UseSsl/);
    assert.match(service, /Enabled SMS gateways require sender ID/);
    assert.match(service, /Enabled SMTP requires a public host/);
    assert.match(service, /Enabled email API gateways require a public HTTPS endpoint/);
});

test('channel-specific provider fields have English and Bangla labels and page manual matches actual saved contract', () => {
    const view = read('EduOS.App/Views/CommunicationAdministration/_Gateways.cshtml');
    const script = read('EduOS.App/wwwroot/js/communication/admin.js');
    const { guideFor } = require('../../EduOS.App/wwwroot/js/platform/page-context-help.js');
    for (const id of ['gatewaySmtpHost', 'gatewaySmtpPort', 'gatewaySmtpUsername', 'gatewayFromEmail',
        'gatewayFromName', 'gatewayUseSsl', 'gatewaySmsSenderId']) {
        const nearby = view.slice(Math.max(0, view.indexOf(`for="${id}"`) - 15), view.indexOf(`for="${id}"`) + 210);
        assert.match(nearby, /data-gateway-en=/, `${id} missing English label`);
        assert.match(nearby, /data-gateway-bn=/, `${id} missing Bangla label`);
    }
    assert.match(script, /document\.documentElement\.lang/);
    const en = guideFor('/CommunicationAdministration/Index', 'en-BD');
    const bn = guideFor('/CommunicationAdministration/Index', 'bn-BD');
    assert.match(en.steps.join(' '), /SMTP to supply host/);
    assert.match(en.steps.join(' '), /sender ID/);
    assert.match(bn.steps.join(' '), /SMTP/);
    assert.match(en.rules, /does not auto-send/);
    assert.match(bn.rules, /Alumni/);
});
