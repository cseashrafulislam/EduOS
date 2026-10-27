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
