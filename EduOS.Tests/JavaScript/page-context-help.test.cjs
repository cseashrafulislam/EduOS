const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const { guideFor } = require('../../EduOS.App/wwwroot/js/platform/page-context-help.js');

test('page header help is contextual, bilingual and falls back safely', () => {
    assert.match(guideFor('/Dashboard/Index', false), /institution activity/);
    assert.match(guideFor('/Dashboard/Index', true), /প্রতিষ্ঠানের/);
    assert.match(guideFor('/Account/CampusSetup', false), /campuses/);
    assert.match(guideFor('/Account/AcademicSetup', false), /academic year/);
    assert.match(guideFor('/workspace', false), /appearance/);
    assert.match(guideFor('/unknown/<script>', false), /permitted for your role/);
    assert.doesNotMatch(guideFor('/unknown/<script>', false), /script/);
});
test('shared help is user initiated, accessible and uses text-only rendering', () => {
    const layout = fs.readFileSync(path.join(root, 'EduOS.App/Views/Shared/_Layout.cshtml'), 'utf8');
    const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/platform/page-context-help.js'), 'utf8');
    assert.match(layout, /id="pageContextHelp"/);
    assert.match(layout, /<summary[^>]*aria-label=/);
    assert.match(layout, /page-context-help\.js/);
    assert.match(js, /text\.textContent = guideFor/);
    assert.match(js, /event\.key !== 'Escape'/);
    assert.match(js, /event\.target/);
    assert.doesNotMatch(js, /\.innerHTML/);
});
