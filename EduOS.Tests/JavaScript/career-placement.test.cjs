const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const read = name => fs.readFileSync(path.join(__dirname, '../../', name), 'utf8');
const help = require('../../EduOS.App/wwwroot/js/platform/page-context-help.js');
const opportunityJs = read('EduOS.App/wwwroot/js/alumni/career-opportunities.js');
const placementJs = read('EduOS.App/wwwroot/js/alumni/career-placements.js');
const opportunityView = read('EduOS.App/Views/CareerOpportunities/Index.cshtml');
const placementView = read('EduOS.App/Views/CareerPlacements/Index.cshtml');
const api = read('EduOS.App/Controllers/Api/CareerPlacementApiController.cs');
const service = read('EduOS.Service/Services/Alumni/AlumniCareerApplicationsService.cs');
const migration = read('EduOS.Persistence/Migrations/20261101200000_CareerPlacementWorkflows.cs');

test('Career pages use shared bilingual manuals, scoped authorization, and CSRF', () => {
    for (const [route, view, script] of [
        ['/CareerOpportunities/Index', opportunityView, opportunityJs],
        ['/CareerPlacements/Index', placementView, placementJs]
    ]) {
        for (const lang of ['en-BD', 'bn-BD']) {
            const manual = help.guideFor(route, lang);
            assert.ok(manual && manual.steps.length >= 3, route + ':' + lang);
        }
        assert.match(view, /@Html.AntiForgeryToken\(\)/);
        assert.match(view, /aria-live="polite"/);
        assert.match(script, /RequestVerificationToken: token/);
        assert.match(script, /encodeURIComponent/);
        assert.doesNotMatch(script, /innerHTML|localStorage/);
    }
    assert.match(api, /\[RequireModule\("ALUMNI"\)\]/);
    assert.match(api, /\[AutoValidateAntiforgeryToken\]/);
    assert.match(api, /\[Authorize\(Roles = "TenantAdmin"\)\]/);
});

test('Posting, applications and placements are idempotent, versioned and independently approved', () => {
    assert.match(opportunityJs, /crypto\.randomUUID\(\)/);
    assert.match(placementJs, /crypto\.randomUUID\(\)/);
    assert.match(opportunityJs, /rowVersion: item\.rowVersion/);
    assert.match(placementJs, /rowVersion: item\.rowVersion/);
    assert.match(service, /current\.ProposedByUserId == actor/);
    assert.match(service, /FixedTimeEquals\(current\.RowVersion, version\)/);
    assert.match(service, /IsolationLevel\.Serializable/);
    assert.match(service, /CareerAdminGrant/);
    assert.match(migration, /UX_CareerApplications_OneStudent/);
    assert.match(migration, /UX_CareerApplicationDecisions_Request/);
    assert.match(migration, /UX_CareerOpportunityDecisions_Action/);
});

test('Reports are bounded and never expose student contact exports or unapproved outcomes', () => {
    assert.match(api, /CareerReportAsync\(query, ct\)/);
    assert.match(api, /career-placement-page-/);
    assert.match(service, /COUNT\(CASE WHEN a\.Status = 3 THEN 1 END\) AS Placed/);
    assert.match(service, /OFFSET \{3\} ROWS FETCH NEXT \{4\} ROWS ONLY/);
    assert.doesNotMatch(api, /ContactPhone|ContactEmail|DateOfBirth|Guardian/);
    assert.match(opportunityView, /id="careerOpportunityRows"/);
    assert.match(placementView, /id="careerPlacementRows"/);
});
