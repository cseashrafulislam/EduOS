const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

test('linked student finance documents use server authorization, no-store and safe export', () => {
  const controller = read('EduOS.App/Controllers/StudentPortalController.cs');
  const api = read('EduOS.App/Controllers/Api/SelfServicePortalController.cs');
  const portal = read('EduOS.App/wwwroot/js/student-portal/index.js');
  const service = read('EduOS.Service/Services/Portals/SelfServicePortalService.cs');
  assert.match(controller, /\[RequireModule\("FINANCE"\)\]/);
  assert.match(controller, /\[ResponseCache\(NoStore = true/);
  assert.match(controller, /CsvCell/);
  assert.match(api, /GetInvoiceDocumentAsync/);
  assert.match(api, /GetReceiptDocumentAsync/);
  assert.match(portal, /studentReference: student, documentReference: item\.reference, kind/);
  assert.match(service, /AuthorizedStudentAsync\(studentReference/);
  assert.match(service, /IgnoreQueryFilters\(\)/);
  assert.match(service, /Receipt ownership or currency requires reconciliation/);
});
