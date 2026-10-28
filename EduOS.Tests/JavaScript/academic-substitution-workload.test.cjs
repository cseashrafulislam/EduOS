const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

test('substitute workload report uses manager-only, scoped SQL grouping and bounded UI CSV', () => {
    const service = read('EduOS.Service/Services/Academic/AcademicInstructionService.cs');
    const api = read('EduOS.App/Controllers/Api/AcademicInstructionController.cs');
    const view = read('EduOS.App/Views/AcademicInstruction/Index.cshtml');
    const js = read('EduOS.App/wwwroot/js/academic/instruction.js');
    assert.match(service, /GetSubstitutionWorkloadAsync/);
    assert.match(service, /if \(!CanManage\(\)\) return Denied<PagedResult<SubstitutionWorkloadDto>>/);
    assert.match(service, /GetAuthorizedSubstitutionQueryAsync\(fromDate, toDate, academicBatchId, ct\)/);
    assert.match(service, /GroupBy\(x => x.SubstituteEmployeeId\)/);
    assert.match(service, /matchingEmployeeIds\.Contains\(x.SubstituteEmployeeId\)/);
    assert.match(service, /"cancelled" => groups.OrderByDescending/);
    assert.match(api, /GetSubstitutionWorkloadFilteredAsync/);
    assert.match(service, /Skip\(\(int\)offset\)\.Take\(pageSize\)/);
    assert.match(api, /\[HttpGet\("substitutions\/workload"\)\]/);
    assert.match(api, /\[Authorize\(Roles = "TenantAdmin,Principal,VicePrincipal"\)\]/);
    for (const id of ['workloadLoad','workloadRows','workloadPage','workloadCsv','workloadPrevious','workloadNext','workloadSearch','workloadSort'])
        assert.ok(view.includes('id="' + id + '"'), id + ' must exist');
    assert.match(js, /workloadRequestSeq/);
    assert.match(js, /query\.set\('search', search\)/);
    assert.match(js, /get\('workloadSearch'\)\.value\.trim\(\) !== search/);
    assert.match(js, /get\('workloadSort'\)\.addEventListener\('change', resetWorkload\)/);
    assert.doesNotThrow(() => new Function(js));
    assert.match(js, /state\.workloadRows\.map/);
    assert.match(js, /if \(\/\^\[=\+@/);
});
