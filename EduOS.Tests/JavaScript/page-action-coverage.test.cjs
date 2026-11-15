'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../..');
const {actions}=require('../../tests/browser/page-action-probe.cjs');
const {compile}=require('../../scripts/qa/page-action-coverage.cjs');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');
const sha='a'.repeat(40);
const manifest=JSON.parse(read('docs/ux/UX_ROUTE_OWNERSHIP_20261114.json'));
function seed(){
    const hints=Object.fromEntries(actions.map(a=>[a,{present:1,visible:1}]));
    const rows=manifest.lanes.flatMap(l=>l.routes.map(r=>({
        path:r.path,passed:true,actionProbe:{sampled:true,hints}
    })));
    const proofs=Object.fromEntries(['real-data-entry','real-academic-foundation',
        'real-student-enrollment'].map(x=>[x,{sha,checks:[
            'year-form-save','term-form-save','class-ui-create-reload',
            'subject-ui-create-reload','section-ui-create-reload','batch-create',
            'curriculum-create','section-linked-offering','student-plus-guardian-create'
        ]}]));
    proofs['real-export-report-qa']={sha,checks:[
        {route:'/StudentImports/Index',action:'excel',proof:'student-xlsx-template-200-zip-mime'},
        {route:'/Students/Index',action:'excel',proof:'student-directory-csv-paged-200-mime'},
        {route:'/AccountingReports/Index',action:'report',proof:'tenant-scoped-trial-balance-json-200'}
    ],denials:[{passed:true},{passed:true},{passed:true}]};
    proofs['real-academic-year-lifecycle']={sha,checks:[
        'year-isolated-create','year-ui-edit-saved-and-reloaded','year-ui-archive-and-reloaded']};
    const printPages=manifest.lanes.flatMap(l=>l.routes.map(r=>r.path))
        .filter(p=>p==='/hostel/guardian-stays/print'||p==='/hostel/my-history/print')
        .map(path=>({path,passed:true,actionProbe:{sampled:true,hints},
            printProof:{buttonInvoked:true,pdfValid:true,controlsHidden:true}}));
    return {proofs,sources:{'demo-render':{sha,pages:rows},
        'real-roleexclusive-page-render':{sha,pages:printPages}}};
}
test('all 204 canonical pages have eight separate business-action cells',()=>{
    const {sources,proofs}=seed(),out=compile(manifest,sources,proofs,sha);
    assert.equal(out.pages,204);
    assert.equal(out.actionCells,1632);
    assert.equal(out.verifiedCells,16,'Only evidenced create/edit/archive/export/report/print count');
    assert.equal(out.unverifiedCells,1616);
    assert.equal(out.productionReleaseApproved,false);
    assert(out.rows.every(r=>actions.every(a=>r.actions[a].result)));
});
test('UI button presence never certifies untested approve or native PDF export',()=>{
    const {sources,proofs}=seed(),out=compile(manifest,sources,proofs,sha);
    for(const row of out.rows)for(const action of actions.filter(a=>!['create','edit','delete','print','excel','report'].includes(a))){
        assert.equal(row.actions[action].result,'NOT_FUNCTIONALLY_TESTED');
        assert.equal(row.actions[action].verified,false);
        assert.equal(row.actions[action].controlsPresent,1);
    }
});
test('missing actual page evidence, unsafe cross-SHA evidence or omitted proof fails closed',()=>{
    const {sources,proofs}=seed();
    sources['demo-render'].pages.pop();
    assert.throws(()=>compile(manifest,sources,proofs,sha),/No actual Chromium/);
    const clean=seed();
    clean.proofs['real-data-entry'].sha='b'.repeat(40);
    assert.throws(()=>compile(manifest,clean.sources,clean.proofs,sha),/Stale or missing/);
});
test('all three browser routes collect read-only candidate evidence without submitting forms',()=>{
    const observer=read('tests/browser/page-action-probe.cjs');
    assert(observer.includes("getClientRects()"));
    assert(!observer.includes("element.click("));
    for(const file of ['real-tenantadmin-page-render','real-roleexclusive-page-render',
        'real-privileged-route-coverage']){
        const c=read('tests/browser/'+file+'.cjs');
        assert(c.includes("probePageActions("),file);
        assert(c.includes('actionProbe'),file);
    }
    const qa=read('.devcontainer/qa.sh'),wf=read('.github/workflows/real-data-entry-qa.yml');
    assert(qa.includes('node scripts/qa/page-action-coverage.cjs'));
    assert(wf.includes('node scripts/qa/page-action-coverage.cjs'));
    assert(wf.includes('qa-artifacts/page-action-coverage.csv'));
    assert(read('tests/browser/real-roleexclusive-page-render.cjs').includes('page.pdf({format:'));
    assert(read('tests/browser/real-roleexclusive-page-render.cjs').includes('printRoster'));
    assert(wf.includes('node tests/browser/real-export-report-qa.cjs'));
    assert(wf.includes('node tests/browser/real-academic-year-lifecycle.cjs'));
    assert(qa.includes('node tests/browser/real-export-report-qa.cjs'));
    assert(read('tests/browser/real-export-report-qa.cjs').includes('student-directory-csv-paged-200-mime'));
});
