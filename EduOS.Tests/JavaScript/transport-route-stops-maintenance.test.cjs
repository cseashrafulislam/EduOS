'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');
const service=read('EduOS.Service/Services/Transport/TransportService.RouteStopsMaintenance.cs');
const dto=read('EduOS.Core/DTOs/Transport/TransportRouteSetupDtos.cs');
const api=read('EduOS.App/Controllers/Api/TransportController.cs');
const mvc=read('EduOS.App/Controllers/TransportOperationsController.cs');
const view=read('EduOS.App/Views/TransportOperations/EditStops.cshtml');
const js=read('EduOS.App/wwwroot/js/transport-operations/route-stops-editor.js');
const listing=read('EduOS.App/wwwroot/js/transport-operations/route-setup.js');
const iface=read('EduOS.Core/Interfaces/IServices/ITransportService.cs');

test('editor scopes every route and stop to tenant and authorized campus',()=>{
    assert.match(service,/if \(!CanManage\(\)\)/);
    assert.match(service,/x\.TenantId == tenant && !x\.IsDeleted && x\.IsActive/);
    assert.match(service,/CanManageCampusAsync\(campus\.Id, tenant/);
    assert.match(api,/\[Authorize\(Roles = "TenantAdmin,Principal,TransportManager"\)\]/);
});
test('updates preserve canonical stop IDs, memberships and Finance-owned fares',()=>{
    assert.match(service,/stops\.Count != requested\.Count/);
    assert.match(service,/SequenceEqual\(requested\.Select\(x => x\.Id\)\.OrderBy\(x => x\)\)/);
    assert.doesNotMatch(service,/FareFromOrigin\s*=/);
    assert.doesNotMatch(service,/DefaultFare\s*=/);
    assert.doesNotMatch(service,/new RouteStop\s*\{/);
});
test('updates use serializable transaction, concurrency checks, idempotency and audit',()=>{
    assert.match(service,/System\.Data\.IsolationLevel\.Serializable/);
    assert.match(service,/TryDecodeVersion\(request\.RowVersion/);
    assert.match(service,/TryDecodeVersion\(item\.RowVersion/);
    assert.match(service,/if \(same\) return/);
    assert.match(service,/DbUpdateConcurrencyException/);
    assert.match(service,/stop\.UpdatedBy = _currentUser\.UserId/);
});
test('API, MVC, Razor and JS form one authorized workflow',()=>{
    assert.match(iface,/UpdateRouteStopsAsync/);
    assert.match(api,/\[HttpPut\("route-setup\/\{reference:guid\}\/stops"\)\]/);
    assert.match(api,/GetRouteStopsForEditAsync/);
    assert.match(mvc,/EditStops\(Guid reference\)/);
    assert.match(view,/transportRouteStopForm/);
    assert.match(js,/transportRouteStopForm/);
    assert.match(listing,/TransportOperations\/EditStops\?reference=/);
    assert.doesNotMatch(js,/innerHTML/);
});
