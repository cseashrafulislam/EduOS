'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'../..');
const read=file=>fs.readFileSync(path.join(root,file),'utf8');
const service=read('EduOS.Service/Services/Transport/TransportVehicleCaseService.cs');
const api=read('EduOS.App/Controllers/Api/TransportVehicleCasesController.cs');
const view=read('EduOS.App/Views/TransportOperations/_VehicleSafetyImpact.cshtml');
const index=read('EduOS.App/Views/TransportOperations/Index.cshtml');
const js=read('EduOS.App/wwwroot/js/transport-operations/vehicle-safety-impact.js');
for(const marker of ['SafetyImpactQuery','AllowedCampuses()','item.TenantId == tenant',
 'rider.TenantId == tenant','item.VehicleId == vehicle.Id','CrossCampusCaseHold',
 'Take(501)','More than 500 vehicles','Cell(row.VehicleNumber)','DateOnly operatingDate'])
 assert.ok(service.includes(marker),marker);
for(const marker of ['[RequireModule("TRANSPORT")]','[AutoValidateAntiforgeryToken]',
 '[Authorize(Roles = "TenantAdmin,Principal,TransportManager")]','NoStore = true',
 'safety-impact/export'])assert.ok(api.includes(marker),marker);
for(const marker of ['transportSafetyImpactFilters','transportSafetyImpactRows',
 'transportSafetyImpactDate','transportSafetyImpactExport'])assert.ok(view.includes(marker),marker);
assert.ok(index.includes('_VehicleSafetyImpact'));
assert.ok(index.includes('vehicle-safety-impact.js'));
for(const marker of ["credentials:'same-origin'","cache:'no-store'",
 'textContent','URLSearchParams','changed. Refresh before printing'])
 assert.ok(js.includes(marker),marker);
assert.ok(!js.includes('innerHTML'));
new Function(js);
console.log('Transport vehicle safety impact API/UI/JS contracts: PASS');
