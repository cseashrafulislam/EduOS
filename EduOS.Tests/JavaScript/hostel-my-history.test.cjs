'use strict';
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const service = read('EduOS.Service/Services/Hostel/HostelService.MyAllocationHistory.cs');
const api = read('EduOS.App/Controllers/Api/HostelController.cs');
const mvc = read('EduOS.App/Controllers/HostelOperationsController.cs');
const view = read('EduOS.App/Views/HostelOperations/Index.cshtml');
const js = read('EduOS.App/wwwroot/js/hostel-operations/my-history.js');
for (const marker of ['student.UserId == user','allocation.TenantId == tenant',
'enrollment.TenantId == tenant','student.TenantId == tenant','bed.TenantId == tenant',
'room.TenantId == tenant','hostel.TenantId == tenant','campus.TenantId == tenant',
'enrollment.StudentId == allocation.StudentId','enrollment.CampusId == hostel.CampusId',
'IgnoreQueryFilters().AsNoTracking()','pageSize is < 1 or > 50','CsvCell(row.HostelName)'])
    assert.ok(service.includes(marker), marker);
assert.match(api, /HttpGet\("my-history"\)/);
assert.match(api, /HttpGet\("my-history\/export"\)/);
assert.match(mvc, /HttpGet\("hostel\/my-history\/print"\)/);
assert.match(view, /hostelMyHistoryPanel/);
assert.match(view, /hostel-operations\/my-history.js/);
assert.match(js, /textContent/);
assert.match(js, /credentials: 'same-origin', cache: 'no-store'/);
assert.doesNotMatch(js, /innerHTML/);
new Function(js);
console.log('Hostel self-service history contracts: PASS');
