'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

test('enrollment, admission, promotion, readmission and import do not silently lose section identity', () => {
    const workflows = [
        ['EduOS.Service/Services/Academic/AcademicEnrollmentService.cs', 'AcademicSectionId = section?.Id'],
        ['EduOS.Service/Services/Admission/AdmissionEnrollmentService.cs', 'AcademicSectionId = sectionPlacement.SectionId'],
        ['EduOS.Service/Services/Students/StudentPromotionService.cs', 'AcademicSectionId = sectionPlacement.SectionId'],
        ['EduOS.Service/Services/Students/StudentReadmissionService.cs', 'AcademicSectionId=sectionPlacement.SectionId'],
        ['EduOS.Service/Services/Students/StudentImportExecutionService.cs', 'AcademicSectionId = importSections[batch.Id]']
    ];
    for (const [file, expected] of workflows)
        assert.ok(read(file).includes(expected), file);
    const policy = read('EduOS.Service/Services/Academic/AcademicSectionPlacementPolicy.cs');
    assert.match(policy, /Select a specific academic section/);
    assert.match(policy, /x.TenantId == tenantId && x.AcademicBatchId == batchId/);
});

test('attendance roster and routine collision use section identity', () => {
    const attendance = read('EduOS.Service/Services/Attendance/StudentAttendanceService.cs');
    assert.match(attendance, /ResolveSectionAsync/);
    assert.match(attendance, /enrollment\.AcademicSectionId == session\.AcademicSectionId/);
    assert.match(attendance, /GetSectionChoicesAsync/);
    const routines = read('EduOS.Service/Services/Academic/AcademicRoutineService.cs');
    assert.match(routines, /otherOffering\.AcademicSectionId == offering\.AcademicSectionId/);
    assert.match(routines, /Instructor already has class at this time/);
    assert.match(routines, /Room is occupied at this time/);
});

test('section page and linked academic workflows expose separate visible selectors', () => {
    const pairs = [
        ['EduOS.App/Views/AcademicManagement/SectionRegister.cshtml',
            'EduOS.App/wwwroot/js/academic/section-register.js', 'sectionBatch'],
        ['EduOS.App/Views/EnrollmentManagement/Index.cshtml',
            'EduOS.App/wwwroot/js/students/enrollment-management.js', 'enrollmentSection'],
        ['EduOS.App/Views/StudentProgressions/Index.cshtml',
            'EduOS.App/wwwroot/js/students/progressions.js', 'promotionSection'],
        ['EduOS.App/Views/StudentReadmissions/Index.cshtml',
            'EduOS.App/wwwroot/js/students/readmissions.js', 'readmissionSection'],
        ['EduOS.App/Views/Admissions/Offers.cshtml',
            'EduOS.App/wwwroot/js/admissions/offers.js', 'offerAcademicSection'],
        ['EduOS.App/Views/Attendance/Index.cshtml',
            'EduOS.App/wwwroot/js/attendance/index.js', 'attendanceAcademicSection']
    ];
    for (const [view, script, id] of pairs) {
        assert.ok(read(view).includes('id="' + id + '"'), view);
        assert.ok(read(script).includes(id), script);
        const source = read(script);
        const withoutImports = script.endsWith('offers.js')
            ? source.replace(/^import\s+.*?;\s*$/gm, '')
            : source;
        new Function(withoutImports);
    }
    new Function(read('EduOS.App/wwwroot/js/academic/subject-offerings.js'));
    new Function(read('EduOS.App/wwwroot/js/assessment/setup.js'));
    assert.match(read('EduOS.App/Views/Shared/_Layout.cshtml'),
        /Action: "Sections", En: "Academic Sections"/);
});

test('exam offerings carry their Section and published result records remain intact', () => {
    assert.match(read('EduOS.Core/DTOs/Assessment/AssessmentSetupSelectorDtos.cs'), /AcademicSectionName/);
    assert.match(read('EduOS.Service/Services/Assessment/AssessmentAdministrationService.SetupSelectors.cs'),
        /row\.AcademicSectionName/);
    assert.match(read('EduOS.Core/Entities/Assessment/AssessmentEntities.cs'),
        /public class ResultPublication[^]*?AcademicSectionId/);
    const migration = read('EduOS.Persistence/Migrations/20261113010000_AcademicSectionFoundation.cs');
    assert.doesNotMatch(migration, /DropTable\("ResultPublications"\)/);
});
