// Source-level privacy and stale-report invariants. Run with: node --test.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname,'../../..');
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
  assert.match(script,/requestSerial!==serial/);
  assert.match(script,/hostelCapacitySearch'\)\.addEventListener\('input'/);
  assert.match(script,/hostelCapacityCsv'\)\.disabled=true/);
  assert.match(script,/textContent/);
  assert.doesNotMatch(script,/innerHTML/);
});
