const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');
const api = read('EduOS.App/Controllers/Api/CommunicationAdministrationController.cs');
const service = read('EduOS.Service/Services/Communication/CommunicationAdministrationService.Deliveries.cs');
const view = read('EduOS.App/Views/CommunicationAdministration/_Deliveries.cshtml');
const js = read('EduOS.App/wwwroot/js/communication/deliveries.js');

test('delivery history is wired from tenant-admin UI through authorized API and tenant-scoped service', () => {
    assert.match(api, /\[Authorize\(Roles = "TenantAdmin"\)\]/);
    assert.match(api, /\[AutoValidateAntiforgeryToken\]/);
    assert.match(api, /\[HttpGet\("deliveries"\)\]/);
    assert.match(api, /service\.GetDeliveriesAsync\(page, pageSize, channel, state, ct\)/);
    assert.match(service, /x\.TenantId == _user\.TenantId/);
    assert.match(service, /!CanManage\(\)/);
    assert.match(service, /pageSize is < 1 or > 100/);
    assert.match(view, /id="deliveryRows"/);
    assert.match(js, /\/api\/communication-admin\/deliveries\?/);
    assert.match(js, /credentials: 'same-origin'/);
    assert.doesNotMatch(js, /innerHTML|outerHTML|insertAdjacentHTML/);
    assert.doesNotMatch(service, /Body = x\.Body|ProtectedCredential =/);
});
