// Source-level privacy and stale-report invariants. Run with: node --test.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname,'../..');
const service = fs.readFileSync(path.join(root,'EduOS.Service/Services/Hostel/HostelService.cs'),'utf8');
const script = fs.readFileSync(path.join(root,'EduOS.App/wwwroot/js/hostel-operations/capacity.js'),'utf8');
test('hostel capacity report is tenant/campus scoped and projects no student PII',()=>{
  const body = service.split('GetCapacityAsync(')[1].split('GetEligibleStudentsAsync(')[0];
  assert.match(body,/room\.TenantId == tenant/);
  assert.match(body,/hostel\.TenantId == tenant/);
  assert.match(body,/campus\.TenantId == tenant/);
  assert.match(body,/AllowedCampusIds\(tenant\)/);
  assert.match(body,/Distinct\(\)\.Count\(\)/);
  assert.doesNotMatch(body,/StudentName|MonthlyRent|SecurityDeposit/);
});
test('report invalidates in-flight responses and CSV when filters change',()=>{
  assert.match(script,/requestSerial\s*!==\s*serial/);
  assert.match(script,/hostelCapacitySearch'\)\.addEventListener\('input'/);
  assert.match(script,/hostelCapacityCsv'\)\.disabled\s*=\s*true/);
  assert.match(script,/textContent/);
  assert.doesNotMatch(script,/innerHTML/);
});

test('hostel full capacity export reuses authorized report and excludes PII',()=>{
  const exportSource = fs.readFileSync(path.join(root,'EduOS.Service/Services/Hostel/HostelService.CapacityExport.cs'),'utf8');
  const api = fs.readFileSync(path.join(root,'EduOS.App/Controllers/Api/HostelController.cs'),'utf8');
  const view = fs.readFileSync(path.join(root,'EduOS.App/Views/HostelOperations/Index.cshtml'),'utf8');
  assert.match(exportSource,/GetCapacityAsync\(campusId, 1, 100, search, cancellationToken, attentionOnly\)/);
  assert.match(exportSource,/TotalCount > 1000/);
  assert.match(exportSource,/rows\.Any\(x => !ids\.Add\(x\.RoomId\)\)/);
  assert.match(exportSource,/CsvCell\(row\.HostelName\)/);
  assert.doesNotMatch(exportSource,/StudentName|StudentCode|MonthlyRent|SecurityDeposit/);
  assert.match(api,/HttpGet\("capacity\/export"\)/);
  assert.match(api,/Authorize\(Roles = "TenantAdmin,Principal,HostelWarden"\)/);
  assert.match(script,/\/api\/hostel\/capacity\/export\?/);
  assert.match(script,/query\(false\)/);
  assert.match(script,/requestSerial !== serial/);
  assert.match(view,/id="hostelCapacityCsv"/);
  new Function(script);
});

test('hostel capacity export uses the existing authorized report and current search filter',()=>{
  assert.match(script,/\/api\/hostel\/capacity\/export\?/);
  assert.match(script,/query\(false\)/);
  assert.match(script,/Export all filtered rooms/);
  assert.match(script,/hostelCapacityCampus/);
  assert.match(script,/params\.set\('campusId',campus\)/);
});
