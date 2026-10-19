const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');
const service = read('EduOS.Service/Services/Hostel/HostelService.cs');
const risks = read('EduOS.Service/Services/Hostel/HostelService.CapacityRisk.cs');
const contract = read('EduOS.Core/Interfaces/IServices/IHostelService.cs');
const api = read('EduOS.App/Controllers/Api/HostelController.cs');
const csv = read('EduOS.Service/Services/Hostel/HostelService.CapacityExport.cs');
const view = read('EduOS.App/Views/HostelOperations/Index.cshtml');
const js = read('EduOS.App/wwwroot/js/hostel-operations/capacity.js');

test('risk cohort is authorized, bounded, stable, and filtered before final pagination', () => {
    assert.match(service, /if \(!CanManage\(\)\) return Denied<PagedResult<HostelCapacityRowDto>>\(\)/);
    assert.match(service, /room\.TenantId == tenant && hostel\.TenantId == tenant && campus\.TenantId == tenant/);
    assert.match(service, /AllowedCampusIds\(tenant\)/);
    assert.match(service, /if \(attentionOnly\) return await GetCapacityRiskAsync/);
    assert.match(risks, /GetCapacityAsync\(campusId, 1, 100, search, cancellationToken, false\)/);
    assert.match(risks, /TotalCount > 1000/);
    assert.match(risks, /next\.Data\.TotalCount != first\.Data\.TotalCount/);
    assert.match(risks, /!seen\.Add\(x\.RoomId\)/);
    assert.match(risks, /all\.Where\(x => x\.HasRisk\)\.ToList\(\)/);
    assert.match(risks, /risks\.Skip\(\(page - 1\) \* pageSize\)\.Take\(pageSize\)/);
    assert.doesNotMatch(risks, /StudentName|StudentCode|MonthlyRent|SecurityDeposit/);
});
test('risk filter remains consistent across authorized page and bounded CSV export', () => {
    assert.match(contract, /GetCapacityAsync\([^;]+bool attentionOnly = false\)/s);
    assert.match(contract, /ExportCapacityCsvAsync\([^;]+bool attentionOnly = false\)/s);
    assert.match(api, /HttpGet\("capacity"\)/);
    assert.match(api, /HttpGet\("capacity\/export"\)/);
    assert.match(api, /GetCapacityAsync\(campusId, page, pageSize, search, cancellationToken, attentionOnly\)/);
    assert.match(api, /ExportCapacityCsvAsync\(campusId, search, cancellationToken, attentionOnly\)/);
    assert.match(csv, /GetCapacityAsync\(campusId, 1, 100, search, cancellationToken, attentionOnly\)/);
    assert.match(csv, /GetCapacityAsync\(campusId, page, 100, search, cancellationToken, attentionOnly\)/);
    assert.match(csv, /TotalCount > 1000/);
    assert.match(view, /id="hostelCapacityAttentionOnly"/);
    assert.match(js, /params\.set\('attentionOnly','true'\)/);
    assert.match(js, /hostelCapacityAttentionOnly'\)\.addEventListener\('change',filterChanged\)/);
    assert.match(js, /query\(false\)/);
    assert.match(js, /requestSerial !== serial/);
    assert.doesNotMatch(js, /innerHTML/);
    new Function(js);
});
test('unprovisioned active rooms are flagged for operational attention', () => {
    assert.match(service, /room\.HostelActive && room\.RoomActive && room\.Capacity > 0 && activeBeds == 0/);
});
