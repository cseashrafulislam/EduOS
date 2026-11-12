'use strict';
const fs = require('node:fs');
const {performance} = require('node:perf_hooks');
const {URL} = require('node:url');
function percentile(xs,p) {
  if (!xs.length) return null;
  const a=[...xs].sort((x,y)=>x-y), rank=(a.length-1)*p, lo=Math.floor(rank), hi=Math.ceil(rank);
  return Number((a[lo]+(a[hi]-a[lo])*(rank-lo)).toFixed(2));
}
function validate(c) {
  if (!c || !Array.isArray(c.routes) || !c.routes.length) throw Error('Explicit read-only routes required');
  const base=new URL(c.baseUrl);
  if (base.protocol!=='https:' && !['localhost','127.0.0.1','[::1]'].includes(base.hostname)) throw Error('Remote HTTPS required');
  if (base.username || base.password || base.search || base.hash || base.pathname!=='/') throw Error('Credentials/query/path forbidden in base URL');
  if (!Number.isInteger(c.samples)||c.samples<5||c.samples>100) throw Error('samples must be 5..100');
  if (!Number.isInteger(c.timeoutMs)||c.timeoutMs<100||c.timeoutMs>30000) throw Error('timeoutMs must be 100..30000');
  const ids=new Set();
  for (const r of c.routes) {
    if (!r || typeof r.id!=='string' || !/^[A-Za-z0-9_-]{1,80}$/.test(r.id) || ids.has(r.id)) throw Error('Unique safe route ID required');
    ids.add(r.id);
    if (typeof r.path!=='string'||!/^\/[A-Za-z0-9/_\-.]*$/.test(r.path)||r.path.startsWith('//')||r.path.split('/').some(s=>s==='.'||s==='..')) throw Error('Only canonical absolute GET paths without encoded/query/traversal characters allowed');
    if (new URL(r.path,base).origin!==base.origin) throw Error('Cross-origin route denied');
  }
  return base;
}
async function probe(config,opts={}) {
  const base=validate(config), token=opts.token;
  if (!token||/[\r\n]/.test(token)) throw Error('Explicit read-only token required');
  const fetchImpl=opts.fetchImpl||fetch, clock=opts.clock||(()=>performance.now()), routes=[];
  for (const r of config.routes) {
    const times=[], statuses={}; let failures=0;
    for (let i=0;i<config.samples;i++) {
      const controller=new AbortController(), timer=setTimeout(()=>controller.abort(),config.timeoutMs), start=clock();
      try {
        const response=await fetchImpl(new URL(r.path,base),{method:'GET',redirect:'manual',cache:'no-store',signal:controller.signal,
          headers:{Authorization:'Bearer '+token,Accept:'application/json','Cache-Control':'no-store'}});
        statuses[response.status]=(statuses[response.status]||0)+1;
        if (response.status===200) {
          const elapsed=clock()-start;
          if (Number.isFinite(elapsed) && elapsed>=0) times.push(Number(elapsed.toFixed(2)));
          else { failures++; statuses.invalidClock=(statuses.invalidClock||0)+1; }
        } else failures++;
        // Body cleanup must not turn a completed HTTP response into a network failure.
        try { await response.body?.cancel?.(); } catch (_) { /* cleanup is best effort */ }
      } catch (_) { failures++; statuses.network=(statuses.network||0)+1; }
      finally {clearTimeout(timer);}
    }
    routes.push({id:r.id,samples:config.samples,success:times.length,failures,statuses,p75Ms:percentile(times,.75),p95Ms:percentile(times,.95)});
  }
  return {schema:1,measuredAtUtc:new Date().toISOString(),context:{
    gitSha:config.gitSha||'UNVERIFIED',role:config.role||'UNSPECIFIED',
    tenantAlias:config.tenantAlias||'UNSPECIFIED',campusAlias:config.campusAlias||'UNSPECIFIED',
    locale:config.locale||'UNSPECIFIED',device:config.device||'UNSPECIFIED',
    metric:'sequential-client-GET-ms',sampleCountPerRoute:config.samples},routes};
}
function compare(before,after) {
  if(!before || !after || before.schema!==1 || after.schema!==1) throw Error('Unsupported schema');
  if(!before.context || !after.context) throw Error('Missing benchmark context');
  for(const k of ['role','tenantAlias','campusAlias','locale','device','metric','sampleCountPerRoute'])
    if(before.context[k]!==after.context[k]) throw Error('Mismatched benchmark context: '+k);
  for(const report of [before,after]) {
    if(!report.context || !Array.isArray(report.routes) || !report.routes.length)
      throw Error('Missing benchmark context or routes');
    if(!Number.isInteger(report.context.sampleCountPerRoute) || report.context.sampleCountPerRoute<5)
      throw Error('Invalid benchmark sample count');
    for(const key of ['role','tenantAlias','campusAlias','locale','device','metric'])
      if(typeof report.context[key]!=='string' || !report.context[key].trim() ||
         ['UNSPECIFIED','UNVERIFIED'].includes(report.context[key]))
        throw Error('Explicit benchmark context required: '+key);
    for(const r of report.routes) {
      if(!r || typeof r.id!=='string' || !/^[A-Za-z0-9_-]{1,80}$/.test(r.id) ||
         r.samples!==report.context.sampleCountPerRoute ||
         r.success!==r.samples || r.failures!==0 ||
         !r.statuses || r.statuses['200']!==r.samples ||
         Object.keys(r.statuses).some(k=>k!=='200'))
        throw Error('Incomplete or invalid benchmark route');
    }
  }
  const map=new Map(before.routes.map(r=>[r.id,r]));
  if(map.size!==before.routes.length||after.routes.length!==before.routes.length||new Set(after.routes.map(r=>r.id)).size!==after.routes.length) throw Error('Route set mismatch');
  return after.routes.map(r=>{
    const old=map.get(r.id);
    if(!old||old.failures||r.failures||![old.p75Ms,old.p95Ms,r.p75Ms,r.p95Ms].every(v=>typeof v==='number'&&Number.isFinite(v)&&v>=0)||old.p75Ms>old.p95Ms||r.p75Ms>r.p95Ms) throw Error('Cannot compare failed or invalid route: '+r.id);
    return {id:r.id,baselineP75Ms:old.p75Ms,candidateP75Ms:r.p75Ms,
      deltaP75Pct:Number(((r.p75Ms-old.p75Ms)/Math.max(old.p75Ms,.01)*100).toFixed(2)),
      baselineP95Ms:old.p95Ms,candidateP95Ms:r.p95Ms,
      deltaP95Pct:Number(((r.p95Ms-old.p95Ms)/Math.max(old.p95Ms,.01)*100).toFixed(2))};
  });
}
if(require.main===module)(async()=>{
  const [,,configPath,outputPath,baselinePath]=process.argv;
  if(!configPath||!outputPath) throw Error('Usage: node ux09-readonly-performance-probe.cjs config.json output.json [baseline.json]');
  const result=await probe(JSON.parse(fs.readFileSync(configPath,'utf8')),{token:process.env.EDUOS_QA_READONLY_TOKEN});
  fs.writeFileSync(outputPath,JSON.stringify(result,null,2)+'\n',{flag:'wx',mode:0o600});
  console.log(JSON.stringify(baselinePath?compare(JSON.parse(fs.readFileSync(baselinePath,'utf8')),result):
    result.routes.map(({id,failures,p75Ms,p95Ms})=>({id,failures,p75Ms,p95Ms})),null,2));
  if(result.routes.some(r=>r.failures)) process.exitCode=1;
})().catch(e=>{console.error('QA probe refused:',e.message);process.exitCode=1;});
module.exports={percentile,validate,probe,compare};
