'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const {percentile,validate,probe,compare}=require('./ux09-readonly-performance-probe.cjs');
const base={baseUrl:'https://example.test',samples:5,timeoutMs:1000,routes:[{id:'students',path:'/api/students'}]};
test('p75/p95 interpolation and empty sample handling',()=>{
  assert.equal(percentile([5,1,4,3,2],.75),4);
  assert.equal(percentile([5,1,4,3,2],.95),4.8);
  assert.equal(percentile([],.95),null);
});
test('reject unsafe hosts, credentials, query, traversal and duplicate routes',()=>{
  for(const baseUrl of ['http://example.test','https://x:y@example.test','https://example.test?token=secret'])
    assert.throws(()=>validate({...base,baseUrl}));
  for(const path of ['//evil.test/path','/api/../private','/api?write=true','/api#secret'])
    assert.throws(()=>validate({...base,routes:[{id:'x',path}]}));
  assert.throws(()=>validate({...base,routes:[base.routes[0],base.routes[0]]}));
  assert.throws(()=>validate({...base,samples:101}));
});
test('requires token, sends only GET no-store with no redirect, never persists token',async()=>{
  const requests=[];let clock=0;
  const fetchImpl=async(url,opts)=>{requests.push({url:String(url),opts});clock+=10;return {status:200,body:{cancel:async()=>{}}};};
  await assert.rejects(probe(base,{fetchImpl}),/token/);
  const result=await probe(base,{token:'qa-only',fetchImpl,clock:()=>clock});
  assert.equal(requests.length,5);
  assert.ok(requests.every(r=>r.opts.method==='GET'&&r.opts.redirect==='manual'&&r.opts.cache==='no-store'));
  assert.ok(requests.every(r=>r.opts.headers.Authorization==='Bearer qa-only'));
  assert.equal(result.routes[0].p95Ms,10);
  assert.ok(!JSON.stringify(result).includes('qa-only'));
});
test('403, redirects and network errors never count as successful latency samples',async()=>{
  const statuses=[403,302,200];let i=0,t=0;
  const result=await probe(base,{token:'x',clock:()=>t+=2,fetchImpl:async()=>{
    const status=statuses[i++]||500;
    if(status===500) throw Error('offline');
    return {status,body:{cancel:async()=>{}}};
  }});
  assert.equal(result.routes[0].failures,4);
  assert.equal(result.routes[0].success,1);
  assert.equal(result.routes[0].statuses[403],1);
  assert.equal(result.routes[0].statuses[302],1);
  assert.equal(result.routes[0].statuses.network,2);
});
test('baseline comparison rejects cross-tenant/device comparisons, failures and route drift',()=>{
  const context={gitSha:'a',role:'Accountant',tenantAlias:'T1',campusAlias:'C1',locale:'bn-BD',device:'390px',metric:'sequential-client-GET-ms',sampleCountPerRoute:5};
  const a={schema:1,context,routes:[{id:'fees',samples:5,success:5,failures:0,statuses:{200:5},p75Ms:100,p95Ms:200}]};
  const b={...a,context:{...context,gitSha:'b'},routes:[{id:'fees',samples:5,success:5,failures:0,statuses:{200:5},p75Ms:80,p95Ms:150}]};
  assert.equal(compare(a,b)[0].deltaP95Pct,-25);
  assert.throws(()=>compare(a,{...b,context:{...b.context,tenantAlias:'T2'}}),/context/);
  assert.throws(()=>compare(a,{...b,routes:[{...b.routes[0],failures:1}]}),/Incomplete or invalid benchmark route/);
  assert.throws(()=>compare(a,{...b,routes:[]}),/routes/);
});

