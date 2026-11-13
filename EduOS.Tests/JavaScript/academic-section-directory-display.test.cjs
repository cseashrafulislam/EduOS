'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

test('tenant- and campus-scoped Section labels are projected without an N+1 lookup', () => {
    const src = read('EduOS.Service/Services/Students/StudentDirectoryService.cs');
    const history = read('EduOS.Service/Services/Academic/StudentEnrollmentHistoryQueryService.cs');
    assert.match(src, /x.TenantId == tenantId && sectionIds.Contains\(x.Id\)/);
    assert.match(src, /section.CampusId == e.CampusId && section.AcademicLevelId == e.AcademicLevelId/);
    assert.match(history, /s.TenantId == tenant && s.Id == x.AcademicSectionId/);
    assert.match(history, /s.CampusId == x.CampusId && s.AcademicLevelId == x.AcademicLevelId/);
});
test('directory, Student 360 and enrollment history expose separate Batch and Section columns', () => {
    const directory = read('EduOS.App/wwwroot/js/students/index.js');
    const s360 = read('EduOS.App/wwwroot/js/students/student-360.js');
    const enroll = read('EduOS.App/wwwroot/js/students/enrollment-history.js');
    for (const source of [directory, s360, enroll]) new Function(source);
    assert.match(directory, /item.academicSectionName/);
    assert.match(s360, /\['sectionName', 'Section', 'শাখা'\]/);
    assert.match(enroll, /'batchName', 'sectionName'/);
    assert.match(read('EduOS.Core/DTOs/Students/StudentDirectoryDtos.cs'), /AcademicSectionName/);
});
test('Admission batch dropdown is not mislabeled Section', () => {
    const view = read('EduOS.App/Views/Admissions/Index.cshtml');
    const js = read('EduOS.App/wwwroot/js/admissions/index.js');
    assert.match(view, /id="enrollmentBatchId"/);
    assert.doesNotMatch(view, /id="enrollmentSectionId"/);
    assert.match(js, /enrollmentBatchId/);
    assert.doesNotMatch(js, /enrollmentSectionId/);
    assert.match(read('EduOS.App/Views/Attendance/Index.cshtml'), /for="sectionId">.*Academic batch/);
});
