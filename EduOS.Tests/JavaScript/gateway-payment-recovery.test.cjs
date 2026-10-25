const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/platform/payment-reconciliation-history.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/SubscriptionPaymentHistory/Index.cshtml'), 'utf8');
test('gateway recovery is a distinct protected, non-HTML-injection UI', () => {
    assert.match(view, /id="gatewayRecoveryAntiforgery"/);
    assert.match(view, /@Html\.AntiForgeryToken\(\)/);
    assert.match(js, /admin\/gateway-pending/);
    assert.match(js, /admin\/gateway-payments\//);
    assert.match(js, /'RequestVerificationToken': token/);
    assert.match(js, /JSON\.stringify\(\{ rowVersion \}\)/);
    assert.doesNotMatch(js, /innerHTML\s*=/);
    assert.match(js, /textContent/);
});
