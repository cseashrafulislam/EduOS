const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..', '..', 'EduOS.App', 'wwwroot', 'js', 'finance');
const adjustment = fs.readFileSync(path.join(root, 'finance-adjustments.js'), 'utf8');
const register = fs.readFileSync(path.join(root, 'finance-fine-register.js'), 'utf8');
const invoice = fs.readFileSync(path.join(root, 'student-invoice-posting.js'), 'utf8');

test('fine reversal exposes a separate request and independent review on the existing finance page', () => {
    assert.match(adjustment, /Request reversal/);
    assert.match(adjustment, /Approve reversal/);
    assert.match(adjustment, /Reject/);
    assert.match(adjustment, /\/reverse\/request/);
    assert.match(adjustment, /\/reverse\/.*?\/review/);
    assert.match(adjustment, /row\.reversalRowVersion/);
    assert.match(adjustment, /stableRequestId\('fine-reversal-'/);
});

test('reversed fine is distinguished from ordinary unpaid waiver in both registers', () => {
    assert.match(adjustment, /Reversed/);
    assert.match(adjustment, /Awaiting reversal review/);
    assert.match(register, /Number\(row\.reversalState\) === 4 \? 'Reversed'/);
    assert.match(invoice, /Cancelled \/ reversal/);
});
