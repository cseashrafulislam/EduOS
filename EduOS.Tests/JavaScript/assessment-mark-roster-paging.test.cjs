'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/assessment/workflow.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/AssessmentAdministration/Index.cshtml'), 'utf8');

test('marks register pages on server and exports only the current authorized page', () => {
    assert.match(js, /mark-roster\/page\?' \+ q/);
    assert.match(js, /pageSize: String\(state\.rosterSize\)/);
    assert.match(js, /state\.rosterTotal = data\.totalCount/);
    assert.match(js, /kind === 'marks'/);
    assert.match(view, /id="examRosterSearchForm"/);
    assert.match(view, /id="examRosterPrev"/);
    assert.match(view, /id="examRosterNext"/);
    assert.match(view, /Page CSV/);
});

test('unsaved marks prompt before page navigation and rowversion is submitted', () => {
    assert.match(js, /hasUnsavedMarks\(\)/);
    assert.match(js, /Discard unsaved marks before changing the register/);
    assert.match(js, /rowVersion: tr\.dataset\.rowVersion \|\| null/);
    assert.match(js, /RequestVerificationToken: token/);
});
