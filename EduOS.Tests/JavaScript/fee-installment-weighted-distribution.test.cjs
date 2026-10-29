const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const js = read('EduOS.App/wwwroot/js/finance/fee-installment-plans.js');
const dto = read('EduOS.Core/DTOs/Finance/StudentFeeInstallmentPlanDtos.cs');
const create = read('EduOS.Service/Services/Finance/FeeBillingInstallmentPlans.cs');
const replacement = read('EduOS.Service/Services/Finance/FeeBillingInstallmentReplacement.cs');
const view = read('EduOS.App/Views/FeeInstallmentPlans/Index.cshtml');
const helper = read('EduOS.Service/Services/Finance/FeeInstallmentDistribution.cs');
const parser = js.match(/function parseMonthWeights\(id, months\) \{[\s\S]*?\n\}/)?.[0];
function parse(value, months) {
    assert.ok(parser, 'monthly share parser must exist');
    const result = vm.runInNewContext(parser + '; parseMonthWeights("shares", ' + months + ')',
        { el: () => ({ value }) });
    return result == null ? null : Array.from(result);
}

test('optional monthly shares parse into exact positive basis points', () => {
    assert.equal(parse('', 3), null);
    assert.deepEqual(parse('50, 25, 25', 3), [5000, 2500, 2500]);
    assert.deepEqual(parse('33.34,33.33,33.33', 3), [3334, 3333, 3333]);
    for (const bad of ['50,50', '50,25,24', '100,0,0', '1e2,0,0', '50,25,25,0']) {
        assert.throws(() => parse(bad, 3), /monthly shares|positive percentage/i);
    }
});

test('creation and replacement use frozen head-conserving distribution and replay checks', () => {
    assert.match(dto, /MonthWeightsBasisPoints/);
    assert.match(create, /FeeInstallmentDistribution\.Allocate/);
    assert.match(create, /FeeInstallmentDistribution\.Matches/);
    assert.match(replacement, /FeeInstallmentDistribution\.Allocate/);
    assert.match(replacement, /FeeInstallmentDistribution\.Matches/);
    assert.match(helper, /cents - allocated\.Sum\(\)/);
    assert.match(helper, /OrderByDescending\(m => remainder\[m\]\)/);
    assert.match(js, /monthWeightsBasisPoints/);
    assert.match(view, /id="feePlanWeights"/);
    assert.match(view, /id="feePlanReplaceWeights"/);
});
