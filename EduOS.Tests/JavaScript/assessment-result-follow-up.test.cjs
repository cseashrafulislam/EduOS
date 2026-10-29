'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const js = read('EduOS.App/wwwroot/js/assessment/result-follow-up.js');
const view = read('EduOS.App/Views/AssessmentAdministration/ResultFollowUp.cshtml');

test('follow-up report uses bounded authorized paging and revalidated page export', () => {
    assert.doesNotThrow(() => new Function(js));
    assert.match(js, /results\/follow-up\?/);
    assert.match(js, /data\.publicationId !== report\.publicationId/);
    assert.match(js, /data\.versionNo !== report\.versionNo/);
    assert.match(js, /data\.items\.length > 25/);
    assert.match(js, /state\.reportSeq/);
    assert.match(js, /cache: 'no-store'/);
    assert.doesNotMatch(js, /innerHTML\s*=/);
    assert.match(view, /data-lang=/);
    assert.match(view, /id="followUpRows"/);
});
