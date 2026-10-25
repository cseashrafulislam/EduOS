const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');
test('scheduled driver change has authorized API, tenant isolation, concurrency and MVC UI',()=>{
 const service=read('EduOS.Service/Services/Transport/TransportService.UpcomingDriverChange.cs');
 const api=read('EduOS.App/Controllers/Api/TransportController.cs');
 const ui=read('EduOS.App/wwwroot/js/transport-operations/vehicle-driver-upcoming-change.js');
 const view=read('EduOS.App/Views/TransportOperations/_VehicleDriverLinks.cshtml');
 const index=read('EduOS.App/Views/TransportOperations/Index.cshtml');
 const list=read('EduOS.App/wwwroot/js/transport-operations/vehicle-driver-list.js');
 for(const part of ['CanManageDriverRegistry','CanManageCampusAsync','System.Data.IsolationLevel.Serializable',
  'link.TransportDriverId = driver.Id','link.RowVersion.AsSpan().SequenceEqual',
  'driver.RowVersion.AsSpan().SequenceEqual','x.TenantId == tenant',
  'link.UpdatedBy = _currentUser.UserId','!x.IsDeleted && x.IsCurrent'])
  assert.ok(service.includes(part),part);
 assert.ok(!service.includes('MonthlyFare')&&!service.includes('FeeHead'));
 for(const route of ['replacement-drivers','change-upcoming-driver'])assert.ok(api.includes(route),route);
 assert.ok(api.includes('[Authorize(Roles = "TenantAdmin")]'));
 assert.ok(view.includes('transportUpcomingDriverChangePanel'));
 assert.ok(index.includes('vehicle-driver-upcoming-change.js'));
 assert.ok(list.includes('u.changeUpcomingDriver'));
 assert.ok(ui.includes('assignmentRowVersion:source.rowVersion'));
 assert.ok(ui.includes('driverRowVersion')&&ui.includes('selectedOptions'));
});
