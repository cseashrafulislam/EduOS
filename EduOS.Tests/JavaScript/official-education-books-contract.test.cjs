const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = filename => fs.readFileSync(path.join(root, filename), 'utf8');
const catalog = JSON.parse(read('EduOS.App/wwwroot/data/official-education-sources-2026.json'));
const allowed = new Set(['nctb.gov.bd', 'nctb.portal.gov.bd', 'ntrca.gov.bd']);

test('official education catalog is 27 unique verified-origin publisher landing pages, not mirrored PDFs', () => {
    assert.equal(catalog.schemaVersion, 1);
    assert.equal(catalog.distribution, 'official-source-links-only');
    assert.equal(catalog.entries.length, 27);
    const ids = new Set();
    for (const item of catalog.entries) {
        assert.ok(!ids.has(item.id), 'duplicate ' + item.id);
        ids.add(item.id);
        assert.ok(['NCTB', 'NTRCA'].includes(item.authority));
        assert.equal(item.localCopy, false);
        assert.equal(item.sourceType, 'official-publisher-page');
        assert.ok(item.titleBn && item.titleEn && item.track && item.year);
        const url = new URL(item.sourceUrl);
        assert.equal(url.protocol, 'https:');
        assert.ok(allowed.has(url.hostname), url.hostname);
        assert.equal(url.username, '');
        assert.equal(url.password, '');
        assert.equal(url.port, '');
        assert.ok(!/\.pdf(?:\?|#|$)/i.test(url.pathname));
    }
    const nctb = catalog.entries.filter(x => x.authority === 'NCTB');
    const ntrca = catalog.entries.filter(x => x.authority === 'NTRCA');
    assert.ok(nctb.some(x => /class 1/i.test(x.titleEn)));
    assert.ok(nctb.some(x => /classes 9/i.test(x.titleEn)));
    assert.ok(nctb.some(x => /11/i.test(x.titleEn)));
    assert.ok(nctb.some(x => x.track === 'Dakhil'));
    assert.ok(nctb.some(x => x.track === 'Teacher guides'));
    assert.ok(ntrca.some(x => x.track === 'NTRCA School-2'));
    assert.ok(ntrca.some(x => x.track === 'NTRCA College'));
});

test('Library and LMS share one official source view and remain separately module-authorized', () => {
    const lib = read('EduOS.App/Controllers/LibraryController.cs');
    const learning = read('EduOS.App/Controllers/LearningController.cs');
    const view = read('EduOS.App/Views/Shared/OfficialEducationBooks.cshtml');
    assert.match(lib, /\[RequireModule\("LIBRARY"\)\]/);
    assert.match(learning, /\[RequireModule\("LMS"\)\]/);
    assert.match(lib, /OfficialBooks\(\) => View\("~\/Views\/Shared\/OfficialEducationBooks.cshtml"\)/);
    // The canonical book catalogue belongs to Library; legacy LMS bookmarks redirect there.
    assert.doesNotMatch(learning, /OfficialBooks\(\) => View/);
    assert.match(lib, /\[HttpGet\("\/Learning\/OfficialBooks"\)\]/);
    assert.match(lib, /LegacyLearningOfficialBooks\(\) => RedirectToAction\(nameof\(OfficialBooks\), "Library"\)/);
    assert.match(lib, /Authorize\(Roles = "TenantAdmin,Principal,Librarian,Student,Teacher,Employee,Staff,Guardian,Parent"\)/);
    assert.match(learning, /Authorize\(Roles = "TenantAdmin,Principal,Teacher,Student"\)/);
    assert.match(view, /official-education-sources-2026\.json/);
    assert.match(view, /official-books\.js/);
    assert.match(view, /NOT|not|কপি|পুনঃপ্রকাশ/);
    for (const page of [
        'EduOS.App/Views/Library/Digital.cshtml',
        'EduOS.App/Views/Library/Study.cshtml'
    ]) assert.match(read(page), /asp-controller="Library" asp-action="OfficialBooks"/, page);
    for (const page of [
        'EduOS.App/Views/Learning/Index.cshtml',
        'EduOS.App/Views/Learning/CourseContent.cshtml'
    ]) assert.doesNotMatch(read(page), /asp-controller="Learning" asp-action="OfficialBooks"/, page);
});

test('Source UI rejects unreviewed hosts and does not persist resources or bypass licensing', () => {
    const js = read('EduOS.App/wwwroot/js/education/official-books.js');
    assert.match(js, /allowedHosts\.has\(url\.hostname\)/);
    assert.match(js, /url\.protocol === 'https:'/);
    assert.match(js, /data\.distribution !== 'official-source-links-only'/);
    assert.match(js, /entry\.localCopy === false/);
    assert.match(js, /link\.rel = 'noopener noreferrer'/);
    assert.match(js, /link\.referrerPolicy = 'no-referrer'/);
    assert.match(js, /sessionValid\(\)/);
    assert.doesNotMatch(js, /method:\s*['"]POST['"]/);
    const doc = read('docs/EDUCATION_OFFICIAL_SOURCES.md');
    assert.match(doc, /existing.*LibraryDigitalResourceService|LibraryDigitalResourceService/i);
    assert.match(doc, /No.*copied PDF|Nothing here is a copied PDF/);
});
