const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const api = read('EduOS.App/Controllers/Api/FeeBillingController.cs');
const service = read('EduOS.Service/Services/Finance/FeeBillingService.cs');
const js = read('EduOS.App/wwwroot/js/finance/fees.js');

test('cash collection preflight is antiforgery protected and rechecked on write', () => {
    assert.match(api, /AutoValidateAntiforgeryToken/);
    assert.match(api, /HttpPost\("payments\/preflight"\)/);
    assert.match(api, /PreviewCashCollectionAsync\(request, ct\)/);
    assert.match(js, /api\('\/payments\/preflight','POST',body\)/);
    assert.match(js, /preview\.invoices\[0\]\.dueAfter/);
    assert.match(service, /ValidCashCollectionRequest\(request\)/);
    assert.match(service, /WriteAsync\("cash collection"/);
    assert.match(service, /Isolation|ExecuteInTransactionAsync/);
});
test('cash receipts never mix campuses or expose unauthorized invoice references', () => {
    assert.match(service, /rows\.Select\(x => x\.CampusId\)\.Distinct\(\)\.Count\(\) != 1/);
    assert.match(service, /invoiceRows\.Select\(x => x\.CampusId\)\.Distinct\(\)\.Count\(\) != 1/);
    assert.match(service, /CanAccessBillingCampusesAsync\(rows\.Select\(x => x\.CampusId\), ct\)/);
    assert.match(service, /CanAccessBillingCampusesAsync\(invoiceRows\.Select\(x => x\.CampusId\), token\)/);
    assert.match(service, /rows\.Any\(x => x\.CurrencyCode != "BDT"\)/);
    assert.match(service, /invoiceRows\.Any\(x => x\.invoice\.CurrencyCode != "BDT"\)/);
});
