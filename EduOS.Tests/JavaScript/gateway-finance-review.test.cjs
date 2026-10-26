const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/platform/payment-reconciliation-history.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/SubscriptionPaymentHistory/Index.cshtml'), 'utf8');

test('finance review history is navigable and rendered with text nodes', () => {
    assert.match(view, /id="gatewayFinancePanel"/);
    assert.match(view, /id="gatewayFinanceRows"/);
    assert.match(js, /\/finance-history\?/);
    assert.match(js, /financeRows\.replaceChildren\(\)/);
    assert.match(js, /financeRequestSeq/);
    assert.doesNotMatch(js, /innerHTML\s*=/);
});
test('finance review requires provider escalation evidence and antiforgery', () => {
    assert.match(js, /GatewayProviderEscalation/);
    assert.match(js, /providerConfirmed === true/);
    assert.match(js, /requiresManualReview === true/);
    assert.match(js, /RequestVerificationToken/);
    assert.match(js, /crypto\.randomUUID\(\)/);
    assert.match(js, /\/finance-review/);
    assert.match(js, /evidenceAuditId: financeEvidenceAuditId/);
    assert.doesNotMatch(js, /body:\s*JSON\.stringify\(\{[^}]*providerStatus/);
});
