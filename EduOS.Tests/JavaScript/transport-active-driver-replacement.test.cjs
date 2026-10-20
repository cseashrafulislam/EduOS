const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');
test('driver replacement uses canonical history and serializable transaction',()=>{
 const s=read('EduOS.Service/Services/Transport/TransportService.ActiveDriverReplacement.cs');
 for(const v of ['_driverLinks.GetQueryable()','System.Data.IsolationLevel.Serializable','source.IsCurrent = false','source.EffectiveTo = yesterday','new VehicleDriverAssignment','CanManageCampusAsync','source.RowVersion.AsSpan().SequenceEqual','target.RowVersion.AsSpan().SequenceEqual'])
  assert.ok(s.includes(v),v);
 assert.ok(!s.includes('MonthlyFare')&&!s.includes('SecurityDeposit'));
});
test('driver replacement is authorized and wired through MVC UI',()=>{
 const api=read('EduOS.App/Controllers/Api/TransportController.cs');
 const view=read('EduOS.App/Views/TransportOperations/_VehicleDriverLinks.cshtml');
 const index=read('EduOS.App/Views/TransportOperations/Index.cshtml');
 const js=read('EduOS.App/wwwroot/js/transport-operations/vehicle-driver-replace.js');
 const options=read('EduOS.App/wwwroot/js/transport-operations/vehicle-driver-replace-options.js');
 const submit=read('EduOS.App/wwwroot/js/transport-operations/vehicle-driver-replace-submit.js');
 assert.ok(api.includes('vehicle-driver-links/{id:long}/replace-active'));
 assert.ok(api.includes('ReplaceActiveDriverAsync'));
 assert.ok(view.includes('transportVehicleDriverReplaceForm'));
 assert.ok(index.includes('vehicle-driver-replace-submit.js'));
 assert.ok(js.includes('driverRowVersion:d.rowVersion'));
 assert.ok(options.includes('u.replacementRows'));
 assert.ok(submit.includes('u.replaceDriver(item)'));
});
