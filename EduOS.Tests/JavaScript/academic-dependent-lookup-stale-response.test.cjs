const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

test('academic programme department lookup preserves current editor, typed search and user selection', () => {
    const js = read('EduOS.App/wwwroot/js/academic/programmes.js');
    assert.match(js, /editorId !== el\('programmeEditId'\)\.value/);
    assert.match(js, /search !== el\('programmeDepartmentSearch'\)\.value\.trim\(\)/);
    assert.match(js, /currentSelection !== selectionAtRequest/);
    assert.match(js, /renderDepartments\(result, Number\(currentSelection\) \|\| null\)/);
    assert.match(js, /ticket !== editSequence \|\| !sameSession\(\) \|\| editGuard\?\.isDirty\(\)/);
});
test('academic track programme lookup rejects stale search, selection and session', () => {
    const js = read('EduOS.App/wwwroot/js/academic/tracks.js');
    assert.match(js, /button\.disabled \|\| createSaving \|\| !sameSession\(\)/);
    assert.match(js, /ticket !== programmeLookupSequence \|\| !sameSession\(\)/);
    assert.match(js, /search !== value\('trackProgrammeLookup'\)/);
    assert.match(js, /selected !== el\('trackCreateProgram'\)\.value \|\| createSaving/);
    assert.match(js, /if \(ticket === programmeLookupSequence\) button\.disabled = false/);
});
