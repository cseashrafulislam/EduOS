'use strict';
// Evidence report: validates selected real functional/navigation tests, but
// it NEVER grants a production go decision or manufactures institutional UAT.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../..');
const dir=path.join(root,'qa-artifacts');
const sha=process.env.GITHUB_SHA||'';
const expected=['real-data-entry','real-academic-foundation','real-student-enrollment',
    'real-menu-route-sweep','real-tenantadmin-page-render',
    'real-roleexclusive-page-render','real-privileged-route-coverage',
    'real-route-coverage-gap'];
const read=name=>JSON.parse(fs.readFileSync(path.join(dir,name+'.json'),'utf8'));
function trxSummary(){
    const candidates=[
        path.join(dir,'test-results/codespaces-all.trx'),
        path.join(root,'TestResults/real-data-entry.trx')
    ];
    const file=candidates.find(fs.existsSync);
    assert(file,'Independent SQL acceptance TRX is missing');
    const text=fs.readFileSync(file,'utf8');
    const raw=text.match(/<Counters\s+([^>]+)\/>/);
    assert(raw,'Independent SQL acceptance TRX counters unavailable');
    const counters=Object.fromEntries([...raw[1].matchAll(/(\w+)="(\d+)"/g)]
        .map(([,k,v])=>[k,Number(v)]));
    assert(counters.total>=3&&counters.passed>=3&&
        counters.failed===0&&counters.notExecuted===0,
        'SQL persistence test evidence incomplete or failed');
    return {total:counters.total,passed:counters.passed,failed:counters.failed};
}
function generate(){
    assert(/^[0-9a-f]{40}$/i.test(sha),'Exact 40-character SHA required');
    const proof=Object.fromEntries(expected.map(name=>[name,read(name)]));
    for(const name of expected)assert.equal(proof[name].sha,sha,
        'Wrong-commit acceptance artifact: '+name);
    const menu=proof['real-menu-route-sweep'];
    assert.equal(menu.results.length,13,'13 demo-role sweep missing');
    assert(menu.results.every(r=>r.failed===0&&r.checked===r.passed),
        'Authorized menu GET failures');
    const admin=proof['real-tenantadmin-page-render'];
    assert.equal(admin.failed,0);
    assert(admin.checked>=177,'TenantAdmin navigation unexpectedly shrank');
    assert.equal(admin.passed,admin.checked,'TenantAdmin actual page render incomplete');
    const exclusive=proof['real-roleexclusive-page-render'];
    assert.equal(exclusive.failed,0);
    assert(exclusive.totalDistinctPages>=189,'Authorized role-navigation coverage unexpectedly shrank');
    assert.equal(exclusive.totalDistinctPages,admin.checked+exclusive.newDistinctPages,
        'Role-exclusive route accounting mismatch');
    const privileged=proof['real-privileged-route-coverage'];
    assert.equal(privileged.checked,15);
    assert.equal(privileged.passed,15);
    assert.equal(privileged.deniedPassed,15);
    const coverage=proof['real-route-coverage-gap'];
    assert.equal(coverage.canonicalDeclared,204);
    assert.equal(coverage.canonicalGetCoveredWithPrivilege,204);
    assert.deepEqual(coverage.stillUnexercised,[]);
    assert.equal(coverage.productionReleaseCertified,false);
    const sql=trxSummary();
    const automated=[
        {gate:'Isolated HTTPS, 13-role navigation, privileged negative access',
            status:'PASS',evidence:'8 exact-SHA browser/SQL artifact groups'},
        {gate:'204/204 declared routes GET/render (not CRUD)',
            status:'PASS',evidence:coverage.canonicalGetCovered+' demo-role paths, 15 privileged/optional verification paths (4 overlap), '+coverage.canonicalGetCoveredWithPrivilege+' distinct declared paths'},
        {gate:'Selected Academic + Student/Guardian lifecycle SQL persistence',
            status:'PASS',evidence:'3 independent SQL acceptance tests'}
    ];
    const pending=[
        'All-page real CRUD/approval/export and report UAT',
        'Cross-tenant/campus IDOR, production roles, MFA and session revocation UAT',
        'End-to-end Finance, Inventory, Payroll and GL balance reconciliation',
        '40K import, 5K/10K/50K AP performance and p95/p99 on deployment-sized hardware',
        'Production-like environment rollout, migration/backward compatibility, backup/restore and rollback',
        'Institution user acceptance, accessibility, language and explicit release-owner signatures'
    ];
    const report={sha,branch:'Ashraful',
        scope:'Automated synthetic staging evidence ONLY; production credentials/data never accessed',
        automated,sql,routeMetrics:{tenantAdmin:admin.checked,distinctMenuRoutes:exclusive.totalDistinctPages,
            canonicalGet:coverage.canonicalGetCovered,privileged:privileged.checked,
            distinctDeclared:coverage.canonicalGetCoveredWithPrivilege},
        pending,
        productionReleaseApproved:false,decision:'NO_GO',
        decisionReason:'Institutional acceptance, transaction, performance and operational gates are not independently signed off'};
    fs.mkdirSync(dir,{recursive:true});
    fs.writeFileSync(path.join(dir,'release-readiness.json'),JSON.stringify(report,null,2));
    console.log('PASS: exact-SHA automatic QA evidence, 204 declared navigation routes and',
        sql.passed+'/'+sql.total,'SQL tests.');
    console.log('RELEASE DECISION: NO_GO; pending independent institution/business/operations acceptance.');
    return report;
}
if(require.main===module){
    try{generate();}catch(e){console.error('Release readiness evidence invalid:',e.message);process.exitCode=1;}
}
module.exports={generate,trxSummary};
