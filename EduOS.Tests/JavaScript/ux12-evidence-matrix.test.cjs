'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');

const FEATURES = Object.freeze(Array.from({length:12},(_,i)=>'F'+String(i+1).padStart(2,'0')));
const GATES = Object.freeze(['source','js','dotnet','sql','chrome','api','security','performance']);
const STATUSES = new Set(['PASS','FAIL','BLOCKED','NOT RUN']);
const SHA = /^[a-f0-9]{40}$/;
const SAFE_ROUTE = /^\/[A-Za-z0-9/_-]*$/;
const FORBIDDEN = /(?:password|access.?token|refresh.?token|authorization|cookie|student.?name|phone|national.?id|nid|email|raw.?prompt|transcript|secret)/i;

function validate(input) {
  const errors=[];
  if (!input || typeof input!=='object' || Array.isArray(input)) return ['Evidence object required'];
  if (!SHA.test(input.gitSha||'')) errors.push('Exact gitSha required');
  if (!Array.isArray(input.cases)||!input.cases.length) return [...errors,'Nonempty cases required'];
  const seen=new Set();
  for (const [i,row] of input.cases.entries()) {
    const p='cases['+i+']';
    if (!row||typeof row!=='object'||Array.isArray(row)) {errors.push(p+': object required');continue;}
    if (!FEATURES.includes(row.feature)) errors.push(p+': unknown feature');
    for (const key of ['route','role','tenantAlias','campusAlias','locale','device','scenario']) {
      if (typeof row[key]!=='string'||!row[key].trim()) errors.push(p+': '+key+' required');
    }
    if (!SAFE_ROUTE.test(row.route||'')||(row.route||'').includes('//')||(row.route||'').split('/').includes('..')) errors.push(p+': unsafe route');
    if (!['bn-BD','en-BD'].includes(row.locale)) errors.push(p+': locale');
    if (!/^(360|390|768|1280|1440)(?:px)?$/.test(row.device||'')) errors.push(p+': viewport');
    if (!GATES.includes(row.gate)) errors.push(p+': gate');
    if (!STATUSES.has(row.status)) errors.push(p+': status');
    if (['PASS','FAIL'].includes(row.status)) {
      const e=row.evidence;
      if (!e||typeof e!=='object'||!/^[a-z0-9][a-z0-9_.-]{2,120}$/i.test(e.testId||'')||!/^(?:https:\/\/github\.com\/[^\s]+\/actions\/runs\/\d+|local:[A-Za-z0-9_./-]+)$/.test(e.reference||'')) errors.push(p+': executable evidence required');
      if (!e||e.gitSha!==input.gitSha) errors.push(p+': evidence SHA mismatch');
    }
    const identity=['feature','route','role','tenantAlias','campusAlias','locale','device','scenario','gate'].map(k=>row[k]).join('|');
    if (seen.has(identity)) errors.push(p+': duplicate case');
    seen.add(identity);
    for (const key of Object.keys(row)) if (FORBIDDEN.test(key)) errors.push(p+': sensitive field');
    if (row.evidence&&typeof row.evidence==='object') for(const key of Object.keys(row.evidence)) if(FORBIDDEN.test(key)) errors.push(p+': sensitive evidence');
  }
  return errors;
}
function summarize(input) {
  const errors=validate(input);
  if(errors.length) return {valid:false,complete:false,errors};
  const byFeature=Object.fromEntries(FEATURES.map(f=>[f,{PASS:0,FAIL:0,BLOCKED:0,'NOT RUN':0}]));
  const byGate=Object.fromEntries(GATES.map(g=>[g,{PASS:0,FAIL:0,BLOCKED:0,'NOT RUN':0}]));
  for(const c of input.cases) {byFeature[c.feature][c.status]++;byGate[c.gate][c.status]++;}
  // This is coverage bookkeeping only, NEVER production acceptance certification.
  return {valid:true,complete:false,gitSha:input.gitSha,byFeature,byGate,caseCount:input.cases.length};
}
const SHA_EXAMPLE='a'.repeat(40);
const sample=(changes={})=>({feature:'F01',route:'/Student/QuickEntry',role:'Admin',tenantAlias:'tenant-a',campusAlias:'campus-a',locale:'bn-BD',device:'390',scenario:'default',gate:'js',status:'PASS',evidence:{testId:'ux12.quick-entry',reference:'local:node-test',gitSha:SHA_EXAMPLE},...changes});
const pack=cases=>({gitSha:SHA_EXAMPLE,cases});
test('valid evidence remains unverified acceptance',()=>{const s=summarize(pack([sample()]));assert.equal(s.valid,true);assert.equal(s.complete,false);});
test('all twelve features tracked',()=>assert.deepEqual(Object.keys(summarize(pack([sample()])).byFeature),FEATURES));
test('all eight gates tracked',()=>assert.deepEqual(Object.keys(summarize(pack([sample()])).byGate),GATES));
test('invalid SHA rejected',()=>assert.match(validate({gitSha:'unknown',cases:[sample()]}).join(' '),/gitSha/));
test('different evidence SHA rejected',()=>assert.match(validate(pack([sample({evidence:{testId:'ux12.quick-entry',reference:'local:node-test',gitSha:'b'.repeat(40)}})])).join(' '),/SHA mismatch/));
test('PASS needs evidence',()=>assert.match(validate(pack([sample({evidence:null})])).join(' '),/executable evidence/));
test('FAIL needs evidence',()=>assert.match(validate(pack([sample({status:'FAIL',evidence:null})])).join(' '),/executable evidence/));
test('blocked and not run are representable',()=>{for(const status of ['BLOCKED','NOT RUN']) assert.deepEqual(validate(pack([sample({status,evidence:undefined})])),[]);});
test('duplicate scenario rejected',()=>assert.match(validate(pack([sample(),sample()])).join(' '),/duplicate/));
test('different tenants and campuses tracked',()=>assert.deepEqual(validate(pack([sample(),sample({tenantAlias:'tenant-b',campusAlias:'campus-b'})])),[]));
test('locale and viewport tracked independently',()=>assert.deepEqual(validate(pack([sample(),sample({locale:'en-BD',device:'360'})])),[]));
test('unsafe route rejected',()=>assert.match(validate(pack([sample({route:'//evil'})])).join(' '),/unsafe route/));
test('sensitive fields rejected',()=>assert.match(validate(pack([sample({accessToken:'unsafe'})])).join(' '),/sensitive/));
test('unsupported feature and gate rejected',()=>assert.ok(validate(pack([sample({feature:'F13',gate:'guess'})])).length>=2));
test('source test cannot infer security or performance',()=>{const s=summarize(pack([sample()]));assert.equal(s.byGate.security.PASS,0);assert.equal(s.byGate.performance.PASS,0);});
test('even all feature/gate labels cannot certify acceptance',()=>{const cases=FEATURES.map((feature,i)=>sample({feature,gate:GATES[i%GATES.length],scenario:'case-'+i}));assert.equal(summarize(pack(cases)).complete,false);});
module.exports={FEATURES,GATES,validate,summarize};
