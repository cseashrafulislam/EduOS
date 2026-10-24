const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../../');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/HostelOperations/Index.cshtml'), 'utf8');
const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/hostel-operations/index.js'), 'utf8');
const dto = fs.readFileSync(path.join(root, 'EduOS.Core/DTOs/Hostel/HostelDtos.cs'), 'utf8');
test('finite stay booking fields and authorized existing endpoints are wired', () => {
  assert.match(view, /id="hostelEnd"/);
  assert.match(js, /\/api\/hostel\/bed-availability\?/);
  assert.match(js, /endDate: el\('hostelEnd'\)/);
  assert.match(dto, /class AllocateStudentHostelRequestDto[\s\S]*?DateOnly\? EndDate/);
});
