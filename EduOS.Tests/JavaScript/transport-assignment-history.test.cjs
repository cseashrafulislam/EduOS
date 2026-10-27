'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs'), path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root,file),'utf8');
const service = read('EduOS.Service/Services/Transport/TransportService.AssignmentHistory.cs');
const dto = read('EduOS.Core/DTOs/Transport/TransportAssignmentHistoryRowDto.cs');
const iface = read('EduOS.Core/Interfaces/IServices/ITransportService.cs');
const api = read('EduOS.App/Controllers/Api/TransportController.cs');
const view = read('EduOS.App/Views/TransportOperations/Index.cshtml');
const partial = read('EduOS.App/Views/TransportOperations/_AssignmentHistory.cshtml');
const js = read('EduOS.App/wwwroot/js/transport-operations/assignment-history.js');
for (const marker of ['assignment.TenantId == tenant','enrollment.TenantId == tenant',
    'student.TenantId == tenant','route.TenantId == tenant','vehicle.TenantId == tenant',
    'campus.TenantId == tenant','enrollment.StudentId == assignment.StudentId',
    'AllowedCampusIds(tenant)','OrderByDescending(x => x.StartDate).ThenByDescending(x => x.Id)',
    'pageSize is < 1 or > 100','fromDate.Value > toDate.Value',
    'route.CampusId.Value == enrollment.CampusId','vehicle.CampusId.Value == enrollment.CampusId'])
    assert.ok(service.includes(marker),marker);
// Preserve canonical reservation/student visibility and tenant/campus authorization;
// retired routes, vehicles and campuses remain available for historical readback.
for (const marker of ['!assignment.IsDeleted','!enrollment.IsDeleted','!student.IsDeleted',
    '_routes.GetQueryable().IgnoreQueryFilters().AsNoTracking()',
    '_vehicles.GetQueryable().IgnoreQueryFilters().AsNoTracking()',
    '_campuses.GetQueryable().IgnoreQueryFilters().AsNoTracking()'])
    assert.ok(service.includes(marker), marker);
assert.match(iface,/GetAssignmentHistoryAsync/);
assert.match(api,/\[HttpGet\("assignment-history"\)\][\s\S]*?\[Authorize\(Roles = "TenantAdmin,Principal,TransportManager"\)\]/);
assert.match(api,/\[ResponseCache\(NoStore = true, Location = ResponseCacheLocation.None\)\]/);
assert.match(view,/_AssignmentHistory/);
assert.match(view,/assignment-history.js/);
assert.match(partial,/transportHistoryPanel/);
assert.match(js,/credentials:'same-origin',cache:'no-store'/);
assert.match(js,/textContent/);
assert.doesNotMatch(js,/innerHTML/);
for (const forbidden of ['MonthlyFare','ClientRequestId','Phone','Email','Address'])
    assert.ok(!dto.includes(forbidden),forbidden);
new Function(js);
console.log('Transport assignment history contracts and JavaScript syntax: PASS');
