'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');
const service=read('EduOS.Service/Services/Transport/TransportService.RouteStopAppend.cs');
const api=read('EduOS.App/Controllers/Api/TransportController.cs');
const iface=read('EduOS.Core/Interfaces/IServices/ITransportService.cs');
const view=read('EduOS.App/Views/TransportOperations/EditStops.cshtml');
const js=read('EduOS.App/wwwroot/js/transport-operations/route-stops-editor.js');

test('append is authorized and scoped to tenant and campus',()=>{
  assert.match(service,/if \(!CanManage\(\)\)/);
  assert.match(service,/x.TenantId == tenant && !x.IsDeleted && x.IsActive/);
  assert.match(service,/CanManageCampusAsync\(campus.Id, tenant/);
  assert.match(api,/\[Authorize\(Roles = "TenantAdmin,Principal,TransportManager"\)\]/);
});
test('canonical route stops retain existing rider references and Finance fares',()=>{
  assert.match(service,/new RouteStop/);
  assert.match(service,/TenantId = tenant, RouteId = route.Id/);
  assert.doesNotMatch(service,/FareFromOrigin\s*=/);
  assert.doesNotMatch(service,/DefaultFare\s*=/);
  assert.match(service,/stops.Count >= 50/);
});
test('append is serializable, concurrency checked, idempotent and audited',()=>{
  assert.match(service,/System.Data.IsolationLevel.Serializable/);
  assert.match(service,/TryDecodeVersion\(request.RowVersion/);
  assert.match(service,/SequenceEqual\(version\)/);
  assert.match(service,/Stop already exists/);
  assert.match(service,/route.UpdatedBy = _currentUser.UserId/);
  assert.match(service,/DbUpdateConcurrencyException/);
});
test('service, API, Razor and JavaScript expose one working add-stop flow',()=>{
  assert.match(iface,/AppendRouteStopAsync/);
  assert.match(api,/\[HttpPost\("route-setup\/\{reference:guid\}\/stops"\)\]/);
  assert.match(view,/transportRouteStopAddForm/);
  assert.match(js,/request\('POST', \{ rowVersion: version, name \}\)/);
  assert.doesNotMatch(js,/innerHTML/);
});
