'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const js = read('EduOS.App/wwwroot/js/assessment/result-rescrutiny.js');
const view = read('EduOS.App/Views/AssessmentAdministration/ResultRescrutiny.cshtml');

test('rescrutiny status filter and case audit history use bounded server APIs and safe DOM', () => {
    assert.doesNotThrow(() => new Function(js));
    assert.match(view, /id="rescrutinyStatusFilter"/);
    assert.match(view, /id="rescrutinyHistoryRows"/);
    assert.match(js, /params\.set\('status',/);
    assert.match(js, /\/history'\)/);
    assert.match(js, /state\.historySeq/);
    assert.match(js, /rows\.replaceChildren\(\)/);
    assert.match(js, /\/review'\)/);
    assert.match(js, /state\.reviewSeq/);
    assert.match(js, /rescrutinyReviewEvidence/);
    assert.match(view, /id="rescrutinyReviewMarks"/);
    assert.doesNotMatch(js, /innerHTML\s*=/);
});
