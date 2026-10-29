'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs'), path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root,file),'utf8');
const service = read('EduOS.Service/Services/Transport/TransportService.BoardingManifest.cs');
const dto = read('EduOS.Core/DTOs/Transport/TransportBoardingManifestRowDto.cs');
const api = read('EduOS.App/Controllers/Api/TransportBoardingManifestController.cs');
const iface = read('EduOS.Core/Interfaces/IServices/ITransportService.cs');
const view = read('EduOS.App/Views/TransportOperations/Index.cshtml');
const partial = read('EduOS.App/Views/TransportOperations/_BoardingManifest.cshtml');
const js = read('EduOS.App/wwwroot/js/transport-operations/boarding-manifest.js');
for (const marker of ['assignment.TenantId == tenant','enrollment.TenantId == tenant',
 'student.TenantId == tenant','route.TenantId == tenant','vehicle.TenantId == tenant',
 'campus.TenantId == tenant','AllowedCampusIds(tenant)','!assignment.IsDeleted',
 'enrollment.StudentId == assignment.StudentId','assignment.State == TransportAssignmentState.Active',
 'assignment.StartDate <= operatingDate','link.EffectiveFrom <= operatingDate',
 'pageSize is < 1 or > 100','TotalCount > 500','CsvCell(row.StudentCode)'])
 assert.ok(service.includes(marker),marker);
assert.match(iface,/GetBoardingManifestAsync/);
assert.match(api,/\[Authorize\(Roles = "TenantAdmin,Principal,TransportManager"\)\]/);
assert.match(api,/\[RequireModule\("TRANSPORT"\)\]/);
assert.match(api,/NoStore = true/);
assert.match(view,/_BoardingManifest/);
assert.match(view,/boarding-manifest.js/);
assert.match(partial,/transportBoardingManifestPanel/);
assert.match(js,/credentials:'same-origin',cache:'no-store'/);
assert.match(js,/textContent/);
assert.doesNotMatch(js,/innerHTML/);
for (const forbidden of ['FullName','Phone','Email','MonthlyFare','Address','DriverName'])
 assert.ok(!dto.includes(forbidden),forbidden);
new Function(js);
console.log('Transport boarding manifest contracts and JS syntax: PASS');
