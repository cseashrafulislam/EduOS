const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const app = path.resolve(__dirname, '../../EduOS.App');
const script = fs.readFileSync(path.join(app, 'wwwroot/js/finance/finance-adjustments.js'), 'utf8');
const register = fs.readFileSync(path.join(app, 'wwwroot/js/finance/finance-fine-register.js'), 'utf8');
const view = fs.readFileSync(path.join(app, 'Views/FinanceAdjustments/Index.cshtml'), 'utf8');

test('fine rule editor preserves rate type, amount and configurable grace days', () => {
  for (const id of ['adjustFrDaily', 'adjustFrAmount', 'adjustFrGrace'])
    assert.ok(view.includes('id="' + id + '"'), id);
  assert.match(script, /isDaily:el\('FrDaily'\)\.checked/);
  assert.match(script, /el\('FrDaily'\)\.checked=r\.isDaily/);
});

test('daily assessments demand selected original posted unpaid invoice and idempotency key', () => {
  assert.match(view, /id="adjustApplySource"/);
  assert.match(script, /fine-source-invoices\//);
  assert.match(script, /el\('ApplySource'\)\.required=daily/);
  assert.match(script, /body\.sourceInvoiceReference=source/);
  assert.match(script, /stableRequestId\('fine-application',body\)/);
  assert.match(register, /row\.chargedOverdueDays/);
});
