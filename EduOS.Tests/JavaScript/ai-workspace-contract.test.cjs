'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const file = name => fs.readFileSync(path.join(root, name), 'utf8');

test('three AI workspaces are role-scoped and read-only by design', () => {
    const page = file('EduOS.App/Controllers/AiWorkspaceController.cs');
    const api = file('EduOS.App/Controllers/Api/AiWorkspaceApiController.cs');
    const view = file('EduOS.App/Views/AiWorkspace/Index.cshtml');
    assert.match(page, /Authorize\(Roles = "Teacher,Principal,TenantAdmin"\)/);
    assert.match(page, /Authorize\(Roles = "Student"\)/);
    assert.match(page, /Authorize\(Roles = "Principal,TenantAdmin"\)/);
    assert.match(api, /RequireModule\("LMS"\)/);
    assert.match(api, /RequireModule\("ATTENDANCE"\)/);
    assert.match(api, /AutoValidateAntiforgeryToken/);
    assert.match(api, /AiGenerationPolicy/g);
    assert.match(view, /@Html\.AntiForgeryToken\(\)/);
    for (const id of ['aiTeacherForm', 'aiTutorForm', 'aiManagementForm', 'aiManagementMetrics'])
        assert.ok(view.includes('id="' + id + '"'));
});

test('provider does not leak secrets or mutate canonical ERP tables', () => {
    const svc = file('EduOS.Service/Services/AI/EduOsAiWorkspaceService.cs');
    const js = file('EduOS.App/wwwroot/js/ai/workspace.js');
    assert.match(svc, /EduOsAi:Enabled/);
    assert.match(svc, /EduOsAi:ApprovedTenantIds/);
    assert.match(svc, /TenantProviderApproved => Trusted/);
    assert.match(svc, /EduOsAi:StudentTutorEnabled/);
    assert.match(svc, /request\?\.ConsentToExternalAi != true/g);
    assert.match(svc, /if \(!consentToExternalAi\)/);
    assert.match(js, /el\('aiProviderConsent'\)\?\.checked === true/);
    assert.match(js, /consentToExternalAi: consent/);
    assert.match(file('EduOS.App/Views/AiWorkspace/Index.cshtml'), /id="aiProviderConsent"/);
    assert.match(file('EduOS.App/Controllers/Api/AiWorkspaceApiController.cs'), /service.CurrentRoleReady/);
    assert.match(svc, /EduOsAi:ApiKey/);
    assert.match(svc, /EduOsAi:Model/);
    assert.match(svc, /https:\/\/api\.openai\.com\/v1\/responses/);
    assert.match(svc, /store = false/);
    assert.match(svc, /max_output_tokens = 900/);
    assert.doesNotMatch(svc, /SaveChangesAsync|ExecuteSqlRaw|FromSqlRaw|ExecuteUpdate|ExecuteDelete/);
    assert.doesNotMatch(js, /ApiKey|openai\.com|localStorage|innerHTML/);
    assert.match(js, /RequestVerificationToken: token/);
    assert.match(js, /textContent = text/);
    assert.match(js, /sameSession\(\)/);
    assert.match(js, /aiTutorGenerate'\)\.disabled = true/);
});

test('management report reads tenant-scoped finalized evidence and campus permissions', () => {
    const svc = file('EduOS.Service/Services/AI/EduOsAiWorkspaceService.cs');
    assert.match(svc, /db\.CurrentTenantId == current\.TenantId/);
    assert.match(svc, /db\.UserCampusAccesses/);
    assert.match(svc, /session\.IsFinalized/);
    assert.match(svc, /enrollment\.CampusId == campusId/);
    assert.match(svc, /e\.IsCurrent && e\.State == EnrollmentState\.Active/);
    assert.match(svc, /source\.PresentMarks/);
    assert.match(svc, /source\.AbsentMarks/);
    assert.match(svc, /source\.LateMarks/);
    assert.match(svc, /Only aggregate numbers travel to the model/);
    assert.match(svc, /await db\.Employees\.AsNoTracking\(\)/);
    assert.match(svc, /current\.IsInRole\("Student"\)/);
});

test('local-first management questions reuse canonical authorized snapshot, without provider calls', () => {
    const source = file('EduOS.Service/Services/AI/EduOsAiWorkspaceService.cs');
    const api = file('EduOS.App/Controllers/Api/AiWorkspaceApiController.cs');
    const mvc = file('EduOS.App/Controllers/AiWorkspaceController.cs');
    const js = file('EduOS.App/wwwroot/js/ai/workspace.js');
    const planner = file('EduOS.AI/ManagementQuestionPlanner.cs');
    assert.match(source, /ManagementQuestionPlanner\.Parse\(question\)/);
    assert.match(source, /var source = await SnapshotAsync\(campusId, fromDate, toDate, ct\)/);
    assert.match(api, /HttpPost\("management\/ask"\)/);
    assert.match(api, /RequirePermission\(PermissionConstants\.Attendance\.View\)/);
    assert.match(mvc, /RequirePermission\(PermissionConstants\.Attendance\.View\)/);
    assert.match(js, /aiManagementAsk/);
    assert.match(js, /management\/ask/);
    assert.match(planner, /ManagementMetric\.AbsentEntries/);
    assert.doesNotMatch(planner, /HttpClient|ExecuteSql|SaveChanges/);
});

test('navigation and DI wiring include all three new workspaces', () => {
    const layout = file('EduOS.App/Views/Shared/_Layout.cshtml');
    const di = file('EduOS.App/Extensions/ApplicationServiceExtensions.cs');
    const limiter = file('EduOS.App/Extensions/RateLimiterExtensions.cs');
    for (const role of ['Teacher', 'Tutor', 'Management'])
        assert.match(layout, new RegExp('/AiWorkspace/' + role));
    assert.match(di, /AddHttpClient<EduOsAiWorkspaceService>/);
    assert.match(limiter, /AiGenerationPolicy/);
    assert.match(limiter, /CreateOptions\(6, TimeSpan\.FromMinutes\(10\)\)/);
});
