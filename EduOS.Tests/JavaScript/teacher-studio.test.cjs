const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..', '..');
const source = file => fs.readFileSync(path.join(root, file), 'utf8');

test('teacher resources use scoped LMS file store, approval and legal rights', () => {
    const service = source('EduOS.Service/Services/LMS/TeacherResourceService.cs');
    const context = source('EduOS.Persistence/Context/EduOSDbContext.cs');
    const migration = source('EduOS.Persistence/Migrations/20261109020000_TeacherResourceStudio.cs');
    assert.match(service, /UploadForLmsAsync\(request\.RequestId/);
    assert.match(service, /MyActiveEmployees\(\)/);
    assert.match(service, /MyCampuses\(\)/);
    assert.match(service, /IsVerifiedSafe &&.*MalwareScanStateCode == "Clean"/s);
    assert.match(service, /TeacherResource\.ReviewViewed/);
    assert.match(service, /previous\.StateCode = "Withdrawn"/);
    assert.match(service, /CryptographicOperations\.FixedTimeEquals/);
    assert.match(service, /LicenceEvidence/);
    assert.match(context, /TenantReference<TeacherResource, FileAsset>/);
    assert.match(migration, /IX_TeacherResources_TenantId_PublicId/);
    assert.match(migration, /FK_TeacherResources_Employees_TenantId_AuthorEmployeeId/);
});

test('teacher studio UI remains bilingual, session-bound and safe against untrusted HTML', () => {
    const script = source('EduOS.App/wwwroot/js/lms/teacher-studio.js');
    const view = source('EduOS.App/Views/TeacherStudio/Index.cshtml');
    const controller = source('EduOS.App/Controllers/Api/TeacherResourcesApiController.cs');
    assert.match(script, /authSessionReference/);
    assert.match(script, /authTenantId/);
    assert.match(script, /new AbortController\(\)/);
    assert.match(script, /textContent =/);
    assert.doesNotMatch(script, /\.innerHTML\s*=/);
    assert.match(script, /encodeURIComponent\(item\.reference\)/);
    assert.match(view, /@Html\.AntiForgeryToken\(\)/);
    assert.match(view, /শিক্ষক রিসোর্স স্টুডিও/);
    assert.match(controller, /AutoValidateAntiforgeryToken/);
    assert.match(controller, /\[RequestSizeLimit\(11534336\)\]/);
    assert.match(controller, /CacheControl = "no-store, private"/);
});

test('public teacher catalogue exposes approved HTTPS metadata only, never private files', () => {
    const service = source('EduOS.Service/Services/LMS/TeacherResourceService.cs');
    const migration = source('EduOS.Persistence/Migrations/20261109040000_TeacherPublicSourceLinks.cs');
    const script = source('EduOS.App/wwwroot/js/lms/teacher-public-catalogue.js');
    const view = source('EduOS.App/Views/TeacherStudio/Public.cshtml');
    const controller = source('EduOS.App/Controllers/Api/TeacherResourcesApiController.cs');
    assert.match(service, /ListPublicAsync/);
    assert.match(service, /IgnoreQueryFilters\(\)/);
    assert.match(service, /resource\.ScopeCode == "Public"/);
    assert.match(service, /resource\.StateCode == "Approved"/);
    assert.match(service, /institution\.State == TenantState\.Active/);
    assert.match(service, /LicenceCode == "CreativeCommons"/);
    assert.match(service, /file\.IsVerifiedSafe/);
    assert.match(service, /TeacherPublicResourceDto/);
    assert.match(migration, /CK_TeacherResources_Scope/);
    assert.match(migration, /IX_TeacherResources_ScopeCode_StateCode_PublishedAtUtc/);
    assert.match(view, /উন্মুক্ত শিক্ষক শিক্ষাসামগ্রী/);
    // UX-03 keeps one shared header Help Drawer instead of a duplicate inline popup.
    const { guideFor } = require('../../EduOS.App/wwwroot/js/platform/page-context-help.js');
    assert.equal(guideFor('/TeacherStudio/Public', 'bn-BD').title, 'উন্মুক্ত শিক্ষক শিক্ষাসামগ্রী');
    assert.doesNotMatch(view, /<summary[^>]*>ⓘ<\/summary>/);
    assert.match(script, /authSessionReference/);
    assert.match(script, /encodeURIComponent|URLSearchParams/);
    assert.match(script, /\.textContent\s*=/);
    assert.doesNotMatch(script, /\.innerHTML\s*=/);
    assert.match(script, /url\.protocol !== 'https:'/);
    assert.match(script, /noopener noreferrer/);
    assert.doesNotMatch(script, /\/download/);
    assert.match(controller, /\[HttpGet\("public"\)\]/);
});
