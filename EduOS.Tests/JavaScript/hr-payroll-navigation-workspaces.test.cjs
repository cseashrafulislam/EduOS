const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const controller = read('EduOS.App/Controllers/HRController.cs');
const view = read('EduOS.App/Views/HR/Index.cshtml');
const hrClient = read('EduOS.App/wwwroot/js/hr/index.js');
const payroll = read('EduOS.App/Views/PayrollOperations/Index.cshtml');
const layout = read('EduOS.App/Views/Shared/_Layout.cshtml');
const { groupFor, categoryFor } = require('../../EduOS.App/wwwroot/js/sidebar-navigation.js');
const { guideFor } = require('../../EduOS.App/wwwroot/js/platform/page-context-help.js');

test('HR directory and leave decisions use separate authorized, localized workspaces', () => {
    assert.match(controller, /Authorize\(Roles = "TenantAdmin,Principal,HR"\)/);
    assert.match(controller, /RequireModule\("HR"\)/);
    assert.match(controller, /Index\(\) => RedirectToAction\(nameof\(Directory\)\)/);
    for (const [action, feature] of [['Directory','directory'],['Leaves','leaves']]) {
        assert.match(controller, new RegExp(`IActionResult ${action}\\(\\) => Feature\\("${feature}"\\)`));
        assert.match(view, new RegExp(`feature == "${feature}"`));
        assert.match(layout, new RegExp(`Path: "/HR/${action}"`));
        assert.equal(groupFor('management', '/HR/' + action), 'people');
        assert.equal(categoryFor('management', 'people', '/HR/' + action), 'operations');
        for (const locale of ['en-BD','bn-BD']) {
            const guide = guideFor('/HR/' + action, locale);
            assert.ok(guide && guide.steps.length >= 3 && guide.example && guide.recovery);
        }
    }
    for (const id of ['hrEmployeeSearch','hrEmployeeRows','hrLeaveSearch','hrLeaveRows'])
        assert.ok(view.includes(`id="${id}"`));
    assert.match(view, /data-active-feature="@feature"/);
    assert.match(hrClient, /dataset\.activeFeature === 'leaves'/);
    assert.doesNotMatch(view, /href="\/HR\/ShiftAssignments"/);
    assert.doesNotMatch(view, /href="\/EmployeeContracts\/Index"/);
});

test('HR and Payroll legacy link forests are promoted to server-role-gated sidebar, not duplicated', () => {
    for (const destination of ['ShiftAssignments', 'CampusAssignments', 'BankAccounts'])
        assert.match(layout, new RegExp(`Path: "/HR/${destination}"`));
    for (const destination of ['EmployeeContracts', 'Recruitment','PayrollSalaryStructures',
        'PayrollSalaryComponents','PayrollSalaryStatement','PayrollReconciliation','PayrollBonuses',
        'PayrollLoanRegister','LoanDisbursement','LoanRecovery','PayrollJournalPosting','PayrollPaymentPosting']) {
        assert.match(layout, new RegExp(`Path: "/${destination}/Index"`));
        assert.equal(groupFor('management', '/' + destination + '/Index'), 'people');
    }
    assert.match(layout, /@if \(User\?\.IsInRole\("TenantAdmin"\) == true\)/);
    assert.match(layout, /@if \(User\?\.IsInRole\("TenantAdmin"\) == true \|\| User\?\.IsInRole\("Principal"\) == true \|\| User\?\.IsInRole\("Accountant"\) == true\)/);
    assert.match(payroll, /id="payrollGenerate"/);
    assert.match(payroll, /id="payrollSlipRows"/);
    assert.doesNotMatch(payroll, /href="\/PayrollSalaryStructures\/Index"/);
    assert.doesNotMatch(payroll, /href="\/HR\/BankAccounts"/);
    assert.match(layout, /href="\/AccountingSetup\/Index"/);
});
