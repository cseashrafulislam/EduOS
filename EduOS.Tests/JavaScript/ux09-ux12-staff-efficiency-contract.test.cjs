'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const source = p => fs.readFileSync(path.join(root, p), 'utf8');

// These tests intentionally verify source-level route/permission contracts.
// They do NOT impersonate a logged-in browser, database transaction or tenant.
const routes = [
  { id: 'F02', file: 'EduOS.App/Controllers/Api/StudentGuardiansController.cs',
    role: 'TenantAdmin', module: 'STUDENT',
    endpoints: ['students/{studentReference:guid}', 'existing', 'students/{studentReference:guid}/links/{linkId:long}/unlink',
      'students/{studentReference:guid}/links/{linkId:long}/reactivate'] },
  { id: 'F04', file: 'EduOS.App/Controllers/Api/StudentImportsController.cs',
    role: 'TenantAdmin', module: 'STUDENT',
    endpoints: ['headers', 'preview', 'errors', 'execute', 'job-status', 'cohort-preflight'] },
  { id: 'F05', file: 'EduOS.App/Controllers/Api/StudentAttendanceController.cs',
    role: 'TenantAdmin,Principal,VicePrincipal,Teacher', module: 'ATTENDANCE',
    endpoints: ['roster-page', 'sessions', 'sessions/{sessionId:long}/finalize',
      '{attendanceId:long}/correct'] },
  { id: 'F06', file: 'EduOS.App/Controllers/Api/StudentInvoicePostingController.cs',
    role: 'TenantAdmin,Principal,Accountant', module: 'FINANCE',
    endpoints: ['{invoiceReference:guid}/post'] },
  { id: 'F12', file: 'EduOS.App/Controllers/Api/AiVoiceAttendanceController.cs',
    role: 'TenantAdmin,Principal,VicePrincipal', module: 'ATTENDANCE',
    endpoints: ['campuses', 'attendance/preview'] }
];
for (const route of routes) {
  test(route.id + ' privileged API keeps module, role, CSRF, rate and no-store boundaries', () => {
    const s = source(route.file);
    assert.ok(s.includes('Authorize(Roles = "' + route.role + '"'), route.file + ': role');
    assert.ok(s.includes('RequireModule("' + route.module + '")'), route.file + ': module');
    assert.match(s, /AutoValidateAntiforgeryToken/);
    assert.match(s, /EnableRateLimiting\("ApiPolicy"\)/);
    assert.match(s, /ResponseCache\(NoStore = true, Location = ResponseCacheLocation.None\)/);
    for (const endpoint of route.endpoints) {
      assert.ok(s.includes('"' + endpoint + '"'), route.file + ': missing route ' + endpoint);
    }
  });
}
test('F04 import execution is explicit and keyed; preview/error export remain separate', () => {
  const s = source('EduOS.App/Controllers/Api/StudentImportsController.cs');
  assert.match(s, /\[HttpPost\("execute"\)\]/);
  assert.match(s, /Guid clientRequestId/);
  assert.match(s, /\[HttpPost\("preview"\)\]/);
  assert.match(s, /\[HttpPost\("errors"\)\]/);
  assert.match(s, /\[RequestSizeLimit\(8 \* 1024 \* 1024\)\]/);
  assert.match(s, /MaxFileSize = 6 \* 1024 \* 1024/);
  assert.match(s, /SourceKey must remain unique and stable when retrying an import/);
});
test('F05 attendance read/ensure/save/finalize are distinct; finalization and correction are manager-only', () => {
  const s = source('EduOS.App/Controllers/Api/StudentAttendanceController.cs');
  assert.match(s, /\[HttpGet\("roster"\)\]/);
  assert.match(s, /\[HttpPost\("sessions"\)\]/);
  assert.match(s, /\[HttpPost\]\s*public async Task<IActionResult> Save/);
  assert.match(s, /\[HttpPost\("sessions\/\{sessionId:long\}\/finalize"\)\]\s*\[Authorize\(Roles = "TenantAdmin,Principal,VicePrincipal"\)\]/);
  assert.match(s, /\[HttpPost\("\{attendanceId:long\}\/correct"\)\]\s*\[Authorize\(Roles = "TenantAdmin,Principal,VicePrincipal"\)\]/);
});
test('F12 voice report is preview-only route with attendance view permission', () => {
  const s = source('EduOS.App/Controllers/Api/AiVoiceAttendanceController.cs');
  assert.match(s, /RequirePermission\(PermissionConstants.Attendance.View\)/);
  assert.match(s, /\[HttpPost\("attendance\/preview"\)\]/);
  assert.doesNotMatch(s, /\[HttpPost\("attendance\/(?:save|commit|finalize|execute)"\)\]/);
});
test('F01-F12 independent QA does not claim runtime acceptance from static checks', () => {
  const matrix = source('docs/qa/UX09_UX12_STAFF_EFFICIENCY_VERIFICATION.md');
  for (let i = 1; i <= 12; i++) assert.ok(matrix.includes('F' + String(i).padStart(2, '0')));
  assert.match(matrix, /NOT RUN/);
  assert.match(matrix, /authenticated/);
  assert.match(matrix, /p75\/p95/);
});
