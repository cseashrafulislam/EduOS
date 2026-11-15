'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');
const seed=read('EduOS.Persistence/Seed/DemoInstitutionSeeder.cs');
const program=read('EduOS.App/Program.cs');
const workflow=read('.github/workflows/real-data-entry-qa.yml');
const qa=read('.devcontainer/qa.sh');
const browser=read('tests/browser/real-privileged-route-coverage.cjs');
const coverage=read('scripts/qa/compare-real-route-coverage.cjs');
const research=read('EduOS.App/Controllers/ResearchProposalsController.cs');
const grants=read('EduOS.App/Controllers/ResearchGrantsController.cs');
const accreditation=read('EduOS.App/Controllers/AccreditationController.cs');
test('privileged QA identity has a separate credential, is non-public and runs only in isolated demo SQL',()=>{
    assert(seed.includes('EduOS_Demo_AutoQa')&&seed.includes('EduOS_Demo_Codespaces'));
    assert(seed.includes('platformQaPassword.Length < 24'));
    assert(seed.includes('platformQaPassword, password, StringComparison.Ordinal'));
    assert(seed.includes('demo.platform-qa@eduos.example.invalid'));
    assert(seed.includes('platformRoles.Any'));
    assert(seed.includes('TenantMemberships.IgnoreQueryFilters()'));
    assert(seed.includes('demoPlan.Id'));
    assert(seed.includes('"DEMO_QA_" + moduleCode'));
    assert(!seed.split('public static readonly IReadOnlyList<string> Roles =')[1].split('];')[0].includes('SuperAdmin'));
    assert(program.includes('DemoData:PlatformQaPassword'));
    assert(qa.includes('DemoData__PlatformQaPassword'));
    assert(workflow.includes('DemoData__PlatformQaPassword'));
    assert(qa.includes('QA_PLATFORM_PASSWORD'));
    assert(workflow.includes('QA_PLATFORM_PASSWORD'));
});
test('server enforces the same research/accreditation roles the authorized sidebar shows',()=>{
    for(const c of [research,grants,accreditation]){
        assert(c.includes('[Authorize(Roles = '));
        assert(c.includes('TenantAdmin'));
        assert(c.includes('Teacher'));
        assert(c.includes('Principal'));
        assert(!c.includes('Roles = "Student'));
    }
    assert(research.includes('RequireModule("RESEARCH")'));
    assert(grants.includes('RequireModule("RESEARCH")'));
    assert(accreditation.includes('RequireModule("ACCREDITATION")'));
});
test('browser checks all 15 exact declared routes and student/tenant access denials without mutations',()=>{
    const m=JSON.parse(read('docs/ux/UX_ROUTE_OWNERSHIP_20261114.json'));
    const matched=[...browser.matchAll(/'\/(?:Dashboard\/Admin|Platform[^']+|Subscription[^']+|LegalDocuments\/Index|Account\/PaymentVerification|Research[^']+|Accreditation\/[^']+)'/g)]
        .map(x=>x[0].slice(1,-1));
    const expected=m.lanes.flatMap(x=>x.routes.map(r=>r.path)).filter(x=>matched.includes(x));
    assert.equal(new Set(expected).size,15);
    assert(browser.includes("const studentDenied=await negativeAccess"));
    assert(browser.includes("const tenantDenied=await negativeAccess"));
    assert(browser.includes("method=request.method()"));
    assert(browser.includes("return route.abort('blockedbyclient')"));
    assert(browser.includes("productionReleaseCertified:false"));
    assert(coverage.includes('canonicalGetCoveredWithPrivilege'));
    assert(coverage.includes("assert.equal(report.canonicalGetCoveredWithPrivilege,204"));
});
