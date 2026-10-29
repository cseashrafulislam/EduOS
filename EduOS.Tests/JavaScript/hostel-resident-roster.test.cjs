'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs'), path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const source = read('EduOS.App/wwwroot/js/hostel-operations/resident-roster.js');
const service = read('EduOS.Service/Services/Hostel/HostelService.ResidentRoster.cs');
const dto = read('EduOS.Core/DTOs/Hostel/HostelResidentRosterRowDto.cs');
const controller = read('EduOS.App/Controllers/Api/HostelController.cs');
const view = read('EduOS.App/Views/HostelOperations/Index.cshtml');
test('roster UI uses authorized fresh endpoints and safe DOM', () => {
    assert.doesNotThrow(() => new Function(source));
    assert.match(source, /\/api\/hostel\/resident-roster\?/);
    assert.match(source, /\/api\/hostel\/resident-roster\/export\?/);
    assert.match(source, /credentials:'same-origin', cache:'no-store'/);
    assert.match(source, /ticket !== sequence \|\| applied !== snapshot/);
    assert.match(source, /textContent = String\(value/);
    assert.doesNotMatch(source, /innerHTML/);
    assert.match(view, /_ResidentRoster/);
    assert.match(view, /resident-roster\.js/);
});
test('roster is tenant/campus restricted, SQL paged, history preserving and export bounded', () => {
    for (const term of [
        'allocation.TenantId == tenant', 'enrollment.TenantId == tenant',
        'student.TenantId == tenant', 'bed.TenantId == tenant',
        'room.TenantId == tenant', 'hostel.TenantId == tenant',
        'campus.TenantId == tenant', 'enrollment.CampusId == hostel.CampusId',
        'AllowedCampusIds(tenant)', 'IgnoreQueryFilters().AsNoTracking()',
        'allocation.State == HostelAllocationState.Closed',
        'allocation.StartDate <= operatingDate',
        'allocation.EndDate.Value >= operatingDate',
        'pageSize is < 1 or > 100', '.Skip((page - 1) * pageSize).Take(pageSize)',
        'if (count > 1000)', 'CsvCell(row.StudentName)'
    ]) assert.ok(service.includes(term), term);
    assert.match(controller, /\[HttpGet\("resident-roster"\)\][\s\S]*?\[Authorize\(Roles = "TenantAdmin,Principal,HostelWarden"\)\]/);
    for (const term of ['MonthlyRent', 'SecurityDeposit', 'Phone', 'Email', 'Address', 'Guardian'])
        assert.ok(!dto.includes(term), term);
});
