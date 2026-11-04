'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const view = read('EduOS.App/Views/AssessmentAdministration/ExamReports.cshtml');
const js = read('EduOS.App/wwwroot/js/assessment/exam-reports.js');
const controller = read('EduOS.App/Controllers/Api/AssessmentAdministrationApiController.cs');
const help = read('EduOS.App/wwwroot/js/platform/page-context-help.js');
const layout = read('EduOS.App/Views/Shared/_Layout.cshtml');
const sidebar = read('EduOS.App/wwwroot/js/sidebar-navigation.js');

test('all nineteen legacy school report options are visible in four task groups', () => {
    const ids = [...view.matchAll(/<option value="(\d+)"/g)]
        .map(match => Number(match[1])).sort((a, b) => a - b);
    assert.deepEqual(ids, Array.from({ length: 19 }, (_, i) => i + 1));
    assert.equal((view.match(/<optgroup /g) || []).length, 4);
    assert.match(view, /value="15" disabled/);
    assert.match(view, /Academic Transcript \(issue register\)/);
    assert.match(view, /data-lang=/);
});

test('reports use scoped saved versions, safe DOM cells and guarded CSV/print', () => {
    assert.doesNotThrow(() => new Function(js));
    assert.match(js, /results\/publications\?/);
    assert.match(js, /results\/reports\/scopes\?/);
    assert.match(js, /results\/reports\/subjects\?/);
    assert.match(js, /results\/reports\?/);
    assert.match(js, /credentials: 'same-origin', cache: 'no-store'/);
    assert.match(js, /data\.publicationId !== Number\(publication\)/);
    assert.match(js, /report\.versionNo !== current\.versionNo/);
    assert.match(js, /JSON\.stringify\(report\.items\) !== JSON\.stringify\(current\.items\)/);
    assert.match(js, /if \(\/\^\\s\*\[=\+@\\-\]\//);
    assert.match(js, /td\.textContent =/);
    assert.match(js, /window\.print\(\)/);
    assert.match(view, /Page CSV/);
    assert.doesNotMatch(js, /innerHTML\s*=/);
});

test('private report readback fails closed on changed institution or session', () => {
    assert.match(js, /document\.body\?\.dataset\.authSessionReference === session\.reference/);
    assert.match(js, /document\.body\?\.dataset\.authTenantId === session\.tenant/);
    assert.match(js, /EduOSSessionTransitionPending/);
    assert.match(js, /await window\.EduOSVerifySessionContext\(\)/);
    assert.match(js, /shield\.hidden !== true/);
    assert.match(js, /response\.headers\.get\('X-EduOS-Session-Reference'\) === session\.reference/);
    assert.match(js, /response\.headers\.get\('X-EduOS-Tenant-Id'\) === session\.tenant/);
    assert.match(js, /if \(path\.startsWith\('results\/reports'\) && !responseMatches\(response\)\)/);
    assert.match(js, /for \(const el of root\.querySelectorAll\('button,select,input'\)\) el\.disabled = true/);
    assert.match(controller, /Response\.Headers\["X-EduOS-Session-Reference"\]/);
    assert.match(controller, /Response\.Headers\["X-EduOS-Tenant-Id"\]/);
    assert.match(controller, /ReportResult\(await _service\.SearchExamReportAsync/);
});

test('new report endpoint, sidebar and reviewed bilingual page manual are discoverable', () => {
    assert.match(controller, /\[HttpGet\("results\/reports"\)\]/);
    assert.match(controller, /\[HttpGet\("results\/reports\/subjects"\)\]/);
    assert.match(controller, /\[HttpGet\("results\/reports\/scopes"\)\]/);
    assert.match(layout, /Action: "ExamReports"/);
    assert.match(sidebar, /\/AssessmentAdministration\/ExamReports/);
    assert.match(help, /'\/assessmentadministration\/examreports\/index'/);
    assert.match(help, /practical-only failure/);
});
