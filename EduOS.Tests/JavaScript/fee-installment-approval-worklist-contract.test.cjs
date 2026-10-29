const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const api = read('EduOS.App/Controllers/Api/FeeInstallmentPlansController.cs');
const service = read('EduOS.Service/Services/Finance/FeeBillingInstallmentPlans.cs');
const view = read('EduOS.App/Views/FeeInstallmentPlans/Index.cshtml');
const js = read('EduOS.App/wwwroot/js/finance/fee-installment-plans.js');

test('approval queue is bounded and server filtered by actor, state and campus', () => {
    assert.match(api, /myApprovalQueue/);
    assert.match(api, /RequireModule\("FINANCE"\)/);
    assert.match(service, /myApprovalQueue && !_user\.IsTenantAdmin/);
    assert.match(service, /x\.Plan\.PreparedByUserId != actor && !x\.SubmittedByActor/);
    assert.match(service, /visible\.CountAsync\(ct\)/);
    assert.match(service, /Skip\(skip\)\.Take\(pageSize\)/);
    assert.match(service, /StudentCode\.StartsWith\(term\)/);
    assert.match(service, /grants\.Contains\(plan\.CampusId\)/);
});

test('register filters and review button use server supplied eligibility', () => {
    for (const id of ['feePlanFilterSearch','feePlanState','feePlanCampus','feePlanMyQueue'])
        assert.ok(view.includes('id="' + id + '"'));
    assert.match(js, /query\.set\('myApprovalQueue', 'true'\)/);
    assert.match(js, /item\.canReviewForApproval \? 'Review' : 'View'/);
    assert.match(js, /!selected\.canReviewForApproval/);
    assert.match(js, /x\.canReviewForApproval \? 'Available for independent review'/);
});
