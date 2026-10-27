'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const source = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/assessment/workflow.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/AssessmentAdministration/Index.cshtml'), 'utf8');

test('stage action is controller-only and lock needs deliberate confirmation', () => {
    assert.match(view, /@if \(canPublish\)/);
    assert.match(view, /id="examStateCurrent"/);
    assert.match(view, /id="examStateAdvance"[^>]*disabled/);
    assert.match(view, /aria-label="Exam workflow help"/);
    assert.match(source, /if \(!canPublish \|\| state\.busy\) return/);
    assert.match(source, /next\[1\] === 5 && !window\.confirm/);
});
test('transition uses current server rowversion and existing CSRF transport', () => {
    assert.match(source, /encodeURIComponent\(s\.assessmentReference\)/);
    assert.match(source, /const latest = await api\(url\)/);
    assert.match(source, /Number\(latest\.state\) !== Number\(s\.state\)/);
    assert.match(source, /state: next\[1\], rowVersion: latest\.rowVersion/);
    assert.match(source, /RequestVerificationToken: token/);
    assert.match(source, /await loadScopes\(state\.scopePage\)/);
});
