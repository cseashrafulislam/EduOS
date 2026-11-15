'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');
test('release-readiness cannot approve production from synthetic browser/SQL artifacts',()=>{
    const source=read('scripts/qa/release-readiness.cjs');
    assert(source.includes('productionReleaseApproved:false'));
    assert(source.includes("decision:'NO_GO'"));
    assert(source.includes('assert.equal(coverage.canonicalGetCoveredWithPrivilege,204)'));
    assert(source.includes('assert.equal(privileged.deniedPassed,15)'));
    assert(source.includes('assert.deepEqual(coverage.stillUnexercised,[])'));
    assert(source.includes('assert.equal(proof[name].sha,sha'));
    assert(source.includes('counters.failed===0'));
    for(const file of ['.devcontainer/qa.sh','.github/workflows/real-data-entry-qa.yml'])
        assert(read(file).includes('node scripts/qa/release-readiness.cjs'));
});
test('release documentation explicitly requires owner acceptance and protects production data',()=>{
    const doc=read('docs/qa/RELEASE_GO_NO_GO_20261115.md');
    assert(doc.includes('NO_GO'));
    assert(doc.includes('Institution UAT lead'));
    assert(doc.includes('Security and tenancy reviewer'));
    assert(doc.includes('DBA / backup-restore owner'));
    assert(doc.includes('EF1002/EF1003'));
});

test('release readiness reconciles actual role growth without relaxing 204-page gate',()=>{
    const source=read('scripts/qa/release-readiness.cjs');
    assert(source.includes('admin.checked>=177'));
    assert(source.includes('admin.passed,admin.checked'));
    assert(source.includes('exclusive.totalDistinctPages>=189'));
    assert(source.includes('exclusive.totalDistinctPages,admin.checked+exclusive.newDistinctPages'));
    assert(source.includes('routeMetrics:'));
    assert(!source.includes('assert.equal(admin.checked,177)'));
    assert(!source.includes('assert.equal(exclusive.totalDistinctPages,189)'));
});
