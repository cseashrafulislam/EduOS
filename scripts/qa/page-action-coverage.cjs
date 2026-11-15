'use strict';
// 204-route functional QA inventory. No action is certified by its UI label.
// Only explicit real-browser + SQL evidence may mark an operation TESTED.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const {actions}=require('../../tests/browser/page-action-probe.cjs');
const root=path.resolve(__dirname,'../..'),artifactDir=path.join(root,'qa-artifacts');
const inputFiles=['real-tenantadmin-page-render','real-roleexclusive-page-render',
    'real-privileged-route-coverage'];
const confirmedExports={
    '/studentimports/index':{excel:'student-xlsx-template-200-zip-mime'},
    '/students/index':{excel:'student-directory-csv-paged-200-mime'},
    '/accountingreports/index':{report:'tenant-scoped-trial-balance-json-200'}
};
const confirmedCreation={
    '/academicmanagement/years':['real-data-entry','year-form-save'],
    '/academicmanagement/terms':['real-data-entry','term-form-save'],
    '/academicmanagement/classes':['real-academic-foundation','class-ui-create-reload'],
    '/academicmanagement/subjects':['real-academic-foundation','subject-ui-create-reload'],
    '/academicmanagement/sections':['real-academic-foundation','section-ui-create-reload'],
    '/academicmanagement/batches':['real-student-enrollment','batch-create'],
    '/academicmanagement/curricula':['real-student-enrollment','curriculum-create'],
    '/academicmanagement/offerings':['real-student-enrollment','section-linked-offering'],
    '/students/create':['real-student-enrollment','student-plus-guardian-create']
};
function normalized(raw){
    assert(typeof raw==='string'&&raw.startsWith('/')&&!raw.startsWith('//'),'Bad declared route');
    const url=new URL(raw,'https://qa.eduos.test:7243');
    assert.equal(url.origin,'https://qa.eduos.test:7243','Foreign route');
    return url.pathname.replace(/\/+$/,'').toLowerCase()||'/';
}
function compile(manifest, sources, proofs, sha){
    assert(/^[a-f0-9]{40}$/i.test(sha),'Immutable commit required');
    assert.equal(manifest.branch,'Ashraful');
    const routes=manifest.lanes.flatMap(l=>l.routes.map(r=>({path:r.path,lane:l.id})));
    assert.equal(routes.length,204,'Canonical route inventory is not 204');
    const witness=new Map();
    for(const [name,artifact] of Object.entries(sources)){
        assert.equal(artifact.sha,sha,'Wrong-SHA browser action census: '+name);
        for(const page of artifact.pages||[]){
            if(!page.passed)continue;
            const key=normalized(page.path);
            const sampled=page.actionProbe;
            assert(sampled&&sampled.sampled,'Missing action census for '+key+' in '+name);
            assert.deepEqual(Object.keys(sampled.hints).sort(),[...actions].sort(),
                'Incomplete eight-action census: '+key);
            const row=witness.get(key)||{sources:[],hints:Object.fromEntries(
                actions.map(a=>[a,{present:0,visible:0}]))};
            row.sources.push(name);
            for(const action of actions){
                const h=sampled.hints[action];
                assert(Number.isInteger(h.present)&&h.present>=0&&Number.isInteger(h.visible)&&
                    h.visible>=0&&h.visible<=h.present,'Invalid hint count');
                row.hints[action].present=Math.max(row.hints[action].present,h.present);
                row.hints[action].visible=Math.max(row.hints[action].visible,h.visible);
            }
            witness.set(key,row);
        }
    }
    const unique=new Set(routes.map(r=>normalized(r.path)));
    assert.equal(unique.size,204,'Duplicate canonical route');
    const rows=routes.map(r=>{
        const key=normalized(r.path),seen=witness.get(key);
        assert(seen,'No actual Chromium GET/render witness for '+r.path);
        return {path:r.path,lane:r.lane,rendered:true,observedBy:seen.sources,
            actions:Object.fromEntries(actions.map(action=>{
                const known=action==='create'?confirmedCreation[key]:null;
                let proof=null;
                const exportProof=confirmedExports[key]?.[action];
                if(exportProof){
                    const exported=proofs['real-export-report-qa'];
                    assert(exported&&exported.sha===sha&&Array.isArray(exported.checks)&&
                        exported.checks.some(x=>x.route.toLowerCase()===key&&
                            x.action===action&&x.proof===exportProof)&&
                        exported.denials.length===3&&exported.denials.every(x=>x.passed),
                        'Stale or missing actual export/report proof '+r.path);
                    proof={artifact:'real-export-report-qa',check:exportProof};
                }
                if(known){
                    const [artifact,check]=known,p=proofs[artifact];
                    assert(p&&p.sha===sha&&p.checks.includes(check),
                        'Stale or missing real CREATE proof '+r.path);
                    proof={artifact,check};
                }
                return [action,{
                    applicability:seen.hints[action].present?'UI_CANDIDATE':'NEEDS_REQUIREMENTS_REVIEW',
                    controlsPresent:seen.hints[action].present,
                    controlsVisible:seen.hints[action].visible,
                    verified:!!proof,
                    result:proof?'SELECTED_E2E_PASS':'NOT_FUNCTIONALLY_TESTED',
                    ...(proof?{proof}:{})
                }];
            }))};
    });
    const total=rows.length*actions.length,verified=rows.reduce((n,row)=>n+
        actions.filter(a=>row.actions[a].verified).length,0);
    return {sha,scope:'204-page UI-control census + selected real action proof; NOT every-page CRUD/export UAT',
        pages:rows.length,actionTypes:actions,actionCells:total,
        verifiedCells:verified,unverifiedCells:total-verified,
        signoffRequired:true,productionReleaseApproved:false,
        note:'No UI control is treated as successful Create/Edit/Delete/Approve/Print/Excel/PDF/Report without separate real transaction/download evidence',
        rows};
}
function run(){
    const sha=process.env.GITHUB_SHA||'';
    const manifest=JSON.parse(fs.readFileSync(path.join(root,
        'docs/ux/UX_ROUTE_OWNERSHIP_20261114.json'),'utf8'));
    const sources=Object.fromEntries(inputFiles.map(n=>[n,JSON.parse(
        fs.readFileSync(path.join(artifactDir,n+'.json'),'utf8'))]));
    const proofNames=[...new Set([...Object.values(confirmedCreation).map(p=>p[0]),
        'real-export-report-qa'])];
    const proofs=Object.fromEntries(proofNames.map(n=>[n,JSON.parse(
        fs.readFileSync(path.join(artifactDir,n+'.json'),'utf8'))]));
    const output=compile(manifest,sources,proofs,sha);
    fs.mkdirSync(artifactDir,{recursive:true});
    fs.writeFileSync(path.join(artifactDir,'page-action-coverage.json'),JSON.stringify(output,null,2));
    const escape=s=>'"'+String(s).replace(/"/g,'""')+'"';
    const csv=['Page,Lane,Action,Applicability,ControlCount,VisibleControls,E2EStatus,Proof'];
    for(const row of output.rows)for(const action of actions){
        const entry=row.actions[action];
        csv.push([row.path,row.lane,action,entry.applicability,
            entry.controlsPresent,entry.controlsVisible,entry.result,
            entry.proof?entry.proof.artifact+':'+entry.proof.check:''].map(escape).join(','));
    }
    fs.writeFileSync(path.join(artifactDir,'page-action-coverage.csv'),csv.join('\n')+'\n');
    assert.equal(output.pages,204);
    assert.equal(output.actionCells,1632);
    console.log('Page action QA census:',output.pages,'pages,',output.actionCells,'cells;',
        output.verifiedCells,'selected real actions verified;',
        output.unverifiedCells,'require functional evidence or N/A approval.');
    console.log('No inferred CRUD or export is certified by its label.');
}
if(require.main===module){
    try{run();}catch(e){console.error('Page action inventory failed:',e.message);process.exitCode=1;}
}
module.exports={compile,normalized,confirmedCreation,confirmedExports};
