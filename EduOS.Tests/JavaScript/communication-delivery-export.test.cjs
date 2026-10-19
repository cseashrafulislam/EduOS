const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

test('delivery CSV export reuses canonical tenant administration owner', () => {
    const service = read('EduOS.Service/Services/Communication/CommunicationAdministrationService.DeliveryExport.cs');
    const api = read('EduOS.App/Controllers/Api/CommunicationDeliveryHistoryApiController.cs');
    const view = read('EduOS.App/Views/CommunicationAdministration/_Deliveries.cshtml');
    const js = read('EduOS.App/wwwroot/js/communication/deliveries.js');
    assert.match(service, /x\.TenantId == _user\.TenantId && !x\.IsDeleted/);
    assert.match(service, /!CanManage\(\)/);
    assert.match(service, /Take\(1001\)/);
    assert.match(service, /rows\.Count > 1000/);
    assert.doesNotMatch(service, /x\.Body|x\.ProtectedCredential/);
    assert.match(api, /\[Authorize\(Roles = "TenantAdmin"\)\]/);
    assert.match(api, /\[HttpGet\("export"\)\]/);
    assert.match(api, /ExportDeliveriesCsvAsync\(channel, state, ct\)/);
    assert.match(view, /id="deliveryExport"/);
    assert.match(js, /credentials: 'same-origin'/);
    assert.doesNotMatch(js, /innerHTML|outerHTML|insertAdjacentHTML/);
});
