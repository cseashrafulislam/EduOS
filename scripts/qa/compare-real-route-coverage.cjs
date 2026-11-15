'use strict';
// Reconcile declared canonical sidebar inventory with real, authenticated
// route GET and Chromium evidence from the SAME disposable QA commit.
// Missing role-entitlement pages are recorded as NOT EXERCISED, not failed.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../..');
const artifactDir=path.join(root,'qa-artifacts');
const manifestFile=path.join(root,'docs/ux/UX_ROUTE_OWNERSHIP_20261114.json');
const names=['real-menu-route-sweep','real-tenantadmin-page-render','real-roleexclusive-page-render'];

function normalizeRoute(input){
    if(typeof input!=='string'||!input.startsWith('/')||input.startsWith('//'))return null;
    const url=new URL(input,'https://qa.eduos.test:7243');
    if(url.origin!=='https://qa.eduos.test:7243')return null;
    return url.pathname.replace(/\/+$/,'').toLowerCase()||'/';
}
function reconcile(manifest,menu,admin,other,sha){
    assert.equal(manifest.branch,'Ashraful');
    assert.equal(menu.sha,sha);
    assert.equal(admin.sha,sha);
    assert.equal(other.sha,sha);
    assert.equal(menu.results.length,13,'Expected actual checks for 13 demo roles');
    assert.equal(admin.failed,0,'TenantAdmin Chromium has failing pages');
    assert.equal(other.failed,0,'Other-role Chromium has failing pages');
    assert(admin.checked>=100,'Unexpectedly small actual TenantAdmin menu');
    const declared=[];
    for(const lane of manifest.lanes){
        for(const route of lane.routes){
            const key=normalizeRoute(route.path);
            assert(key,'Invalid canonical route in manifest');
            declared.push({path:route.path,key,lane:lane.id,group:route.menuGroup});
        }
    }
    assert.equal(declared.length,manifest.counts.canonicalRoutes,
        'Canonical route manifest count disagrees with its lanes');
    const canonical=new Set(declared.map(x=>x.key));
    assert.equal(canonical.size,declared.length,'Duplicate canonical route in manifest');
    const visited=new Set();
    for(const role of menu.results){
        assert.equal(role.failed,0,'Failed authorized role-menu GET for '+role.role);
        assert.equal(role.checked,role.pages.length,'Incomplete route results for '+role.role);
        for(const item of role.pages){
            assert(item.passed,'Failed GET for '+role.role+' '+item.path);
            const key=normalizeRoute(item.path);
            if(key)visited.add(key);
        }
    }
    const rendered=new Set();
    for(const item of [...admin.pages,...other.pages]){
        assert(item.passed,'Failed real Chromium route '+item.path);
        const key=normalizeRoute(item.path);
        if(key)rendered.add(key);
    }
    const groups={};
    for(const route of declared){
        const group=groups[route.lane]??={declared:0,menuGet:0,rendered:0,unobserved:[]};
        group.declared++;
        if(visited.has(route.key))group.menuGet++;
        if(rendered.has(route.key))group.rendered++;
        if(!visited.has(route.key))group.unobserved.push(route.path);
    }
    return {
        sha,scope:'authenticated demo-role navigation GET and page render only; NOT CRUD or complete institutional UAT',
        canonicalDeclared:declared.length,uniqueRoleMenuGet:visited.size,
        uniqueChromiumRendered:rendered.size,
        canonicalGetCovered:declared.filter(x=>visited.has(x.key)).length,
        canonicalRendered:declared.filter(x=>rendered.has(x.key)).length,
        unobservedCanonical:declared.filter(x=>!visited.has(x.key)).map(x=>x.path),
        observedOutsideManifest:[...visited].filter(x=>!canonical.has(x)).sort(),
        lanes:groups,
        productionReleaseCertified:false
    };
}
function run(){
    const sha=process.env.GITHUB_SHA||'';
    assert(/^[0-9a-f]{40}$/i.test(sha),'Require exact 40-character commit SHA');
    const manifest=JSON.parse(fs.readFileSync(manifestFile,'utf8'));
    const [menu,admin,other]=names.map(name=>
        JSON.parse(fs.readFileSync(path.join(artifactDir,name+'.json'),'utf8')));
    const report=reconcile(manifest,menu,admin,other,sha);
    fs.mkdirSync(artifactDir,{recursive:true});
    fs.writeFileSync(path.join(artifactDir,'real-route-coverage-gap.json'),
        JSON.stringify(report,null,2));
    console.log('Real QA menu coverage:',report.canonicalGetCovered+'/'+report.canonicalDeclared,
        'declared routes exercised by demo-role GET; distinct Chromium routes',
        report.uniqueChromiumRendered,'; unobserved',report.unobservedCanonical.length);
    console.log('These are navigation/render checks, not full functional CRUD/UAT certification.');
}
if(require.main===module){
    try{run();}catch(error){console.error('Real-route coverage reconciliation failed:',error.message);process.exitCode=1;}
}
module.exports={normalizeRoute,reconcile};