test('reject encoded traversal, encoded authority, backslashes, whitespace and base subpaths',()=>{
  for(const path of ['/api/%2e%2e/admin','/api/%2F%2Fevil','/api/\\\\admin','/api/%0d%0a','/api/hello world','/api/./students','/api//../admin'])
    assert.throws(()=>validate({...base,routes:[{id:'x',path}]}),/canonical/);
  assert.throws(()=>validate({...base,baseUrl:'https://example.test/tenant-one/'}),/base URL/);
});
test('comparison refuses duplicate candidate IDs and invalid percentile values',()=>{
  const context={role:'Teacher',tenantAlias:'A',campusAlias:'C',locale:'bn-BD',device:'390px',metric:'sequential-client-GET-ms',sampleCountPerRoute:5};
  const routes=[{id:'a',samples:5,success:5,failures:0,statuses:{200:5},p75Ms:12,p95Ms:15},{id:'b',samples:5,success:5,failures:0,statuses:{200:5},p75Ms:20,p95Ms:25}];
  const before={schema:1,context,routes};
  assert.throws(()=>compare(before,{schema:1,context,routes:[routes[0],routes[0]]}),/Route set mismatch/);
  assert.throws(()=>compare(before,{schema:1,context,routes:[{...routes[0],p75Ms:null},routes[1]]}),/invalid route/);
  assert.throws(()=>compare(before,{schema:1,context,routes:[{...routes[0],p95Ms:Infinity},routes[1]]}),/invalid route/);
});

test('comparison accepts ordinary ASCII route IDs but rejects malformed identifiers',()=>{
  const context={role:'Teacher',tenantAlias:'T1',campusAlias:'C1',locale:'en-BD',device:'desktop',metric:'sequential-client-GET-ms',sampleCountPerRoute:5};
  const valid={schema:1,context,routes:[{id:'student-list_01',samples:5,success:5,failures:0,statuses:{200:5},p75Ms:10,p95Ms:12}]};
  assert.equal(compare(valid,valid)[0].id,'student-list_01');
  for(const id of ['a.b','a/b','', 'a b']) assert.throws(()=>compare({...valid,routes:[{...valid.routes[0],id}]},valid),/invalid benchmark route/);
  assert.throws(()=>compare({...valid,context:null},valid),/Missing benchmark context/);
});

test('comparison rejects incomplete counters, duplicate baselines and invalid latency ordering',()=>{
  const context={role:'Accountant',tenantAlias:'Tenant-A',campusAlias:'Main',locale:'en-BD',device:'desktop',metric:'sequential-client-GET-ms',sampleCountPerRoute:5};
  const row={id:'fees',samples:5,success:5,failures:0,statuses:{200:5},p75Ms:12,p95Ms:20};
  const report={schema:1,context,routes:[row]};
  assert.throws(()=>compare({...report,routes:[row,row]},report),/Route set mismatch/);
  for(const candidate of [
    {...row,success:4}, {...row,statuses:{200:4}}, {...row,statuses:{200:5,403:1}},
    {...row,failures:1}, {...row,p75Ms:NaN}, {...row,p95Ms:-1}
  ]) assert.throws(()=>compare(report,{...report,routes:[candidate]}),/invalid|failed/i);
});

test('response body cleanup failure does not double-count a successful GET',async()=>{
  let now=0;
  const result=await probe(base,{token:'qa',clock:()=>now+=5,fetchImpl:async()=>({
    status:200,body:{cancel:async()=>{throw Error('stream already closed');}}
  })});
  assert.equal(result.routes[0].success,5);
  assert.equal(result.routes[0].failures,0);
  assert.deepEqual(result.routes[0].statuses,{200:5});
  assert.equal(result.routes[0].p95Ms,5);
});

test('invalid and backwards clock readings fail closed without fake latency',async()=>{
  let i=0;
  const ticks=[10,9,10,NaN,10,Infinity,10,-Infinity,10,9];
  const result=await probe(base,{token:'qa',clock:()=>ticks[i++],fetchImpl:async()=>({
    status:200,body:{cancel:async()=>{}}
  })});
  assert.equal(result.routes[0].success,0);
  assert.equal(result.routes[0].failures,5);
  assert.equal(result.routes[0].statuses.invalidClock,5);
  assert.equal(result.routes[0].p75Ms,null);
  assert.equal(result.routes[0].p95Ms,null);
});

test('comparison rejects impossible percentile ordering on either side',()=>{
  const context={role:'Teacher',tenantAlias:'TenantA',campusAlias:'Main',locale:'bn-BD',device:'390px',metric:'sequential-client-GET-ms',sampleCountPerRoute:5};
  const row={id:'student-list',samples:5,success:5,failures:0,statuses:{200:5},p75Ms:30,p95Ms:20};
  const good={schema:1,context,routes:[{...row,p75Ms:10,p95Ms:20}]};
  const bad={schema:1,context,routes:[row]};
  assert.throws(()=>compare(bad,good),/invalid route/);
  assert.throws(()=>compare(good,bad),/invalid route/);
});
