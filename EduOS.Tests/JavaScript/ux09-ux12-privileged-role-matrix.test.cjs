'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');
const api = 'EduOS.App/Controllers/Api/';
const specs = Object.freeze([
  { feature:'F02 guardian links', file:'StudentGuardiansController.cs', module:'STUDENT',
    roles:['TenantAdmin'], allowed:['TenantAdmin'], denied:['Teacher','Student','Guardian','Accountant'],
    methods:['students/{studentReference:guid}','existing','students/{studentReference:guid}/links/{linkId:long}/unlink','students/{studentReference:guid}/links/{linkId:long}/reactivate'] },
  { feature:'F04 student import', file:'StudentImportsController.cs', module:'STUDENT',
    roles:['TenantAdmin'], allowed:['TenantAdmin'], denied:['Teacher','Student','Guardian','Accountant'],
    methods:['preview','execute','job-status','cohort-preflight'] },
  { feature:'F05 attendance', file:'StudentAttendanceController.cs', module:'ATTENDANCE',
    roles:['TenantAdmin','Principal','VicePrincipal','Teacher'],
    allowed:['TenantAdmin','Principal','VicePrincipal','Teacher'], denied:['Student','Guardian','Accountant'],
    methods:['roster-page','sessions','sessions/{sessionId:long}/finalize','{attendanceId:long}/correct'] },
  { feature:'F06 finance invoice posting', file:'StudentInvoicePostingController.cs', module:'FINANCE',
    roles:['TenantAdmin','Principal','Accountant'],
    allowed:['TenantAdmin','Principal','Accountant'], denied:['Teacher','Student','Guardian'],
    methods:['{invoiceReference:guid}/post'] },
  { feature:'F12 attendance voice preview', file:'AiVoiceAttendanceController.cs', module:'ATTENDANCE',
    roles:['TenantAdmin','Principal','VicePrincipal'],
    allowed:['TenantAdmin','Principal','VicePrincipal'], denied:['Teacher','Student','Guardian','Accountant'],
    methods:['campuses','attendance/preview'] }
]);
function controllerRoles(source) {
  const header = source.split(/public\s+(?:sealed\s+)?class\s+\w+Controller\b/)[0];
  const match = header.match(/\[Authorize\(Roles\s*=\s*"([^"]+)"\)/);
  return match ? match[1].split(',').map(x => x.trim()) : [];
}
function scoped(source, module) {
  return source.includes('RequireModule("'+module+'")') &&
    /\[ResponseCache\(NoStore = true, Location = ResponseCacheLocation.None\)\]/.test(source) &&
    /\bAutoValidateAntiforgeryToken\b/.test(source) &&
    /\bEnableRateLimiting\("ApiPolicy"\)/.test(source);
}
function routeSource(source, route) {
  return source.includes('"'+route+'"');
}
for (const spec of specs) {
  test(spec.feature + ': role and module contract (static, not authenticated)', () => {
    const source = read(api+spec.file);
    const actual = controllerRoles(source);
    assert.deepEqual(actual, spec.roles, spec.file+' controller roles changed; review method overrides');
    assert.ok(scoped(source, spec.module), spec.file+' missing module, no-store, antiforgery or rate policy');
    for (const role of spec.allowed) assert.ok(actual.includes(role), role+' missing');
    for (const role of spec.denied) assert.ok(!actual.includes(role), role+' unexpectedly in controller role list');
    for (const route of spec.methods) assert.ok(routeSource(source,route), 'missing '+route);
  });
}
test('F05 finalization and correction are explicitly more restrictive than teacher roster access', () => {
  const source=read(api+'StudentAttendanceController.cs');
  for (const route of ['sessions/{sessionId:long}/finalize','{attendanceId:long}/correct']) {
    const i=source.indexOf('[HttpPost("'+route+'")]');
    assert.ok(i>=0);
    const after=source.slice(i,i+250);
    assert.match(after,/\[Authorize\(Roles = "TenantAdmin,Principal,VicePrincipal"\)\]/);
    assert.doesNotMatch(after,/Roles = "[^"]*Teacher/);
  }
});
test('F06 posting is not authorized to accountant even though read is', () => {
  const source=read(api+'StudentInvoicePostingController.cs');
  assert.match(source,/\[HttpPost\("\{invoiceReference:guid\}\/post"\), Authorize\(Roles = "TenantAdmin,Principal"\)\]/);
});
test('F12 voice remains preview-only and binds tenant and campus at query boundary', () => {
  const source=read(api+'AiVoiceAttendanceController.cs');
  assert.match(source,/RequirePermission\(PermissionConstants.Attendance.View\)/);
  assert.match(source,/x\.TenantId == current\.TenantId/);
  assert.match(source,/a\.CampusId == c\.Id/);
  assert.doesNotMatch(source,/\[HttpPost\("attendance\/(?:save|finalize|execute|commit)"\)\]/);
});
test('static matrix must not masquerade as browser, SQL or latency acceptance', () => {
  const doc=read('docs/qa/UX09_UX12_STAFF_EFFICIENCY_VERIFICATION.md');
  assert.match(doc,/NOT RUN/);
  assert.match(doc,/p75\/p95/);
  assert.match(doc,/authenticated/i);
});
