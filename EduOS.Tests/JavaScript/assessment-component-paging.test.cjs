'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

test('component marks page uses server-side scope paging and bounded search', () => {
    const js = read('EduOS.App/wwwroot/js/assessment/component-marks.js');
    const view = read('EduOS.App/Views/AssessmentAdministration/ComponentMarks.cshtml');
    assert.doesNotThrow(() => new Function(js));
    assert.match(js, /pageSize: String\(state\.pageSize\)/);
    assert.match(js, /state\.total = roster\.totalCount/);
    assert.match(js, /confirmDiscard\(\)/);
    assert.match(js, /component-marks-page-/);
    assert.match(view, /id="cmSearch"[^>]*maxlength="100"/);
    assert.match(view, /id="cmPrev"/);
    assert.match(view, /id="cmNext"/);
    assert.match(view, /Component marks guide/);
    assert.doesNotMatch(js, /innerHTML\s*=/);
});

test('component marks retain CSRF, rowversion, and current-page CSV', () => {
    const js = read('EduOS.App/wwwroot/js/assessment/component-marks.js');
    assert.match(js, /RequestVerificationToken: token/);
    assert.match(js, /rowVersion: tr\.dataset\.version/);
    assert.match(js, /state\.roster\.students\.map/);
    assert.match(js, /state\.page \+ '\.csv'/);
});
