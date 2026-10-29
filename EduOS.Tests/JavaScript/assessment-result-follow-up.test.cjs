'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const js = read('EduOS.App/wwwroot/js/assessment/result-follow-up.js');
const view = read('EduOS.App/Views/AssessmentAdministration/ResultFollowUp.cshtml');

test('follow-up register has authorized bounded export, counts and revalidated print', () => {
    assert.doesNotThrow(() => new Function(js));
    assert.match(js, /results\\/follow-up\\?/);
    assert.match(js, /results\\/follow-up\\/summary\\?/);
    assert.match(js, /results\\/follow-up\\/export\\?/);
    assert.match(js, /data\\.versionNo === report\\.versionNo/);
    assert.match(js, /data\\.items\\.length > 500/);
    assert.match(js, /state\\.reportSeq/);
    assert.match(js, /cache: 'no-store'/);
    assert.match(js, /function printPage\\(/);
    assert.match(js, /window\\.print\\(\\)/);
    assert.doesNotMatch(js, /innerHTML\\s*=/);
    assert.match(view, /data-lang=/);
    assert.match(view, /id="followUpSummary"/);
    assert.match(view, /id="followUpRows"/);
});
