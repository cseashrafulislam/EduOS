(() => {
 'use strict';
 const root=document.getElementById('supplierSettlement');if(!root)return;
 const $=id=>document.getElementById(id),bn=(document.documentElement.lang||'').toLowerCase().startsWith('bn');
 const t=(en,bangla)=>bn?bangla:en;
 if(bn)root.querySelectorAll('[data-bn]').forEach(x=>{x.textContent=x.dataset.bn;});
 const token=document.querySelector('meta[name="request-verification-token"]')?.content;
 const maker=root.dataset.maker==='true',reviewer=root.dataset.reviewer==='true';
 let invoice=null,page=1,pages=1,seq=0,selectionSeq=0,busy=false,requestKey=null,review=null,withdraw=null,withdrawKey=null;
 const base='/api/procurement/supplier-invoices/';
 const money=x=>Number(x).toLocaleString(bn?'bn-BD':'en-BD',{minimumFractionDigits:2,maximumFractionDigits:2});
 const cell=x=>{const td=document.createElement('td');td.textContent=String(x??'');return td;};
 function status(message,error=false){$('ssStatus').className='small '+(error?'text-danger':'text-muted');$('ssStatus').textContent=message;}
 async function api(url,method='GET',payload){
  const headers={Accept:'application/json'};if(payload!==undefined)headers['Content-Type']='application/json';
  if(method!=='GET'&&token)headers.RequestVerificationToken=token;
  const r=await fetch(url,{method,credentials:'same-origin',cache:'no-store',headers,
   ...(payload===undefined?{}:{body:JSON.stringify(payload)})});
  const body=await r.json().catch(()=>null);
  if(!r.ok||!body?.success)throw new Error(body?.message||t('Request failed','অনুরোধ ব্যর্থ'));
  return body.data;
 }
 function options(select,items){select.replaceChildren(new Option(t('Select','নির্বাচন করুন'),''));
  for(const x of items)select.append(new Option(x.name,String(x.id)));}
 async function load(){
  if(!invoice)return;const n=++seq;
  $('ssPrev').disabled=true;$('ssNext').disabled=true;
  try{
   const result=await api(base+invoice.reference+'/settlements?'+new URLSearchParams({page,pageSize:25}));
   if(n!==seq)return;
   pages=Math.max(1,result.totalPages||1);$('ssRows').replaceChildren();
   for(const x of result.items||[]){
    const tr=document.createElement('tr');
    for(const value of [x.reference.slice(0,8),x.state,money(x.amount),x.plannedDate,x.makerId])tr.append(cell(value));
    const action=cell('');
    if(maker&&x.state==='Submitted'&&
       Number(x.makerId)===Number(document.getElementById('supplierInvoiceMatch')?.dataset.userId)){
     const b=document.createElement('button');b.type='button';b.className='btn btn-outline-danger btn-sm';
     b.textContent=t('Withdraw','প্রত্যাহার');
     b.addEventListener('click',()=>{
      withdraw=x;withdrawKey=null;$('ssWithdrawReason').value='';$('ssWithdrawDialog').showModal();
     });action.append(b);
    }
    if(reviewer&&(x.state==='Submitted'||x.state==='Approved')&&
       Number(x.makerId)!==Number(document.getElementById('supplierInvoiceMatch')?.dataset.userId)){
     const revoking=x.state==='Approved';
     const b=document.createElement('button');b.type='button';b.className='btn btn-outline-primary btn-sm';
     b.textContent=revoking?t('Revoke approval','অনুমোদন প্রত্যাহার'):t('Review','যাচাই');
     b.addEventListener('click',()=>{
      review=x;$('ssReviewReason').value='';
      $('ssDecision').value=revoking?'Reject':'Approve';
      $('ssDecision').disabled=revoking;
      $('ssReviewTitle').textContent=revoking?
       t('Revoke approved, unposted instruction','অনুমোদিত, পোস্টবিহীন নির্দেশনা প্রত্যাহার'):
       t('Independent Finance decision','স্বতন্ত্র ফাইন্যান্স সিদ্ধান্ত');
      $('ssReviewDialog').showModal();
     });action.append(b);
    }
    tr.append(action);$('ssRows').append(tr);
   }
   if(!(result.items||[]).length){const tr=document.createElement('tr'),td=cell(t('No instructions','নির্দেশনা নেই'));td.colSpan=6;tr.append(td);$('ssRows').append(tr);}
   $('ssPage').textContent=page+' / '+pages;$('ssPrev').disabled=page<=1;$('ssNext').disabled=page>=pages;
  }catch(e){if(n===seq)status(e.message,true);}
 }
 async function selectInvoice(x){
  const chosen=++selectionSeq;
  if($('ssWithdrawDialog')?.open)$('ssWithdrawDialog').close();
  withdraw=null;withdrawKey=null;
  invoice=x;requestKey=null;page=1;seq++;
  $('ssSelected').textContent=x.invoiceNumber+' · '+x.reference;
  $('ssRows').replaceChildren();$('ssForm')?.classList.add('d-none');
  try{
   const data=await api(base+x.reference+'/settlements/options');
   if(chosen!==selectionSeq)return;
   status(t('Unreserved BDT ','অসংরক্ষিত ৳')+money(data.availableAmount)+t(' · No payment executed',' · পরিশোধ হয়নি'));
   if(maker&&$('ssForm')){
    options($('ssBank'),data.banks);options($('ssPeriod'),data.periods);
    $('ssAmount').max=String(data.availableAmount);$('ssAmount').value='';
    $('ssDate').value=new Date().toISOString().slice(0,10);
    $('ssForm').classList.remove('d-none');
   }
   await load();
  }catch(e){status(e.message,true);}
 }
 window.addEventListener('procurement-settlement-select',e=>{if(e.detail?.reference)selectInvoice(e.detail);});
 $('ssForm')?.addEventListener('submit',async e=>{
  e.preventDefault();if(!invoice||busy)return;
  busy=true;$('ssSubmit').disabled=true;
  try{
   requestKey ||= crypto.randomUUID();
   await api(base+invoice.reference+'/settlements','POST',{
    clientRequestId:requestKey,invoiceRowVersion:invoice.rowVersion,
    bankAccountId:Number($('ssBank').value),accountingPeriodId:Number($('ssPeriod').value),
    plannedDate:$('ssDate').value,amount:Number($('ssAmount').value),reason:$('ssReason').value.trim()});
   requestKey=null;status(t('Instruction submitted; GL and bank unchanged','নির্দেশনা জমা; GL ও ব্যাংক অপরিবর্তিত'));
   await selectInvoice(invoice);
  }catch(e){status(e.message,true);}finally{busy=false;$('ssSubmit').disabled=false;}
 });
 for(const id of ['ssAmount','ssDate','ssBank','ssPeriod','ssReason'])$(id)?.addEventListener('input',()=>{requestKey=null;});
 $('ssWithdrawClose').addEventListener('click',()=>$('ssWithdrawDialog').close());
 $('ssWithdrawReason').addEventListener('input',()=>{withdrawKey=null;});
 $('ssWithdrawForm').addEventListener('submit',async e=>{
  e.preventDefault();if(!withdraw||!invoice||busy||$('ssWithdrawDialog').open===false)return;
  busy=true;$('ssWithdrawSubmit').disabled=true;
  const selected=invoice.reference,selectedInstruction=withdraw;
  try{
   withdrawKey ||= crypto.randomUUID();
   await api(base+selected+'/settlements/'+selectedInstruction.reference+'/withdraw','POST',{
    clientRequestId:withdrawKey,rowVersion:selectedInstruction.rowVersion,
    reason:$('ssWithdrawReason').value.trim()});
   withdrawKey=null;withdraw=null;$('ssWithdrawDialog').close();
   if(invoice?.reference===selected){
    status(t('Instruction withdrawn; GL and bank unchanged','নির্দেশনা প্রত্যাহার; GL ও ব্যাংক অপরিবর্তিত'));
    await selectInvoice(invoice);
   }
  }catch(e){status(e.message,true);}finally{busy=false;$('ssWithdrawSubmit').disabled=false;}
 });
 $('ssClose').addEventListener('click',()=>$('ssReviewDialog').close());
 $('ssReviewForm').addEventListener('submit',async e=>{
  e.preventDefault();if(!review||!invoice||busy)return;busy=true;$('ssReviewSubmit').disabled=true;
  try{
   await api(base+invoice.reference+'/settlements/'+review.reference+'/review','POST',{
    rowVersion:review.rowVersion,approve:$('ssDecision').value==='Approve',reason:$('ssReviewReason').value.trim()});
   $('ssReviewDialog').close();status(t('Decision saved; no disbursement executed','সিদ্ধান্ত সংরক্ষিত; পরিশোধ হয়নি'));
   await selectInvoice(invoice);
  }catch(e){status(e.message,true);}finally{busy=false;$('ssReviewSubmit').disabled=false;}
 });
 $('ssPrev').addEventListener('click',()=>{if(page>1){page--;load();}});
 $('ssNext').addEventListener('click',()=>{if(page<pages){page++;load();}});
})();
