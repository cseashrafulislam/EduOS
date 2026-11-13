const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

test('My Work uses canonical server-filtered pagination and role-protected API', () => {
  const api = read('EduOS.App/Controllers/Api/CentralWorkCenterApiController.cs');
  const service = read('EduOS.Service/Services/Platform/CentralWorkCenterService.cs');
  const ui = read('EduOS.App/wwwroot/js/platform/central-work-center.js');
  const view = read('EduOS.App/Views/CentralWorkCenter/Index.cshtml');
  assert.match(api, /Authorize\(Roles = "TenantAdmin,Principal,InventoryManager"\)/);
  assert.match(api, /GetPendingAsync\(page, pageSize, campusId, ct, source, reference, order\)/);
  assert.match(service, /pending = pending\.Where\(x => x\.Kind == kind\)/);
  assert.ok(service.indexOf('pending = pending.Where(x => x.Kind == kind)') <
            service.indexOf('result.Pending.TotalCount = await pending.CountAsync'));
  assert.match(service, /visibleCampuses\.Contains\(r\.CampusId\)/);
  assert.match(service, /CreatedBy != user\.UserId/);
  assert.match(view, /id="cwcSource"/);
  assert.match(view, /id="cwcPageSize"/);
  assert.match(ui, /params\.set\('source', source\.value\)/);
  assert.match(ui, /pageSize: pageSize\.value/);
  assert.match(ui, /pending\.abort\(\)/);
  assert.match(ui, /request !== serial/);
  assert.match(ui, /page = Math\.ceil\(result\.totalCount \/ result\.pageSize\)/);
});
test('Dashboard exposes My Work only to the canonical reviewer role family', () => {
  const dashboard = read('EduOS.App/Views/Dashboard/Index.cshtml');
  assert.match(dashboard, /User\.IsInRole\("InventoryManager"\)/);
  assert.match(dashboard, /asp-controller="CentralWorkCenter" asp-action="Index"/);
});

test('My Work reference search and ordering stay server-side and preserve scoped source counts', () => {
  const api = read('EduOS.App/Controllers/Api/CentralWorkCenterApiController.cs');
  const service = read('EduOS.Service/Services/Platform/CentralWorkCenterService.cs');
  const ui = read('EduOS.App/wwwroot/js/platform/central-work-center.js');
  const view = read('EduOS.App/Views/CentralWorkCenter/Index.cshtml');
  assert.match(view, /id="cwcReference"/);
  assert.match(view, /id="cwcOrder"/);
  assert.match(ui, /params\.set\('reference', reference\.value\.trim\(\)\)/);
  assert.match(ui, /params\.set\('order', order\.value\)/);
  assert.match(api, /string\? reference = null/);
  assert.match(service, /pending = pending\.Where\(x => x\.Reference\.Contains\(reference\)\)/);
  assert.ok(service.indexOf('pending = pending.Where(x => x.Reference.Contains(reference))') <
            service.indexOf('var grouped = await pending.GroupBy'));
  assert.match(service, /pending\.OrderByDescending\(x => x\.SubmittedAtUtc\)/);
  assert.match(service, /reference is \{ Length: > 80 \}/);
  assert.match(service, /order is not \("oldest" or "newest"\)/);
});

test('Work Center compact UI uses canonical campuses and the existing global assistant', () => {
  const dto = read('EduOS.Core/DTOs/Platform/CentralWorkCenterDtos.cs');
  const service = read('EduOS.Service/Services/Platform/CentralWorkCenterService.cs');
  const ui = read('EduOS.App/wwwroot/js/platform/central-work-center.js');
  const view = read('EduOS.App/Views/CentralWorkCenter/Index.cshtml');
  const layout = read('EduOS.App/Views/Shared/_Layout.cshtml');
  assert.match(dto, /List<CentralWorkCampusDto> CampusOptions/);
  assert.match(service, /visibleCampuses\.Contains\(c\.Id\)/);
  assert.match(service, /result\.CampusOptions = await db\.Campuses\.AsNoTracking\(\)/);
  assert.match(view, /<select id="cwcCampus"/);
  assert.doesNotMatch(view, /<h1\b/);
  assert.match(view, /id="cwcClear"/);
  assert.match(ui, /renderCampusOptions\(data\.campusOptions\)/);
  assert.match(ui, /sourceRoutes\[item\.source\] === item\.workspaceUrl/);
  assert.match(layout, /id="pageAssistantOpen"/);
  assert.doesNotMatch(view, /id="pageAssistantVoice"|id="pageAssistantOpen"/);
});
