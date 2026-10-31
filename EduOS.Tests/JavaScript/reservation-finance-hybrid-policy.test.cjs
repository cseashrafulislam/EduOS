const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

for (const kind of ['hostel', 'transport']) {
    test(`${kind} period Finance approval supports automatic, manual final and additive override`, () => {
        const js = fs.readFileSync(path.join(__dirname,
            `../../EduOS.App/wwwroot/js/finance/${kind}-clearances.js`), 'utf8');
        const viewName = kind === 'hostel' ? 'HostelClearances' : 'TransportClearances';
        const view = fs.readFileSync(path.join(__dirname,
            `../../EduOS.App/Views/FinanceAdjustments/${viewName}.cshtml`), 'utf8');
        assert.match(js, /manualNetAdjustment/);
        assert.match(js, /manualAdjustmentDelta/);
        assert.match(js, /'hybrid'/);
        assert.match(js, /togglePeriodInput/);
        assert.match(js, /selectedAmount === 0/);
        assert.match(js, /autoPostInvoice: auto/);
        assert.match(js, /reservations\/credit-options/);
        assert.match(js, /originalInvoiceReference: auto && selectedAmount < 0/);
        assert.match(js, /studentCreditLiabilityAccountId: auto && selectedAmount < 0/);
        assert.match(view, /CreditSource/);
        assert.match(view, /CreditLiability/);
        assert.match(view, /value="auto"/);
        assert.match(view, /value="manual"/);
        assert.match(view, /value="hybrid"/);
        assert.match(view, /Auto \+ Manual/);
    });
}
