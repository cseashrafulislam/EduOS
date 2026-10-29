'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../../');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');
const js = read('EduOS.App/wwwroot/js/learning/index.js');
const view = read('EduOS.App/Views/Learning/Index.cshtml');
const api = read('EduOS.App/Controllers/Api/LmsWorkflowController.cs');
const service = read('EduOS.Service/Services/LMS/LmsWorkflowService.LearnerProgress.cs');

test('student learning dashboard is paged and tenant-owned', () => {
 assert.match(api, /\[HttpGet\("my-progress"\)\]/);
 assert.match(api, /\[Authorize\(Roles = "Student"\)\]/);
 assert.match(service, /enrollment\.StudentId == studentId/);
 assert.match(service, /course\.TenantId == tenant/);
 assert.match(service, /pageSize is < 1 or > 25/);
 assert.match(service, /GroupBy\(x => x\.CourseEnrollmentId\)/);
 assert.match(js, /api\/lms\/my-progress\?page=/);
 for(const id of ['learningMyProgressRows','learningMyProgressPrev','learningMyProgressNext','learningMyProgressExport'])
  assert.ok(view.includes('id="'+id+'"'), id);
});

test('lesson progress is read back before allowing completion and exports are CSV-safe', () => {
 assert.match(api, /lesson-progress/);
 assert.match(service, /await CanViewAsync\(course, ct\)/);
 assert.match(service, /x\.IsPublished && !x\.IsDeleted/);
 assert.match(js, /state\.progressLoaded=true/);
 assert.match(js, /if\(!state\.progressLoaded\)action\.textContent='Progress unavailable'/);
 assert.match(js, /if\(\/\^\[=\+@/);
 assert.doesNotMatch(js, /innerHTML\s*=/);
 new Function(js);
});


test('archived learner progress is explicitly scoped and shown as historical evidence', () => {
 assert.match(api, /includeArchived = false/);
 assert.match(api, /GetMyCourseProgressAsync\(page, pageSize, ct, includeArchived\)/);
 assert.match(service, /HistoricalCourseIdsQuery\(studentId\)/);
 assert.match(service, /historicalIds\.Contains\(x\.Course\.Id\)/);
 assert.match(service, /IsArchived = !x\.Course\.IsActive/);
 assert.match(js, /my-progress\?page=.*includeArchived=true/);
 assert.match(js, /state\.myProgressPage=1/);
 assert.match(js, /x\.isArchived\?'Yes':'No'/);
 assert.match(view, /Include archived completed courses in courses and progress/);
});
