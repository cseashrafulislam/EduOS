const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const app = path.resolve(__dirname, '..', '..', 'EduOS.App');
const js = fs.readFileSync(path.join(app, 'wwwroot/js/finance/payroll-payment-posting.js'), 'utf8');
const view = fs.readFileSync(path.join(app, 'Views/PayrollPaymentPosting/Index.cshtml'), 'utf8');

test('actual salary return requires dated receipt, reason, period and rowversion', () => {
    for (const field of ['payrollReturnDate', 'payrollReturnEvidence', 'payrollReturnReason',
        'payrollReturnPanel', 'payrollReturnSubmit']) {
        assert.match(view, new RegExp(`id="${field}"`));
        assert.match(js, new RegExp(field));
    }
    assert.match(js, /evidence\.length > 100/);
    assert.match(js, /reason\.length > 1000/);
    assert.match(js, /clientRequestId:returnRequestId/);
    assert.match(js, /rowVersion:returnSelected\.rowVersion/);
    assert.match(js, /crypto\.randomUUID\(\)/);
    assert.match(js, /headers\.RequestVerificationToken = token/);
    assert.match(js, /credentials:'same-origin'/);
});

test('salary return is a separate submitted request and maker-checker decision', () => {
    assert.match(js, /'\/returns', 'POST'/);
    assert.match(js, /'\/review', 'POST'/);
    assert.match(js, /Record return/);
    assert.match(js, /Approve return/);
    assert.match(js, /Reject/);
    assert.match(js, /No financial entry will post until approved/);
    assert.match(js, /immutable contra journal/);
    assert.match(js, /row\.reconciliationStatus === 'Reconciled'/);
    assert.match(js, /row\.returnRowVersion/);
});
