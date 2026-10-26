const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');
test('route vehicle change reuses canonical link with tenant/campus, rider protection and concurrency',()=>{
 const service=read('EduOS.Service/Services/Transport/TransportService.UpcomingRouteVehicleChange.cs');
 const api=read('EduOS.App/Controllers/Api/TransportController.cs');
 const ui=read('EduOS.App/wwwroot/js/transport-operations/route-vehicle-upcoming-change.js');
 const view=read('EduOS.App/Views/TransportOperations/_RouteVehicleLinks.cshtml');
 const index=read('EduOS.App/Views/TransportOperations/Index.cshtml');
 const render=read('EduOS.App/wwwroot/js/transport-operations/route-vehicle-render.js');
 for(const part of ['CanManageCampusAsync','System.Data.IsolationLevel.Serializable',
  'link.VehicleId = vehicle.Id','link.RowVersion.AsSpan().SequenceEqual',
  'vehicle.RowVersion.AsSpan().SequenceEqual','x.TenantId == tenant',
  'RouteVehicleAssignmentId == link.Id','link.UpdatedBy = _currentUser.UserId',
  '_commandReceipts.ReplayAsync','_commandReceipts.RecordAsync'])
  assert.ok(service.includes(part),part);
 assert.ok(!service.includes('MonthlyFare')&&!service.includes('DefaultFare'));
 for(const route of ['replacement-vehicles','change-upcoming-vehicle'])
  assert.ok(api.includes(route),route);
 assert.ok(view.includes('transportUpcomingLinkVehiclePanel'));
 assert.ok(index.includes('route-vehicle-upcoming-change.js'));
 assert.ok(render.includes('u.changeUpcomingRouteVehicle'));
 assert.ok(ui.includes('assignmentRowVersion:source.rowVersion')&&ui.includes('vehicleRowVersion:chosen.rowVersion'));
 assert.ok(ui.includes('clientRequestId:pendingKey')&&ui.includes('pendingSignature'));
});
