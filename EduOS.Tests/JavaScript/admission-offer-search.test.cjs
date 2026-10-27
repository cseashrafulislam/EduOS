'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../../');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');
const search = read('EduOS.App/wwwroot/js/admissions/offers-search.js');
const offers = read('EduOS.App/wwwroot/js/admissions/offers.js');
const core = read('EduOS.App/wwwroot/js/admissions/offers-core.js');
const view = read('EduOS.App/Views/Admissions/Offers.cshtml');
const list = read('EduOS.App/wwwroot/js/admissions/index.js');
const listView = read('EduOS.App/Views/Admissions/Index.cshtml');
const controller = read('EduOS.App/Controllers/Api/AdmissionApplicationsController.cs');

test('admission offer search reuses authorized tenant-scoped paginated application API', () => {
    assert.match(search, /api\('\/api\/admission-applications\?' \+ params\)/);
    assert.match(search, /pageSize = 10/);
    assert.match(search, /token !== latestSearch/);
    assert.match(search, /applicant\.applicationNumber/);
    assert.match(search, /applicant\.campusName/);
    assert.match(controller, /\[Authorize\(Roles = "TenantAdmin,AdmissionOfficer"\)\]/);
    assert.match(view, /id="offerSearchForm"/);
    for (const id of ['offerSearchResults', 'offerSearchPrevious', 'offerSearchNext', 'offerSearchPage'])
        assert.ok(view.includes('id="' + id + '"'), id);
    assert.match(offers, /initializeOfferSearch\(\)/);
    assert.doesNotMatch(search, /innerHTML\s*=/);
});

test('qualified applicants can open offer workspace directly, while other roles cannot see shortcut', () => {
    assert.match(list, /Number\(item\.state\) === 6 && list\.dataset\.canOffer === 'true'/);
    assert.match(list, /\/Admissions\/Offers\?reference=/);
    assert.match(listView, /User\.IsInRole\("TenantAdmin"\)/);
    assert.match(search, /new URLSearchParams\(window\.location\.search\)/);
    assert.match(core, /ctx\.applicant\.applicantName/);
    assert.match(core, /sequence !== loadSequence/);
    assert.match(core, /ctx\.reference !== reference/);
});
