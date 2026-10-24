const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');

test('rider extension has authorized API, transactional capacity validation and interactive UI',()=>{
 const api=read('EduOS.App/Controllers/Api/TransportController.cs');
 const service=read('EduOS.Service/Services/Transport/TransportService.RiderExtension.cs');
 const view=read('EduOS.App/Views/TransportOperations/Index.cshtml');
 const js=read('EduOS.App/wwwroot/js/transport-operations/index.js');
 assert.match(api,/HttpPost\("assignments\/\{reference:guid\}\/extend"\)/);
 assert.match(api,/ExtendOperatingRiderAsync/);
 assert.match(service,/IsolationLevel\.Serializable/);
 assert.match(service,/CanManageCampusAsync/);
 assert.match(service,/RowVersion\.AsSpan\(\)\.SequenceEqual/);
 assert.match(service,/PeakReservedSeats/);
 assert.match(service,/x\.StudentId == assignment\.StudentId/);
 assert.match(service,/x\.EffectiveTo\.Value >= request\.EndDate/);
 assert.match(js,/transportRiderExtensionForm/);
 assert.match(js,/rowVersion: item\.rowVersion/);
 assert.match(js,/\/api\/transport\/assignments\/' \+ encodeURIComponent\(item\.reference\) \+ '\/extend'/);
 assert.match(view,/transportRiderExtensionPanel/);
 assert.match(view,/transportRiderExtensionDate/);
 assert.doesNotMatch(js,/innerHTML/);
});
