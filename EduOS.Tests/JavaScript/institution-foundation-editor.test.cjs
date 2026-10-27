const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');

test('academic year editor round-trips canonical fields', () => {
    const js = read('EduOS.App/wwwroot/js/account/academic-setup.js');
    const view = read('EduOS.App/Views/Account/AcademicSetup.cshtml');
    for (const field of ['yearCode', 'yearCampusId', 'yearCycleType', 'yearIsActive']) {
        assert.ok(js.includes(field), field);
        assert.ok(view.includes('id="' + field + '"'), field);
    }
    assert.match(js, /setValue\('yearCode', year\.code\)/);
    assert.match(js, /setValue\('yearCampusId', year\.campusId\)/);
    assert.match(js, /setValue\('yearCycleType', year\.cycleType\)/);
    assert.match(js, /rowVersion: editedYearRowVersion/);
});
test('academic term editor supports editing and rowversion replay protection', () => {
    const js = read('EduOS.App/wwwroot/js/account/academic-setup.js');
    const view = read('EduOS.App/Views/Account/AcademicSetup.cshtml');
    for (const field of ['termCode', 'termDisplayOrder', 'termIsCurrent', 'termIsActive']) {
        assert.ok(js.includes(field), field);
        assert.ok(view.includes('id="' + field + '"'), field);
    }
    assert.match(js, /createActionButton\('edit-term'/);
    assert.match(js, /async function editTerm\(id\)/);
    assert.match(js, /rowVersion: editedTermRowVersion/);
    assert.match(js, /editedTermRowVersion = term\.rowVersion/);
    assert.match(js, /editedTermRowVersion = '';/);
});

test('campus archive preview is used by both institution editors', () => {
    const api = read('EduOS.App/Controllers/Api/InstitutionOnboardingController.cs');
    const service = read('EduOS.Service/Services/Tenants/InstitutionFoundationService.cs');
    const onboarding = read('EduOS.App/wwwroot/js/account/campus-setup.js');
    const administration = read('EduOS.App/wwwroot/js/institution-management/index.js');
    assert.match(api, /Authorize\(Roles = "TenantAdmin"\)/);
    assert.match(api, /AutoValidateAntiforgeryToken/);
    assert.match(api, /HttpGet\("campus\/\{id:long\}\/archive-impact"\)/);
    assert.match(service, /GetCampusArchiveBlockersAsync\(tenant, id, token\)/);
    assert.match(onboarding, /archive-impact/);
    assert.match(administration, /archive-impact/);
    assert.match(onboarding, /result\.data\.canArchive/);
    assert.match(administration, /preview\.canArchive/);
});
