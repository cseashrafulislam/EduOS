const stateName={1:'Draft',2:'Submitted',3:'Approved',4:'Posted',5:'Rejected',6:'Reversed',7:'Cancelled'};
export function setupJournalRegister(ctx){
 const $=ctx.get;
 let page=1,pages=1,selected=null,reversalKey=crypto.randomUUID();
 let readSeq=0,detailSeq=0,pending=false,reversalFingerprint=null;
 const completed=new Set();
 const cell=(row,value)=>{const td=document.createElement('td');td.textContent=String(value??'');row.appendChild(td);return td;};
 const setButton=(id,visible)=>{const el=$(id);if(el)el.classList.toggle('d-none',!visible);};
 $('journalFrom').value=ctx.today().slice(0,4)+'-01-01';
 $('journalTo').value=ctx.today();
 async function reload(){
  const seq=++readSeq;
  const fromDate=$('journalFrom').value,toDate=$('journalTo').value;
  if(!fromDate||!toDate||fromDate>toDate)throw new Error('Valid journal date range required.');
  const data=await ctx.api('?'+new URLSearchParams({fromDate,toDate,page,pageSize:25}));
  if(seq!==readSeq||!ctx.live())return;
  pages=Math.max(1,data.totalPages||1);
  $('journalPage').textContent='Page '+page+' of '+pages+' ('+data.totalCount+')';
  $('journalPrev').disabled=page<=1;$('journalNext').disabled=page>=pages;
  const tbody=$('journalRegisterRows');tbody.replaceChildren();
  for(const j of data.items||[]){
   const tr=document.createElement('tr');
   cell(tr,j.journalNumber);cell(tr,j.journalDate);cell(tr,j.accountingPeriodName);
   cell(tr,stateName[j.state]||j.state);cell(tr,j.sourceType);
   cell(tr,Number(j.totalDebit).toFixed(2));cell(tr,Number(j.totalCredit).toFixed(2));
   const td=cell(tr,'');const btn=document.createElement('button');
   btn.type='button';btn.className='btn btn-sm btn-outline-primary';btn.textContent='Details';
   btn.addEventListener('click',()=>ctx.run(()=>open(j.reference)));td.appendChild(btn);tbody.appendChild(tr);
  }
 }
 async function open(reference,internal=false){
  if((pending&&!internal)||!ctx.live())return;
  const seq=++detailSeq;
  const j=await ctx.api('/'+encodeURIComponent(reference));
  if(seq!==detailSeq||!ctx.live())return;
  if(selected?.reference!==j.reference){reversalKey=crypto.randomUUID();reversalFingerprint=null;}
  selected=j;
  $('journalDetailPanel').classList.remove('d-none');
  $('journalDetailTitle').textContent=j.journalNumber+' — '+(stateName[j.state]||j.state);
  $('journalDetailInfo').textContent=j.journalDate+' | '+j.accountingPeriodName+' | '+(j.description||'')+
   (j.reversalOfJournalId?' | Reversal of journal #'+j.reversalOfJournalId:'');
  const tbody=$('journalDetailRows');tbody.replaceChildren();
  for(const line of j.lines||[]){
   const tr=document.createElement('tr');
   cell(tr,line.lineNo);cell(tr,line.accountCode+' — '+line.accountName);cell(tr,line.description);
   cell(tr,Number(line.debitAmount).toFixed(2));cell(tr,Number(line.creditAmount).toFixed(2));
   tbody.appendChild(tr);
  }
  $('journalDetailDebit').textContent=Number(j.totalDebit).toFixed(2);
  $('journalDetailCredit').textContent=Number(j.totalCredit).toFixed(2);
  const manual=j.sourceType==='Manual'&&!j.reversalOfJournalId&&ctx.writable;
  const reversal=j.sourceType==='ManualReversal'&&Boolean(j.reversalOfJournalId)&&ctx.writable;
  setButton('journalSubmit',manual&&j.state===1);
  setButton('journalApprove',(manual||reversal)&&j.state===2);
  setButton('journalReject',(manual||reversal)&&j.state===2);
  setButton('journalPost',(manual||reversal)&&j.state===3);
  setButton('journalReverseForm',manual&&j.state===4);
  if(ctx.writable)$('journalReverseDate').value=ctx.today();
 }
 ctx.reload=reload;ctx.open=open;
 ctx.reset=()=>{
  ++readSeq;++detailSeq;selected=null;reversalFingerprint=null;completed.clear();
  $('journalRegisterRows').replaceChildren();$('journalDetailRows').replaceChildren();
  $('journalDetailPanel').classList.add('d-none');
 };
 $('journalFilter').addEventListener('submit',e=>{e.preventDefault();page=1;ctx.run(reload);});
 $('journalPrev').addEventListener('click',()=>ctx.run(async()=>{if(page>1){page--;await reload();}}));
 $('journalNext').addEventListener('click',()=>ctx.run(async()=>{if(page<pages){page++;await reload();}}));
 $('journalCloseDetail').addEventListener('click',()=>{
  if(pending)return;
  ++detailSeq;$('journalDetailPanel').classList.add('d-none');selected=null;
 });
 if(!ctx.writable)return;
 async function action(name,bodyFactory,confirmation){
  if(pending||!selected||!ctx.live()||!confirm(confirmation))return false;
  const row=selected,operation=row.reference+'|'+name+'|'+row.rowVersion;
  if(completed.has(operation))return false;
  const body=bodyFactory(row);
  pending=true;++detailSeq;++readSeq;
  for(const id of ['journalSubmit','journalApprove','journalReject','journalPost']){
   $(id).disabled=true;
  }
  try{
   const result=await ctx.api('/'+encodeURIComponent(row.reference)+'/'+name,'POST',body);
   completed.add(operation);
   ctx.notify('Journal '+name+' completed.');
   try{await reload();await open(result.reference,true);}
   catch(error){ctx.notify('Journal '+name+' saved; refresh the register. '+error.message,true);}
   return true;
  }finally{
   pending=false;
   for(const id of ['journalSubmit','journalApprove','journalReject','journalPost']){
    $(id).disabled=false;
   }
  }
 }
 $('journalSubmit').addEventListener('click',()=>ctx.run(()=>action('submit',row=>({rowVersion:row.rowVersion}),'Submit this journal?')));
 $('journalApprove').addEventListener('click',()=>ctx.run(()=>action('review',row=>({approve:true,rowVersion:row.rowVersion}),'Approve this journal? Maker and approver must differ.')));
 $('journalReject').addEventListener('click',()=>ctx.run(()=>action('review',row=>({approve:false,rowVersion:row.rowVersion}),'Reject this journal?')));
 $('journalPost').addEventListener('click',()=>ctx.run(()=>action('post',row=>({rowVersion:row.rowVersion}),'Post this balanced journal to the ledger?')));
 $('journalReverseForm').addEventListener('submit',e=>{e.preventDefault();ctx.run(async()=>{
  if(!selected||pending)return;
  const reason=$('journalReverseReason').value.trim(),reversalDate=$('journalReverseDate').value;
  if(!reason||!reversalDate)throw new Error('Reversal date and reason required.');
  const fingerprint=[selected.reference,selected.rowVersion,reversalDate,reason].join('|');
  if(reversalFingerprint!==fingerprint){reversalFingerprint=fingerprint;reversalKey=crypto.randomUUID();}
  const key=reversalKey;
  const saved=await action('reverse',row=>({clientRequestId:key,reversalDate,reason,rowVersion:row.rowVersion}),
   'Submit a compensating reversal for independent approval? No ledger posting occurs yet.');
  if(saved&&reversalKey===key){reversalKey=crypto.randomUUID();reversalFingerprint=null;}
 });});
}
