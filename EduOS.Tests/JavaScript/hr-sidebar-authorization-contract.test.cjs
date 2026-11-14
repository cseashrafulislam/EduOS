'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

test('TenantAdmin-only attendance and leave-entitlement links never appear in Principal/HR block', () => {
    const layout = read('EduOS.App/Views/Shared/_Layout.cshtml');
    const sharedMarker = '@if (User?.IsInRole("TenantAdmin") == true || User?.IsInRole("Principal") == true || User?.IsInRole("HR") == true)';
    const adminMarker = '@if (User?.IsInRole("TenantAdmin") == true)';
    const sharedStart = layout.indexOf(sharedMarker);
    const adminStart = layout.indexOf(adminMarker, sharedStart + sharedMarker.length);
    assert(sharedStart >= 0 && adminStart > sharedStart, 'HR sidebar role boundaries must exist');
    const shared = layout.slice(sharedStart, adminStart);
    const admin = layout.slice(adminStart, layout.indexOf('@foreach (var hrSetup', adminStart));
    for (const [controller, route] of [
        ['EmployeeAttendanceController.cs', '/EmployeeAttendance/Index'],
        ['LeaveEntitlementsController.cs', '/LeaveEntitlements/Index']
    ]) {
        const source = read('EduOS.App/Controllers/' + controller);
        assert(source.includes('[Authorize(Roles = "TenantAdmin")]'),
            'Server-side TenantAdmin policy must remain unchanged: ' + controller);
        assert(!shared.includes('href="' + route + '"'),
            'Restricted route leaked to Principal or HR sidebar: ' + route);
        assert(admin.includes('href="' + route + '"'),
            'TenantAdmin must retain the authorized sidebar link: ' + route);
    }
});
