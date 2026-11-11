import { setupJournalForm } from './accounting-journals-form.js';
import { setupJournalRegister } from './accounting-journals-register.js';
import { journalRequest, journalSetup, journalSessionLive } from './accounting-journals-api.js';
const root=document.getElementById('accountingJournals');
if(root){
 const ctx={root,get:id=>document.getElementById(id),writable:root.dataset.write==='true',
  live:journalSessionLive,
  api:journalRequest,setup:journalSetup,
  today:()=>{const d=new Date();return [d.getFullYear(),String(d.getMonth()+1).padStart(2,'0'),String(d.getDate()).padStart(2,'0')].join('-');},
  notify:(message,error=false)=>{const el=document.getElementById('journalNotice');el.textContent=message;el.className='alert '+(error?'alert-danger':'alert-success');},
  accounts:[],periods:[],reload:async()=>{},open:async()=>{}};
 ctx.run=async fn=>{try{await fn();}catch(e){ctx.notify(e.message,true);}};
 setupJournalRegister(ctx);
 const observer=new MutationObserver(()=>{
  if(ctx.live())return;
  ctx.reset?.();
  if(ctx.writable)ctx.resetJournalForm?.();
  ctx.notify('Institution session changed. Reload the page.',true);
  observer.disconnect();
 });
 observer.observe(document.body,{attributes:true,attributeFilter:['data-auth-tenant-id','data-auth-session-reference']});
 ctx.run(async()=>{
  await setupJournalForm(ctx);
  await ctx.reload();
  // Work Center supplies only a public GUID. The source API still enforces
  // current tenant, role, module entitlement, state and maker-checker rules.
  const reference=new URLSearchParams(window.location.search).get('reference');
  if(reference&&/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(reference))
   await ctx.open(reference);
 });
}
