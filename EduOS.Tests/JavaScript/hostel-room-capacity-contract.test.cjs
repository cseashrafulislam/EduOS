const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const service = read('EduOS.Service/Services/Hostel/HostelService.RoomCapacity.cs');
const api = read('EduOS.App/Controllers/Api/HostelController.cs');
const iface = read('EduOS.Core/Interfaces/IServices/IHostelService.cs');
const dto = read('EduOS.Core/DTOs/Hostel/HostelRoomCapacityDto.cs');
const view = read('EduOS.App/Views/HostelOperations/Index.cshtml');
const js = read('EduOS.App/wwwroot/js/hostel-operations/room-capacity.js');

test('capacity command is wired through service and authorized API', () => {
    assert.match(iface, /SetRoomCapacityAsync\(/);
    assert.match(api, /HttpPut\("rooms\/\{id:long\}\/capacity"\)/);
    assert.match(api, /Authorize\(Roles = "TenantAdmin,Principal,HostelWarden"\)/);
    assert.match(service, /public async Task<ApiResponse<HostelRoomInventoryDto>> SetRoomCapacityAsync/);
});
test('capacity updates are tenant and campus scoped', () => {
    assert.match(service, /x\.TenantId == tenant && x\.Id == roomId/);
    assert.match(service, /CanManageCampusAsync\(hostel\.CampusId, tenant/);
    assert.match(service, /x\.TenantId == tenant && x\.Id == hostel\.CampusId/);
});
test('physical beds and active allocation safeguards are enforced', () => {
    assert.match(service, /physicalBeds/);
    assert.match(service, /HostelAllocationState\.Active/);
    assert.match(service, /request\.Capacity < physicalBeds \|\| request\.Capacity < occupied/);
    assert.match(dto, /Range\(1, 1000\)/);
});
test('serializable concurrency, idempotency and audit are present', () => {
    assert.match(service, /IsolationLevel\.Serializable/);
    assert.match(service, /RowVersion\.AsSpan\(\)\.SequenceEqual\(version\)/);
    assert.match(service, /room\.Capacity == request\.Capacity/);
    assert.match(service, /DbUpdateConcurrencyException/);
    assert.match(service, /room\.UpdatedBy = _currentUser\.UserId/);
});
test('capacity UI uses antiforgery and DOM-safe controls', () => {
    assert.match(view, /id="hostelCapacityForm"/);
    assert.match(view, /room-capacity\.js/);
    assert.match(js, /RequestVerificationToken/);
    assert.match(js, /textContent/);
    assert.doesNotMatch(js, /innerHTML\s*=/);
    assert.match(js, /window\.location\.reload\(\)/);
});
test('capacity command cannot write Finance-owned rent', () => {
    assert.doesNotMatch(service, /RentPerBed\s*=/);
    assert.doesNotMatch(service, /MonthlyRent\s*=/);
});
