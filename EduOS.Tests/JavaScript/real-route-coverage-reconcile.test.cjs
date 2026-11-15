'use strict';
const assert=require('node:assert/strict');
const test=require('node:test');
const {normalizeRoute,reconcile}=require('../../scripts/qa/compare-real-route-coverage.cjs');
const sha='a'.repeat(40);
const item=(path)=>({path,passed:true});
test('route normalization accepts only local absolute paths and removes query',()=>{
    assert.equal(normalizeRoute('/AcademicManagement/Years?tab=1'),'/academicmanagement/years');
    assert.equal(normalizeRoute('/AcademicManagement/Years/'),'/academicmanagement/years');
    assert.equal(normalizeRoute('//evil.example/'),null);
    assert.equal(normalizeRoute('https://evil.example/'),null);
});
test('reconcile distinguishes missing entitlement routes from failed real navigation',()=>{
    const manifest={branch:'Ashraful',counts:{canonicalRoutes:3},lanes:[
        {id:'A',routes:[{path:'/A',menuGroup:'one'},{path:'/B',menuGroup:'one'}]},
        {id:'B',routes:[{path:'/C',menuGroup:'two'}]}
    ]};
    const menu={sha,results:Array.from({length:13},(_,i)=>({
        role:'DemoRole'+i,failed:0,checked:1,pages:[item('/A?x=1')]
    }))};
    const admin={sha,checked:100,failed:0,pages:[item('/A')]};
    const other={sha,failed:0,pages:[item('/B')]};
    const report=reconcile(manifest,menu,admin,other,sha);
    assert.equal(report.canonicalDeclared,3);
    assert.equal(report.canonicalGetCovered,1);
    assert.equal(report.canonicalRendered,2);
    assert.deepEqual(report.unobservedCanonical,['/B','/C']);
    assert.equal(report.productionReleaseCertified,false);
    assert.equal(report.lanes.A.declared,2);
    assert.equal(report.lanes.A.menuGet,1);
    assert.equal(report.lanes.A.rendered,2);
});
test('failed role navigation or mismatched SHA cannot produce a coverage report',()=>{
    const manifest={branch:'Ashraful',counts:{canonicalRoutes:1},
        lanes:[{id:'A',routes:[{path:'/A',menuGroup:'g'}]}]};
    const menu={sha,results:Array.from({length:13},(_,i)=>({
        role:'Role'+i,failed:0,checked:1,pages:[item('/A')]
    }))};
    const admin={sha,checked:100,failed:0,pages:[item('/A')]};
    const other={sha,failed:0,pages:[]};
    assert.throws(()=>reconcile(manifest,menu,admin,other,'b'.repeat(40)));
    menu.results[1].pages[0].passed=false;
    assert.throws(()=>reconcile(manifest,menu,admin,other,sha));
});
