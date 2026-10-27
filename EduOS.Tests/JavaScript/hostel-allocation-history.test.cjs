'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs'), path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root,file),'utf8');
const service = read('EduOS.Service/Services/Hostel/HostelService.AllocationHistory.cs');
const dto = read('EduOS.Core/DTOs/Hostel/HostelAllocationHistoryRowDto.cs');
const iface = read('EduOS.Core/Interfaces/IServices/IHostelService.cs');
const api = read('EduOS.App/Controllers/Api/HostelController.cs');
const view = read('EduOS.App/Views/HostelOperations/Index.cshtml');
const partial = read('EduOS.App/Views/HostelOperations/_AllocationHistory.cshtml');
const js = read('EduOS.App/wwwroot/js/hostel-operations/allocation-history.js');
assert.match(service,/GetAllocationHistoryAsync/);
for (const marker of ['allocation.TenantId == tenant','enrollment.TenantId == tenant',
    'student.TenantId == tenant','bed.TenantId == tenant','room.TenantId == tenant',
    'hostel.TenantId == tenant','campus.TenantId == tenant',
    'enrollment.CampusId == hostel.CampusId','AllowedCampusIds(tenant)',
    'OrderByDescending(x => x.StartDate).ThenByDescending(x => x.Id)',
    'pageSize is < 1 or > 100','fromDate.Value > toDate.Value'])
    assert.ok(service.includes(marker),marker);
// Preserve the canonical reservation and student privacy boundary, but retain
// historical locations after their physical inventory is retired.
for (const marker of ['!allocation.IsDeleted','!enrollment.IsDeleted','!student.IsDeleted',
    '_beds.GetQueryable().IgnoreQueryFilters().AsNoTracking()',
    '_rooms.GetQueryable().IgnoreQueryFilters().AsNoTracking()',
    '_hostels.GetQueryable().IgnoreQueryFilters().AsNoTracking()',
    '_campuses.GetQueryable().IgnoreQueryFilters().AsNoTracking()'])
    assert.ok(service.includes(marker), marker);
assert.match(iface,/GetAllocationHistoryAsync/);
assert.match(api,/\[HttpGet\("allocation-history"\)\][\s\S]*?\[Authorize\(Roles = "TenantAdmin,Principal,HostelWarden"\)\]/);
assert.match(api,/\[ResponseCache\(NoStore = true, Location = ResponseCacheLocation.None\)\]/);
assert.match(view,/_AllocationHistory/);
assert.match(view,/allocation-history.js/);
assert.match(partial,/hostelHistoryPanel/);
assert.match(js,/credentials:'same-origin',cache:'no-store'/);
assert.match(js,/textContent/);
assert.doesNotMatch(js,/innerHTML/);
for (const forbidden of ['MonthlyRent','SecurityDeposit','Phone','Email','Address'])
    assert.ok(!dto.includes(forbidden),forbidden);
new Function(js);
console.log('Hostel allocation history contracts and JavaScript syntax: PASS');
