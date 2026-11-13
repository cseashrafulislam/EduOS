'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = filename => fs.readFileSync(path.join(root, filename), 'utf8');

test('Academic Batches and Sections remain separate first-class menu and page titles', () => {
    const sidebar = read('EduOS.App/Views/Shared/_Layout.cshtml');
    const batches = read('EduOS.App/Views/AcademicManagement/Index.cshtml');
    const sections = read('EduOS.App/Views/AcademicManagement/SectionRegister.cshtml');
    assert.match(sidebar, /Action: "Batches", En: "Academic Batches"/);
    assert.match(sidebar, /Action: "Sections", En: "Academic Sections"/);
    assert.match(batches, /"sections" => \(En: "Academic Batches"/);
    assert.doesNotMatch(sidebar + batches, /Academic Batches \(Legacy\)/);
    assert.match(sections, /id="sectionCreateForm"/);
    assert.match(sections, /id="sectionAdvanced"/);
    assert.match(sections, /Manage Batches/);
});

test('Batch registry opens the associated Section editor without re-entering batch', () => {
    const batches = read('EduOS.App/wwwroot/js/academic/batches.js');
    const sections = read('EduOS.App/wwwroot/js/academic/section-register.js');
    new Function(batches);
    new Function(sections);
    assert.match(batches, /AcademicManagement\/Sections\?academicBatchId=/);
    assert.match(batches, /academic-batches-page-/);
    assert.match(sections, /URLSearchParams\(window\.location\.search\)/);
    assert.match(sections, /\/api\/academic-setup\/batches\/.*encodeURIComponent\(desired\)/s);
    assert.match(sections, /async function syncCapacity|function syncCapacity/);
    assert.match(sections, /guard\?\.markClean/);
    assert.doesNotMatch(batches, /No sections found/);
});

test('Section read access supports authorized admission staff without broadening mutations', () => {
    const controller = read('EduOS.App/Controllers/Api/AcademicSectionsController.cs');
    const service = read('EduOS.Service/Services/Academic/AcademicSectionService.cs');
    assert.match(controller, /Authorize\(Roles = "TenantAdmin,Principal,VicePrincipal,AdmissionOfficer"\)/);
    assert.match(controller, /\[HttpPost\]\s*\[Authorize\(Roles = "TenantAdmin"\)\]\s*\[RequireModule\("ACADEMIC"\)\]/);
    assert.match(controller, /\[HttpPut\("\{id:long\}"\)\]\s*\[Authorize\(Roles = "TenantAdmin"\)\]/);
    assert.match(controller, /\[HttpDelete\("\{id:long\}"\)\]\s*\[Authorize\(Roles = "TenantAdmin"\)\]/);
    assert.match(service, /private bool CanRead/);
    assert.match(service, /private bool Allowed => _user\.IsAuthenticated && _user\.IsTenantAdmin/);
    assert.match(service, /x\.TenantId == tenant && !x\.IsDeleted/);
});

test('Generated unused section cleanup preserves every referenced academic and fee record', () => {
    const migration = read('EduOS.Persistence/Migrations/20261113030000_PruneUnusedBatchGeneratedSections.cs');
    assert.match(migration, /IsLegacyDefault = 1/);
    for (const table of ['StudentEnrollments', 'SubjectOfferings', 'AttendanceSessions',
        'ResultPublications', 'FeeStructures'])
        assert.match(migration, new RegExp('NOT EXISTS \\(\\s*SELECT 1 FROM dbo\\.' + table + ' AS x'));
    assert.match(migration, /x\.TenantId = s\.TenantId/);
    assert.match(migration, /x\.AcademicSectionId = s\.Id/);
    assert.doesNotMatch(migration, /DELETE\s+FROM\s+dbo\.AcademicBatches/i);
});

test('Section-aware workspaces and bilingual manuals are parseable', () => {
    for (const name of [
        'EduOS.App/wwwroot/js/academic/subject-offerings.js',
        'EduOS.App/wwwroot/js/students/enrollment-management.js',
        'EduOS.App/wwwroot/js/assessment/setup.js',
        'EduOS.App/wwwroot/js/attendance/index.js',
        'EduOS.App/wwwroot/js/platform/page-context-help.js'
    ]) new Function(read(name));
    const manuals = read('EduOS.App/wwwroot/js/platform/page-context-help.js');
    assert.match(manuals, /'\/academicmanagement\/batches\/index'/);
    assert.match(manuals, /'\/academicmanagement\/sections\/index'/);
    assert.match(manuals, /'Academic Batches', 'একাডেমিক ব্যাচ'/);
    assert.match(manuals, /'Academic Sections', 'একাডেমিক শাখা'/);
});
