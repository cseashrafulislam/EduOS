const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const js = fs.readFileSync(path.join(__dirname, '../../EduOS.App/wwwroot/js/assessment/admit-cards.js'), 'utf8');
const view = fs.readFileSync(path.join(__dirname, '../../EduOS.App/Views/AssessmentAdministration/AdmitCards.cshtml'), 'utf8');
test('admit card UI never injects student names as HTML and exports only loaded page', () => {
    assert.match(js, /node\.textContent = value/);
    assert.doesNotMatch(js, /innerHTML|insertAdjacentHTML/);
    assert.match(js, /state\.data\.items/);
    assert.match(js, /csvValue/);
    assert.match(js, /URL\.revokeObjectURL/);
});
test('admit card report is print scoped, paginated and protected against stale caching', () => {
    assert.match(js, /cache: 'no-store'/);
    assert.match(js, /credentials: 'same-origin'/);
    assert.match(js, /PageSize: String\(state\.pageSize\)/);
    assert.match(view, /@Html\.AntiForgeryToken\(\)/);
    assert.match(view, /@@media print/);
    assert.match(view, /id="admitCards"/);
});
