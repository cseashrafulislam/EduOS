const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/platform/payment-reconciliation-history.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/SubscriptionPaymentHistory/Index.cshtml'), 'utf8');
test('gateway exceptions are paged by server-side category and render safely', () => {
    assert.match(view, /id="gatewayRecoveryCategory"/);
    assert.match(view, /value="Blocked"/);
    assert.match(view, /value="Overdue"/);
    assert.match(js, /admin\/gateway-exceptions/);
    assert.match(js, /query\.set\('category', category\)/);
    assert.match(js, /item\.exceptionReason/);
    assert.match(js, /item\.canReconcile === false/);
    assert.match(js, /textContent/);
    assert.doesNotMatch(js, /innerHTML\s*=/);
});
