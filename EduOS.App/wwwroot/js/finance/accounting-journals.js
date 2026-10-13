import { setupJournalForm } from './accounting-journals-form.js';
import { setupJournalRegister } from './accounting-journals-register.js';
import { journalRequest, journalSetup } from './accounting-journals-api.js';
const root=document.getElementById('accountingJournals');
if(root){
 const ctx={root,get:id=>document.getElementById(id),writable:root.dataset.write==='true',
  api:journalRequest,setup:journalSetup,
  today:()=>{const d=new Date();return [d.getFullYear(),String(d.getMonth()+1).padStart(2,'0'),String(d.getDate()).padStart(2,'0')].join('-');},
  notify:(message,error=false)=>{const el=document.getElementById('journalNotice');el.textContent=message;el.className='alert '+(error?'alert-danger':'alert-success');},
  accounts:[],periods:[],reload:async()=>{},open:async()=>{}};
 ctx.run=async fn=>{try{await fn();}catch(e){ctx.notify(e.message,true);}};
 setupJournalRegister(ctx);
 ctx.run(async()=>{await setupJournalForm(ctx);await ctx.reload();});
}
