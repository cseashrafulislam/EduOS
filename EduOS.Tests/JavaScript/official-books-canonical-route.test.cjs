const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

test('official textbooks have one canonical Library route and an authorized legacy redirect', () => {
    const library = read('EduOS.App/Controllers/LibraryController.cs');
    const learning = read('EduOS.App/Controllers/LearningController.cs');
    const shared = 'View("~/Views/Shared/OfficialEducationBooks.cshtml")';

    assert.match(library, /\[RequireModule\("LIBRARY"\)\]/);
    assert.match(library, /public IActionResult OfficialBooks\(\)/);
    assert.match(library, /\[HttpGet\("\/Learning\/OfficialBooks"\)\]/);
    assert.match(library, /public IActionResult LegacyLearningOfficialBooks\(\) => RedirectToAction\(nameof\(OfficialBooks\), "Library"\)/);
    assert.equal(library.split(shared).length - 1, 1, 'one catalogue view action');
    assert.doesNotMatch(learning, /public IActionResult OfficialBooks\(\)/);
    assert.doesNotMatch(learning, /OfficialEducationBooks\.cshtml/);
});

test('one user-facing Official Books navigation entry exists in Library, not Learning', () => {
    const learningIndex = read('EduOS.App/Views/Learning/Index.cshtml');
    const libraryIndex = read('EduOS.App/Views/Library/Index.cshtml');
    const officialView = read('EduOS.App/Views/Shared/OfficialEducationBooks.cshtml');

    assert.doesNotMatch(learningIndex, /asp-action="OfficialBooks"/);
    assert.match(libraryIndex, /asp-controller="Library" asp-action="OfficialBooks"/);
    assert.match(officialView, /asp-controller="Library" asp-action="Digital"/);
    assert.doesNotMatch(officialView, /ViewContext\.RouteData\.Values\["controller"\]/);
    assert.doesNotMatch(officialView, /"CourseContent", "Learning"/);
});

test('single-catalogue documentation identifies canonical and legacy routes', () => {
    const doc = read('docs/academic/EDUCATION_CONTENT_CATALOGUE.md');
    assert.match(doc, /`\/Library\/OfficialBooks`/);
    assert.match(doc, /`\/Learning\/OfficialBooks`/);
    assert.match(doc, /module entitlement and role checks/);
});
