const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/admissions/index.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/Admissions/Index.cshtml'), 'utf8');
test('admission pagination offers bounded server-side page sizes and resets page on change', () => {
  assert.match(view, /id="admissionPageSize"/);
  assert.match(js, /\[10, 20, 50\]\.includes\(selected\)/);
  assert.match(js, /state\.pageSize = selected;\s*state\.page = 1;\s*loadApplications\(\)/);
  assert.match(js, /new URLSearchParams\(\{ page: String\(state\.page\), pageSize: String\(state\.pageSize\) \}\)/);
});
test('out-of-order list requests never render stale pages and empty last page is recovered', () => {
  assert.match(js, /requestNumber !== state\.listRequest/);
  assert.match(js, /if \(state\.page > state\.totalPages\)/);
  assert.match(js, /state\.page = state\.totalPages/);
  assert.match(js, /updatePagination\(true\)/);
});
test('pagination status is announced accessibly and localized', () => {
  assert.match(view, /id="pageStatus" role="status" aria-live="polite"/);
  assert.match(js, /Intl\.NumberFormat\(bangla \? 'bn-BD' : 'en-BD'\)/);
  assert.match(js, /loading \|\| state\.page <= 1/);
  assert.match(js, /loading \|\| state\.page >= state\.totalPages/);
});
