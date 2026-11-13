'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const base = path.resolve(__dirname, '../..');
const read = relative => fs.readFileSync(path.join(base, relative), 'utf8');

test('separate section master and backward-compatible academic batch keys are retained', () => {
    const model = read('EduOS.Core/Entities/Academic/AcademicEntities.cs');
    for (const token of ['class AcademicBatch : BaseTenantEntity', 'class AcademicSection : BaseTenantEntity',
        'class StudentEnrollment : BaseTenantEntity', 'class SubjectOffering : BaseTenantEntity',
        'class AttendanceSession : BaseTenantEntity'])
        assert.ok(model.includes(token), token);
    assert.match(model, /public class AcademicSection[^]*?CampusId[^]*?AcademicLevelId/);
    const sectionModel = model.split('public class AcademicSection : BaseTenantEntity')[1]
        .split('public class Room : BaseTenantEntity')[0];
    assert.doesNotMatch(sectionModel, /AcademicBatchId/);
    assert.match(read('EduOS.Persistence/Migrations/20261113050000_DecoupleSectionsFromBatches.cs'),
        /DropColumn\("AcademicBatchId", "AcademicSections"\)/);
    assert.doesNotMatch(model, /IsLegacyDefault/);
    assert.match(model, /public class StudentEnrollment[^]*?AcademicBatchId[^]*?AcademicSectionId/);
    const migration = read('EduOS.Persistence/Migrations/20261113010000_AcademicSectionFoundation.cs');
    assert.match(migration, /CreateTable\(name: "AcademicSections"/);
    assert.match(migration, /FK_AcademicSections_AcademicBatches_TenantId_AcademicBatchId/);
    const removal = read('EduOS.Persistence/Migrations/20261113040000_RemoveAcademicSectionLegacyDefault.cs');
    assert.match(removal, /DropColumn\(/);
    assert.match(removal, /DropIndex\(/);
    assert.doesNotMatch(removal, /THROW 51042/);
    assert.doesNotMatch(read('EduOS.Persistence/Context/EduOSDbContext.cs'), /IsLegacyDefault/);
    assert.doesNotMatch(read('EduOS.Persistence/Migrations/EduOSDbContextModelSnapshot.AcademicSections.cs'), /IsLegacyDefault/);
    assert.doesNotMatch(migration, /DropTable\("AcademicBatches"\)/);
});

test('section UI uses a dedicated tenant-scoped API, safe DOM text and concurrency tokens', () => {
    const view = read('EduOS.App/Views/AcademicManagement/SectionRegister.cshtml');
    const js = read('EduOS.App/wwwroot/js/academic/section-register.js');
    new Function(js);
    const api = read('EduOS.App/Controllers/Api/AcademicSectionsController.cs');
    const service = read('EduOS.Service/Services/Academic/AcademicSectionService.cs');
    assert.match(view, /id="sectionCreateForm"/);
    assert.match(view, /id="sectionCampus"/);
    assert.match(view, /id="sectionLevel"/);
    assert.doesNotMatch(view, /id="sectionBatch"/);
    assert.match(api, /Authorize\(Roles = "TenantAdmin"\)/);
    assert.match(api, /AutoValidateAntiforgeryToken/);
    assert.match(service, /x.TenantId == tenant/);
    assert.match(service, /FixedTimeEquals/);
    assert.match(service, /ClientRequestId == request.ClientRequestId/);
    assert.doesNotMatch(service, /IsLegacyDefault/);
    assert.match(js, /textContent = value/);
    assert.match(js, /RequestVerificationToken: token/);
    assert.match(js, /state.requestId/);
    assert.match(js, /rowVersion/);
});
