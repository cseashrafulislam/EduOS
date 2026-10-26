// Run: node --test EduOS.Tests/JavaScript/hostel-capacity-effective-dates.test.cjs
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const service = fs.readFileSync(path.join(root, 'EduOS.Service/Services/Hostel/HostelService.cs'), 'utf8');
const layout = fs.readFileSync(path.join(root, 'EduOS.App/Views/Shared/_Layout.cshtml'), 'utf8');
const capacity = service.split('GetCapacityAsync(')[1].split('GetEligibleStudentsAsync(')[0];
test('current occupancy excludes future and expired allocations in both report queries', () => {
    assert.match(capacity, /var today = DateOnly.FromDateTime\(_clock.GetLocalNow\(\).DateTime\);/);
    const predicate = /allocation\.State == HostelAllocationState\.Active &&\s*allocation\.StartDate <= today &&\s*\(!allocation\.EndDate\.HasValue \|\| allocation\.EndDate\.Value >= today\)/g;
    assert.equal([...capacity.matchAll(predicate)].length, 2);
    assert.match(capacity, /Occupied = g\.Distinct\(\)\.Count\(\)/);
    assert.match(capacity, /Count = g\.Distinct\(\)\.Count\(\)/);
});
test('tenant/campus scoping, privacy and future reservation protection remain intact', () => {
    assert.match(capacity, /allocation\.TenantId == tenant && bed\.TenantId == tenant/);
    assert.match(capacity, /AllowedCampusIds\(tenant\)/);
    assert.doesNotMatch(capacity, /StudentName|StudentCode|MonthlyRent|SecurityDeposit/);
    const booking = service.split('GetAvailableBedsAsync(')[1].split('GetActiveAllocationsAsync(')[0];
    assert.match(booking, /x\.State != HostelAllocationState\.Cancelled/);
    assert.match(booking, /!x\.EndDate\.HasValue \|\| x\.EndDate\.Value >= reservationStart/);
});
test('inclusive effective-date boundary semantics', () => {
    const effective = (start, end, today) => start <= today && (end == null || end >= today);
    assert.equal(effective('2026-10-17', null, '2026-10-16'), false);
    assert.equal(effective('2026-10-16', null, '2026-10-16'), true);
    assert.equal(effective('2026-10-01', '2026-10-16', '2026-10-16'), true);
    assert.equal(effective('2026-10-01', '2026-10-15', '2026-10-16'), false);
    assert.equal(effective('2026-10-01', null, '2026-10-16'), true);
});
test('operations insights navigation restricted to management roles', () => {
    const link = layout.indexOf('href="/OperationsInsights/Index"');
    const gate = layout.lastIndexOf('User?.IsInRole("TenantAdmin") == true || User?.IsInRole("Principal") == true', link);
    const next = layout.indexOf('href="/AuditLogs/Index"', link);
    assert.ok(gate >= 0 && link > gate && next > link);
    assert.equal(layout.split('href="/OperationsInsights/Index"').length - 1, 1);
});
