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
test('Admission enrollment requires Section and keeps Academic Batch optional', () => {
    const view = read('EduOS.App/Views/Admissions/Index.cshtml');
    const js = read('EduOS.App/wwwroot/js/admissions/index.js');
    assert.match(view, /id="enrollmentSectionId"[^>]*required/);
    assert.match(view, /for="enrollmentSectionId">Academic section/);
    assert.doesNotMatch(view, /id="enrollmentBatchId"/);
    assert.match(js, /academicSectionId:\s*positiveInteger\(valueOf\('enrollmentSectionId'\)\)/);
    assert.match(js, /academicBatchId:\s*state\.enrollmentOfferBatchId\s*\?\?\s*null/);
    const attendance = read('EduOS.App/Views/Attendance/Index.cshtml');
    assert.match(attendance, /for="attendanceAcademicSection"/);
    assert.match(attendance, /id="attendanceAcademicSection"/);
});
