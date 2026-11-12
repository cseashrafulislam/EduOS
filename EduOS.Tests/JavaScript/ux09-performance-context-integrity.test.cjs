'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const {validate,probe,compare}=require('./ux09-readonly-performance-probe.cjs');
const base={baseUrl:'https://qa.example.test',samples:5,timeoutMs:1000,routes:[{id:'student-list',path:'/api/students'}]};
const ctx={gitSha:'sha-a',role:'Teacher',tenantAlias:'tenant-a',campusAlias:'campus-a',locale:'bn-BD',device:'390px',metric:'sequential-client-GET-ms',sampleCountPerRoute:5};
const row=(id='students',extra={})=>({id,samples:5,success:5,failures:0,statuses:{200:5},p75Ms:20,p95Ms:25,...extra});
const report=(routes=[row()],context=ctx)=>({schema:1,context,routes});
test('reject noncanonical path tricks before any network request',()=>{
 for(const path of ['/api/%252e%252e/secret','/api/%2f%2fhost','/api/..','/api/.','/api/\\admin','/api/students?tenant=other','/api/students#secret','/api/students%0a'])
  assert.throws(()=>validate({...base,routes:[{id:'x',path}]}));
});
test('reject duplicate IDs and invalid sample/time limits',()=>{
 assert.throws(()=>validate({...base,routes:[...base.routes,...base.routes]}));
 for(const samples of [0,4,101,1.5,NaN]) assert.throws(()=>validate({...base,samples}));
 for(const timeoutMs of [0,99,30001,1.5,NaN]) assert.throws(()=>validate({...base,timeoutMs}));
});
test('probe is sequential, read-only and does not follow redirects',async()=>{
 const seen=[];let active=0,max=0,now=0;
 const fetchImpl=async(url,options)=>{active++;max=Math.max(max,active);seen.push({url:String(url),options});now+=10;active--;return {status:200,body:{cancel:async()=>{}}};};
 const result=await probe(base,{token:'secret-test-token',fetchImpl,clock:()=>now});
 assert.equal(max,1);assert.equal(seen.length,5);
 for(const {url,options} of seen){
  assert.equal(url,'https://qa.example.test/api/students');
  assert.equal(options.method,'GET');assert.equal(options.redirect,'manual');
  assert.equal(options.cache,'no-store');assert.equal(options.headers['Cache-Control'],'no-store');
 }
 assert.equal(result.routes[0].success,5);
 assert.equal(result.routes[0].failures,0);
 assert.ok(!JSON.stringify(result).includes('secret-test-token'));
});
test('unauthorized, redirected and failed requests cannot inflate success counts',async()=>{
 let i=0,t=0;const statuses=[200,401,403,302,500];
 const result=await probe(base,{token:'test',clock:()=>++t,fetchImpl:async()=>({status:statuses[i++],body:{cancel:async()=>{}}})});
 assert.equal(result.routes[0].success,1);assert.equal(result.routes[0].failures,4);
 for(const code of [401,403,302,500]) assert.equal(result.routes[0].statuses[code],1);
});
test('network failures cannot be treated as successful performance measurements',async()=>{
 let n=0;
 const result=await probe(base,{token:'test',fetchImpl:async()=>{n++;throw Error('unavailable')}});
 assert.equal(n,5);assert.equal(result.routes[0].success,0);
 assert.equal(result.routes[0].failures,5);assert.equal(result.routes[0].p95Ms,null);
});
test('comparison requires identical role tenant campus locale device and sample size',()=>{
 const a=report(),b=report([row('students',{p75Ms:15,p95Ms:20})],{...ctx,gitSha:'sha-b'});
 assert.equal(compare(a,b)[0].deltaP95Pct,-20);
 for(const key of ['role','tenantAlias','campusAlias','locale','device','metric','sampleCountPerRoute']){
  assert.throws(()=>compare(a,{...b,context:{...b.context,[key]:'different'}}),/Mismatched benchmark context/);
 }
});
test('comparison rejects missing, duplicated, failed and invalid latency routes',()=>{
 const a=report([row('a'),row('b')]);
 for(const routes of [
  [row('a')],[row('a'),row('a')],[row('a'),row('c')],
  [row('a',{failures:1}),row('b')],
  [row('a',{p95Ms:null}),row('b')],
  [row('a',{p75Ms:-1}),row('b')],
  [row('a',{p95Ms:Infinity}),row('b')]
 ]) assert.throws(()=>compare(a,report(routes)));
});
test('percentile comparisons are not cross-SHA success assertions',()=>{
 const before=report([row('a')],{...ctx,gitSha:'sha-a'});
 const after=report([row('a',{p75Ms:30,p95Ms:40})],{...ctx,gitSha:'sha-b'});
 const [delta]=compare(before,after);
 assert.equal(delta.deltaP75Pct,50);
 assert.equal(delta.deltaP95Pct,60);
 assert.equal(delta.baselineP95Ms,25);
 assert.equal(delta.candidateP95Ms,40);
});

test('comparison rejects partial-success or dishonest status counters',()=>{
 const baseline=report();
 for(const bad of [
  {success:4,failures:0},{success:5,failures:1},{samples:6},
  {statuses:{200:4}},{statuses:{200:5,403:1}},
  {statuses:{200:5,network:1}}
 ]) {
  assert.throws(()=>compare(baseline,report([row('students',bad)])));
  assert.throws(()=>compare(report([row('students',bad)]),baseline));
 }
});
test('comparison refuses missing or placeholder authorization context',()=>{
 const baseline=report();
 for(const key of ['role','tenantAlias','campusAlias','locale','device','metric']){
  for(const value of [undefined,'','UNSPECIFIED','UNVERIFIED']){
   const invalid=report([row()],{...ctx,[key]:value});
   assert.throws(()=>compare(invalid,invalid));
  }
 }
 assert.throws(()=>compare({...baseline,context:null},baseline));
 assert.throws(()=>compare({...baseline,routes:[]},baseline));
 assert.throws(()=>compare(baseline,{...baseline,routes:null}));
});
test('comparison rejects invalid sample count and route identifiers',()=>{
 for(const sampleCountPerRoute of [undefined,0,4,5.5]){
  const invalid=report([row()],{...ctx,sampleCountPerRoute});
  assert.throws(()=>compare(invalid,invalid));
 }
 for(const id of ['', '../other','other tenant']){
  const invalid=report([row(id)]);
  assert.throws(()=>compare(invalid,invalid));
 }
});
