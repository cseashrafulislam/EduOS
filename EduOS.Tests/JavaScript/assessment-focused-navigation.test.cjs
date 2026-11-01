const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const controller = read('EduOS.App/Controllers/AssessmentAdministrationController.cs');
const view = read('EduOS.App/Views/AssessmentAdministration/Index.cshtml');
const layout = read('EduOS.App/Views/Shared/_Layout.cshtml');
const workflow = read('EduOS.App/wwwroot/js/assessment/workflow.js');
const publications = read('EduOS.App/wwwroot/js/assessment/publication-register.js');
const { groupFor, categoryFor, navigationLabelFor } =
    require('../../EduOS.App/wwwroot/js/sidebar-navigation.js');
const { guideFor } = require('../../EduOS.App/wwwroot/js/platform/page-context-help.js');

test('legacy exam Index is preserved while three task-specific workspaces are server-authorized', () => {
    assert.match(controller, /Authorize\(Roles = "TenantAdmin,Principal,VicePrincipal,Teacher,ExamController"\)/);
    assert.match(controller, /RequireModule\("EXAM"\)/);
    assert.match(controller, /public IActionResult Index\(\) => View\(\)/);
    assert.match(controller, /ViewData\["ExamFeature"\] = feature/);
    assert.match(controller, /public IActionResult Marks\(\) => Workflow\("marks"\)/);
    for (const [action, feature] of [['Results','results'],['Publications','publications']]) {
        assert.match(controller, new RegExp(`Authorize\\(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController"\\)\\][\\s\\S]*?public IActionResult ${action}\\(\\) => Workflow\\("${feature}"\\)`));
    }
    for (const [action, feature, category] of [
        ['Marks','marks','operations'],['Results','results','operations'],['Publications','publications','reports']
    ]) {
        assert.ok(view.includes(`feature == "${feature}"`));
        assert.match(layout, new RegExp(`Action: "${action}"`));
        assert.equal(groupFor('academics', '/AssessmentAdministration/' + action), 'assessments');
        assert.equal(categoryFor('academics', 'assessments', '/AssessmentAdministration/' + action), category);
    }
    assert.match(view, /data-active-feature="@feature"/);
    assert.doesNotMatch(layout, /href="\/AssessmentAdministration\/Index"/);
    assert.doesNotMatch(view, /asp-action="MarkReviews"/);
    assert.doesNotMatch(view, /aria-label="Exam workflow help"/);
});

test('role-checked sidebar promotes existing authorized exam operations without duplicate routes', () => {
    for (const [action, category] of [
        ['MarkReviews','operations'],['Setup','settings'],['Attempts','operations'],
        ['ResultRescrutiny','operations'],['ResultFollowUp','reports'],
        ['GradeDistribution','reports'],['AdmitCards','operations']
    ]) {
        assert.match(layout, new RegExp(`Action: "${action}"`));
        assert.equal(categoryFor('academics', 'assessments', '/AssessmentAdministration/' + action), category);
        assert.ok(navigationLabelFor('/AssessmentAdministration/' + action, 'bn-BD'));
        assert.equal(navigationLabelFor('/AssessmentAdministration/' + action, 'en-BD'), null);
    }
    for (const action of ['Marks','Results','Publications','MarkReviews','Setup','Attempts',
        'ResultRescrutiny','ResultFollowUp','GradeDistribution','AdmitCards']) {
        for (const locale of ['en-BD','bn-BD']) {
            const guide = guideFor('/AssessmentAdministration/' + action, locale);
            assert.ok(guide && guide.steps.length >= 3 && guide.example && guide.recovery, action + locale);
        }
    }
});

test('canonical mark, state, publication and historical safeguards survive focused layouts', () => {
    for (const id of ['examFilters','examScopeSearch','examRoster','examSaveMarks','examStateAdvance',
        'examReadinessRows','examGenerate','examPublish','examResults',
        'examHistoryVersion','examHistoryWithdraw','examComparisonRows','examTabulationRows'])
        assert.ok(view.includes(`id="${id}"`), 'required existing exam control ' + id);
    assert.match(workflow, /state\.publishKey \|\|= crypto\.randomUUID\(\)/);
    assert.match(workflow, /if \(window\.prompt\('Publication is immutable\. Type PUBLISH/);
    assert.match(workflow, /Verify publication status before retrying/);
    assert.match(publications, /if \(!reconciled\(version\)\)/);
    assert.match(publications, /Type WITHDRAW to confirm/);
    assert.match(publications, /rowVersion: version\.rowVersion/);
});
