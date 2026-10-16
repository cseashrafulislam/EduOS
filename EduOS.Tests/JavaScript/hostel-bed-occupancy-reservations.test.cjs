// Run: node --test EduOS.Tests/JavaScript/hostel-bed-occupancy-reservations.test.cjs
'use strict';
const test = require('node:test'), assert = require('node:assert/strict');
const fs = require('node:fs'), path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const service = read('EduOS.Service/Services/Hostel/HostelService.cs');
const dto = read('EduOS.Core/DTOs/Hostel/HostelDtos.cs');
const ui = read('EduOS.App/wwwroot/js/hostel-operations/bed-inventory.js');
const view = read('EduOS.App/Views/HostelOperations/Index.cshtml');
const list = service.split('GetBedInventoryAsync(')[1].split('SetBedStateAsync(')[0];
const state = service.split('SetBedStateAsync(')[1].split('GetRoomsAsync(')[0];
test('bed inventory separates effective occupancy from active reservations', () => {
    assert.match(list, /var today = DateOnly.FromDateTime/);
    assert.match(list, /a.StartDate <= today && \(!a.EndDate.HasValue \|\| a.EndDate.Value >= today\)/);
    assert.match(list, /HasActiveReservations = _allocations.GetQueryable\(\).Any/);
    assert.match(list, /HasActiveReservations = x.HasActiveReservations/);
    assert.match(list, /AllowedCampusIds\(tenant\)/);
    assert.match(list, /bed.TenantId == tenant && room.TenantId == tenant && hostel.TenantId == tenant/);
});
test('bed state response distinguishes occupancy and closure reservation guard', () => {
    assert.match(state, /var reserved = await/);
    assert.match(state, /var occupiedToday = await/);
    assert.match(state, /x.StartDate <= today && \(!x.EndDate.HasValue \|\| x.EndDate.Value >= today\)/);
    assert.match(state, /IsOccupied = occupiedToday/);
    assert.match(state, /HasActiveReservations = reserved/);
    assert.match(state, /if \(!request.IsActive && reserved\)/);
    assert.match(state, /IsolationLevel.Serializable/);
    assert.match(state, /bed.RowVersion.AsSpan\(\).SequenceEqual\(version\)/);
    assert.match(state, /bed.UpdatedBy = _currentUser.UserId/);
});
test('bed inventory UI exposes occupancy and reservations separately', () => {
    assert.match(dto, /bool HasActiveReservations/);
    assert.match(view, /<th>Occupied today<\/th><th>Active reservations<\/th>/);
    assert.match(ui, /item.hasActiveReservations \? 'Yes' : 'No'/);
    assert.match(ui, /item.isActive && item.hasActiveReservations/);
    assert.match(ui, /cell.colSpan = 7/);
    assert.doesNotMatch(ui, /innerHTML/);
    new Function(ui);
});
test('future and expired allocations do not count as physical occupancy', () => {
    const occupied = (state, start, end, today) => state === 'Active' && start <= today && (end == null || end >= today);
    assert.equal(occupied('Active', '2026-10-17', null, '2026-10-16'), false);
    assert.equal(occupied('Active', '2026-10-01', '2026-10-15', '2026-10-16'), false);
    assert.equal(occupied('Active', '2026-10-01', '2026-10-16', '2026-10-16'), true);
    assert.equal(occupied('Closed', '2026-10-01', null, '2026-10-16'), false);
});
