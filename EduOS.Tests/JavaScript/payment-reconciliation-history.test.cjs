const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const script = fs.readFileSync(path.resolve(__dirname, '../../EduOS.App/wwwroot/js/platform/payment-reconciliation-history.js'), 'utf8');

test('reconciliation decision history remains read-only, paged, and safely rendered', () => {
    const history = script.split('// Recovery is intentionally a separate')[0];
    assert.match(history, /admin\/reconciliation-history/);
    assert.match(history, /URLSearchParams/);
    assert.match(history, /replaceChildren/);
    assert.match(history, /textContent/);
    assert.doesNotMatch(history, /innerHTML|method:\s*['"]POST['"]|method:\s*['"]PUT['"]/);
    assert.match(history, /paymentState/);
    assert.match(history, /invoiceDueAmount/);
});
test('provider recovery and finance review actions require CSRF and server-side provider evidence', () => {
    assert.doesNotMatch(script, /innerHTML/);
    assert.match(script, /gatewayRecoveryAntiforgery/);
    assert.match(script, /RequestVerificationToken': token/);
    assert.match(script, /\/inspect-provider/);
    assert.match(script, /\/reconcile/);
    assert.match(script, /\/finance-review/);
    assert.match(script, /evidenceAuditId: financeEvidenceAuditId/);
    assert.match(script, /rowVersion: financeRowVersion/);
});
