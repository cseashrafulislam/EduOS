// Run: node --test EduOS.Tests/JavaScript/hostel-room-occupancy-reservations.test.cjs
'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs'), path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');
const room = read('EduOS.Service/Services/Hostel/HostelService.RoomState.cs');
const service = read('EduOS.Service/Services/Hostel/HostelService.cs');
const dto = read('EduOS.Core/DTOs/Hostel/HostelRoomInventoryDto.cs');
const ui = read('EduOS.App/wwwroot/js/hostel-operations/room-inventory.js');
const view = read('EduOS.App/Views/HostelOperations/Index.cshtml');
const managerUi = read('EduOS.App/wwwroot/js/hostel-operations/index.js');
test('room inventory separates effective occupancy from active reservations', () => {
    const part = room.split('GetRoomInventoryAsync(')[1].split('SetRoomStateAsync(')[0];
    assert.match(part, /var reservations = await/);
    assert.match(part, /var occupied = await/);
    assert.match(part, /allocation\.StartDate <= today && \(!allocation\.EndDate\.HasValue \|\| allocation\.EndDate\.Value >= today\)/);
    assert.match(part, /HasActiveReservations = reservations\.ContainsKey\(x\.Id\)/);
    assert.match(part, /allocation\.TenantId == tenant && bed\.TenantId == tenant/);
    assert.match(part, /AllowedCampusIds\(tenant\)/);
});
test('room closure checks all active reservations and preserves concurrency', () => {
    const part = room.split('SetRoomStateAsync(')[1];
    assert.match(part, /if \(!request\.IsActive && reserved > 0\)/);
    assert.match(part, /HasActiveReservations = reserved > 0/);
    assert.match(part, /reserved > room\.Capacity/);
    assert.match(part, /room\.RowVersion\.AsSpan\(\)\.SequenceEqual\(version\)/);
    assert.match(part, /IsolationLevel\.Serializable/);
    assert.match(part, /room\.UpdatedBy = _currentUser\.UserId/);
});
test('active room summary uses distinct effective occupied beds', () => {
    const part = service.split('GetRoomsAsync(')[1].split('GetCapacityAsync(')[0];
    assert.match(part, /allocation\.StartDate <= today/);
    assert.match(part, /allocation\.EndDate\.Value >= today/);
    assert.match(part, /Count = g\.Distinct\(\)\.Count\(\)/);
});
test('room inventory UI distinguishes occupancy and closure restrictions', () => {
    assert.match(dto, /bool HasActiveReservations/);
    assert.match(ui, /item\.hasActiveReservations \? 'Yes' : 'No'/);
    assert.match(ui, /item\.isActive && item\.hasActiveReservations/);
    assert.match(view, /<th>Occupied today<\/th><th>Active reservations<\/th>/);
    assert.match(ui, /cell\.colSpan = 8/);
    assert.doesNotMatch(ui, /innerHTML/);
    new Function(ui);
});
test('inclusive dates exclude future and expired allocations without lifting reservations', () => {
    const occupied = (state, start, end, today) => state === 'Active' && start <= today && (end == null || end >= today);
    const reserved = state => state === 'Active';
    assert.equal(occupied('Active', '2026-10-17', null, '2026-10-16'), false);
    assert.equal(reserved('Active'), true);
    assert.equal(occupied('Active', '2026-10-01', '2026-10-16', '2026-10-16'), true);
    assert.equal(occupied('Active', '2026-10-01', '2026-10-15', '2026-10-16'), false);
    assert.equal(occupied('Closed', '2026-10-01', null, '2026-10-16'), false);
});

test('manager room dashboard uses canonical paged inventory; legacy compatibility fails explicitly over limit', () => {
    const part = service.split('GetRoomsAsync(')[1].split('GetCapacityAsync(')[0];
    assert.match(part, /var roomCount = await roomsQuery\.CountAsync\(cancellationToken\)/);
    assert.match(part, /if \(roomCount > 100\)/);
    assert.match(part, /roomsQuery\.Take\(100\)\.ToListAsync\(cancellationToken\)/);
    assert.match(part, /Room summary exceeds 100 rooms/);
    assert.doesNotMatch(managerUi, /\/api\/hostel\/rooms['"]/);
    assert.doesNotMatch(managerUi, /loadRooms\(/);
    assert.doesNotMatch(view, /hostelRoomRows/);
    assert.match(view, /hostelRoomInventoryPanel/);
    assert.match(ui, /\/api\/hostel\/room-inventory\?/);
    new Function(managerUi);
});
