'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../../');
const fields = require(path.join(root, 'EduOS.App/wwwroot/js/public-admission/fields.js'));
const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/public-admission/index.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/PublicAdmission/Index.cshtml'), 'utf8');

test('public custom fields support all nine canonical types and safe options', () => {
    for (let type = 1; type <= 9; type++) {
        const field = fields.normalizeField({ fieldKey: 'previousInstitution', label: 'Previous',
            dataType: type, isRequired: true, displayOrder: 3, optionsJson: '["One","Two"]' });
        assert.equal(field.key, 'previousInstitution');
        assert.equal(field.type, type);
        assert.equal(field.required, true);
        assert.equal(field.order, 3);
        assert.equal(field.options.length, type === 7 || type === 8 ? 2 : 0);
    }
    assert.equal(fields.normalizeField({ key: 'legacy', type: 1 }), null);
    assert.equal(fields.normalizeField({ fieldKey: 'bad-key', dataType: 1 }), null);
    assert.deepEqual(fields.parseOptions('{bad'), []);
    assert.deepEqual(fields.parseOptions('{"not":"array"}'), []);
    assert.match(js, /fieldContract\.normalizeField/);
    assert.match(js, /fieldContract\.readValue/);
    assert.ok(view.indexOf('/fields.js') < view.indexOf('/index.js'));
});

test('public custom values use canonical boolean, multiselect, datetime and JSON wire formats', () => {
    assert.equal(fields.readValue({ dataset: { fieldType: '6' }, value: 'false' }), 'false');
    assert.equal(fields.readValue({ dataset: { fieldType: '8' }, selectedOptions: [
        { value: 'One' }, { value: 'Two' }] }), '["One","Two"]');
    assert.equal(fields.readValue({ dataset: { fieldType: '8' }, selectedOptions: [] }), null);
    assert.equal(fields.readValue({ dataset: { fieldType: '5' }, value: '2026-10-27T12:30' }),
        new Date('2026-10-27T12:30').toISOString());
    assert.equal(fields.readValue({ dataset: { fieldType: '9' }, value: ' {"a":1} ' }), '{"a":1}');
    assert.equal(fields.readValue({ dataset: { fieldType: '1' }, value: '  School  ' }), 'School');
});

test('public form avoids collecting unsupported standalone previous institution and second address', () => {
    assert.doesNotMatch(view, /id="previousInstitution"|id="permanentAddress"/);
    assert.match(js, /previousInstitution: null/);
    assert.match(js, /permanentAddress: null/);
    assert.match(view, /institution-configured intake fields/);
});

test('completed required document uploads are not re-demanded on partial batch retry', () => {
    assert.match(js, /const uploadedDocumentTypes = new Set\(\)/);
    assert.match(js, /!uploadedDocumentTypes\.has\(x\.dataset\.documentType\)/);
    assert.match(js, /uploadedDocumentTypes\.add\(input\.dataset\.documentType\)/);
    assert.match(js, /uploadedDocumentTypes\.clear\(\)/);
});
