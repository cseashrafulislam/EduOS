const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
test('both campus editors round-trip online mode and time zone', () => {
    for (const file of ['EduOS.App/wwwroot/js/institution-management/index.js', 'EduOS.App/wwwroot/js/account/campus-setup.js']) {
        const js = read(file);
        assert.match(js, /isOnlineCampus:/);
        assert.match(js, /timeZoneId:/);
        assert.match(js, /campus\.isOnlineCampus/);
        assert.match(js, /campus\.timeZoneId/);
    }
});
test('both campus views expose configuration controls', () => {
    for (const file of ['EduOS.App/Views/InstitutionManagement/Index.cshtml', 'EduOS.App/Views/Account/CampusSetup.cshtml']) {
        const view = read(file);
        assert.match(view, /campusTimeZone/);
        assert.match(view, /Online campus/);
    }
});
test('onboarding API forwards configuration to canonical service', () => {
    const api = read('EduOS.App/Controllers/Api/InstitutionOnboardingController.cs');
    assert.match(api, /IsOnlineCampus = request\.IsOnlineCampus/);
    assert.match(api, /TimeZoneId = request\.TimeZoneId/);
});
