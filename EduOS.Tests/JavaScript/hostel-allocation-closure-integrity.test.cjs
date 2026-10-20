const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');

test('checkout is tenant and campus authorized, serialized and concurrency checked',()=>{
 const s=read('EduOS.Service/Services/Hostel/HostelService.AllocationClosure.cs');
 for(const token of ['CanManageCampusAsync','enrollment.StudentId == allocation.StudentId',
  'hostel.TenantId == tenant','System.Data.IsolationLevel.Serializable',
  'RowVersion.AsSpan().SequenceEqual(version)','IsValidClosureDate',
  'HostelAllocationState.Closed','_unitOfWork.SaveChangesAsync(ct)'])
  assert.ok(s.includes(token),token);
 assert.ok(!s.includes('SecurityDeposit =') && !s.includes('MonthlyRent ='));
});
test('existing API, MVC and JS preserve closure wiring and reject future dates',()=>{
 const api=read('EduOS.App/Controllers/Api/HostelController.cs');
 const js=read('EduOS.App/wwwroot/js/hostel-operations/index.js');
 const view=read('EduOS.App/Views/HostelOperations/Index.cshtml');
 assert.ok(api.includes('allocations/{id:long}/close'));
 assert.ok(api.includes('_service.CloseAsync(id, request, ct)'));
 assert.ok(js.includes('endDate > today()'));
 assert.ok(view.includes('hostelActiveRows'));
});
