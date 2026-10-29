const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../EduOS.App');
const script = fs.readFileSync(path.join(root, 'wwwroot/js/finance/finance-adjustments.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'Views/FinanceAdjustments/Index.cshtml'), 'utf8');

test('fine posting requires an explicit period and server-side journal command', () => {
    assert.match(script, /fines\/'\+row\.id\+'\/post-invoice'/);
    assert.match(script, /accountingPeriodId:period,rowVersion:row\.rowVersion/);
    assert.match(script, /if\(!Number\.isInteger\(period\)\|\|period<=0\)/);
    assert.match(script, /row\.billedInvoiceReference\?'Posted':'Unposted'/);
    assert.match(view, /id="fineRegisterPosted"/);
    assert.match(view, /id="adjustFinePeriod"/);
});
