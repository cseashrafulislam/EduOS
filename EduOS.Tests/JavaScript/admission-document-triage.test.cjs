'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/admissions/index.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/Admissions/Index.cshtml'), 'utf8');
test('document triage includes accessible bilingual filters and live summary', () => {
  for (const id of ['applicantDocumentStatus', 'applicantDocumentVersions', 'applicantDocumentSummary']) {
    assert.match(view, new RegExp('id="' + id + '"'));
    assert.ok(js.includes(id));
  }
  assert.match(view, /aria-live="polite" aria-atomic="true"/);
  assert.match(js, /new Intl\.NumberFormat\(bangla \? 'bn-BD' : 'en-BD'\)/);
});
test('latest versions drive summary and historical versions are never reviewable', () => {
  assert.match(js, /const latest = \[\.\.\.latestVersions\.values\(\)\]/);
  assert.match(js, /const candidates = showHistory \? documents : latest/);
  assert.match(js, /latestVersions\.get\(String\(documentItem\.documentTypeCode \|\| ''\)\)\?\.id === documentItem\.id/);
  assert.match(js, /state\.documentReference !== state\.currentApplication\?\.reference/);
});
test('server-authorized document decisions retain concurrency and CSRF checks', () => {
  assert.match(js, /response\.status === 409 \|\| response\.status === 412/);
  assert.match(js, /apiOptions\('POST', \{/);
  assert.match(js, /rowVersion: button\.dataset\.documentRowVersion/);
  assert.match(js, /options\.headers\.RequestVerificationToken = csrf/);
});

test('document triage supports searchable paged history with bounded navigation', () => {
  for (const id of ['applicantDocumentSearch', 'applicantDocumentPageSize', 'applicantDocumentPrevious', 'applicantDocumentNext', 'applicantDocumentPageStatus']) {
    assert.ok(view.includes('id="' + id + '"'));
    assert.ok(js.includes(id));
  }
  assert.match(js, /matched\.slice\(\(state\.documentPage - 1\) \* pageSize, state\.documentPage \* pageSize\)/);
  assert.match(js, /Math\.min\(totalPages, Math\.max\(1, state\.documentPage\)\)/);
  assert.match(js, /state\.documentPage = 1;\s*setValue\('applicantDocumentSearch', ''\)/);
});
