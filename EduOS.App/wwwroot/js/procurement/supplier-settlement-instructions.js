(() => {
 'use strict';
 const root=document.getElementById('supplierSettlement');if(!root)return;
 const $=id=>document.getElementById(id),bn=(document.documentElement.lang||'').toLowerCase().startsWith('bn');
 const t=(en,bangla)=>bn?bangla:en;
 if(bn)root.querySelectorAll('[data-bn]').forEach(x=>{x.textContent=x.dataset.bn;});
 const token=document.querySelector('meta[name="request-verification-token"]')?.content;
 const maker=root.dataset.maker==='true',reviewer=root.dataset.reviewer==='true';
 let invoice=null,page=1,pages=1,seq=0,selectionSeq=0,busy=false,requestKey=null,review=null,withdraw=null,withdrawKey=null;
 let post=null,postKey=null,reverse=null,reverseKey=null,contra=null,availablePeriods=[];
 const base='/api/procurement/supplier-invoices/';
 const money=x=>Number(x).toLocaleString(bn?'bn-BD':'en-BD',{minimumFractionDigits:2,maximumFractionDigits:2});
 const localDate=()=>{const day=new Date();return [day.getFullYear(),String(day.getMonth()+1).padStart(2,'0'),String(day.getDate()).padStart(2,'0')].join('-');};
 const cell=x=>{const td=document.createElement('td');td.textContent=String(x??'');return td;};
 function status(message,error=false){$('ssStatus').className='small '+(error?'text-danger':'text-muted');$('ssStatus').textContent=message;}
 // Clear every sensitive row and action on failed reads, revoked access or
 // concurrency conflicts. Selection sequence also invalidates late responses.
 function clearSelection(){
  ++selectionSeq;++seq;invoice=null;availablePeriods=[];page=1;pages=1;
  review=null;withdraw=null;post=null;reverse=null;contra=null;
  requestKey=null;withdrawKey=null;postKey=null;reverseKey=null;
  $('ssSelected').textContent=t('No authorized invoice selected','অনুমোদিত ইনভয়েস নির্বাচিত নেই');
  $('ssRows').replaceChildren();$('ssPage').textContent='—';
  $('ssPrev').disabled=true;$('ssNext').disabled=true;
  $('ssForm')?.classList.add('d-none');
  for(const id of ['ssWithdrawDialog','ssReviewDialog','ssPostDialog','ssReverseDialog','ssContraDialog'])
   if($(id)?.open)$(id).close();
 }
 function handleError(error,failedRead=false){
  if(failedRead||[401,403,404,409].includes(error.statusCode))clearSelection();
  status(error.message,true);
 }
 async function api(url,method='GET',payload){
  const headers={Accept:'application/json'};if(payload!==undefined)headers['Content-Type']='application/json';
  if(method!=='GET'&&token)headers.RequestVerificationToken=token;
  const r=await fetch(url,{method,credentials:'same-origin',cache:'no-store',headers,
   ...(payload===undefined?{}:{body:JSON.stringify(payload)})});
  const body=await r.json().catch(()=>null);
  if(!r.ok||!body?.success){
   const error=new Error(body?.message||t('Request failed','অনুরোধ ব্যর্থ'));
   error.statusCode=r.status;
   throw error;
  }
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
    const bankEvidence=x.state==='Posted'?
      (x.bankStatementCleared?t(' · Bank cleared',' · ব্যাংক মিলেছে'):
       t(' · Bank statement pending',' · ব্যাংক স্টেটমেন্ট বাকি')):'';
    for(const value of [x.reference.slice(0,8),x.state+(x.reversalState?' / '+x.reversalState:'')+bankEvidence,money(x.amount),x.plannedDate,x.makerId])tr.append(cell(value));
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
    if(reviewer&&x.state==='Approved'&&
       Number(x.makerId)!==Number(document.getElementById('supplierInvoiceMatch')?.dataset.userId)){
     const b=document.createElement('button');b.type='button';b.className='btn btn-primary btn-sm ms-1';
     b.textContent=t('Bank confirmed / Post GL','ব্যাংক নিশ্চিত / GL পোস্ট');
     b.addEventListener('click',()=>{post=x;postKey=null;$('ssExecutedDate').value=x.plannedDate;
      $('ssBankReference').value='';$('ssPostDialog').showModal();});action.append(b);
    }
    if(maker&&x.state==='Posted'&&!['Submitted','Posted'].includes(x.reversalState)){
     const b=document.createElement('button');b.type='button';b.className='btn btn-outline-danger btn-sm ms-1';
     b.textContent=t('Request GL reversal','GL রিভার্সালের আবেদন');
     b.addEventListener('click',()=>{reverse=x;reverseKey=null;options($('ssReversePeriod'),availablePeriods);
      $('ssReverseDate').value=localDate();$('ssReverseReason').value='';
      $('ssReverseDialog').showModal();});action.append(b);
    }
    if(reviewer&&x.reversalState==='Submitted'&&x.reversalReference){
     const b=document.createElement('button');b.type='button';b.className='btn btn-outline-danger btn-sm ms-1';
     b.textContent=t('Review reversal','রিভার্সাল যাচাই');
     b.addEventListener('click',()=>{contra=x;$('ssContraReason').value='';$('ssContraDecision').value='Approve';
      $('ssContraDialog').showModal();});action.append(b);
    }
    tr.append(action);$('ssRows').append(tr);
   }
   if(!(result.items||[]).length){const tr=document.createElement('tr'),td=cell(t('No instructions','নির্দেশনা নেই'));td.colSpan=6;tr.append(td);$('ssRows').append(tr);}
   $('ssPage').textContent=page+' / '+pages;$('ssPrev').disabled=page<=1;$('ssNext').disabled=page>=pages;
  }catch(e){if(n===seq)handleError(e,true);}
 }
 async function selectInvoice(x){
  const chosen=++selectionSeq;
  if($('ssWithdrawDialog')?.open)$('ssWithdrawDialog').close();
  if($('ssReviewDialog')?.open)$('ssReviewDialog').close();
  for(const id of ['ssPostDialog','ssReverseDialog','ssContraDialog'])if($(id)?.open)$(id).close();
  review=null;withdraw=null;withdrawKey=null;post=null;postKey=null;reverse=null;reverseKey=null;contra=null;
  invoice=x;requestKey=null;page=1;seq++;
  $('ssSelected').textContent=x.invoiceNumber+' · '+x.reference;
  $('ssRows').replaceChildren();$('ssForm')?.classList.add('d-none');
  try{
   const data=await api(base+x.reference+'/settlements/options');
   if(chosen!==selectionSeq)return;
   availablePeriods=data.periods||[];
   status(t('Unreserved BDT ','অসংরক্ষিত ৳')+money(data.availableAmount)+
    t(' · Posted credits BDT ',' · পোস্টকৃত ক্রেডিট ৳')+money(data.postedSupplierCreditAmount||0)+
    t(' · No payment executed',' · পরিশোধ হয়নি'));
   if(maker&&$('ssForm')){
    options($('ssBank'),data.banks);options($('ssPeriod'),data.periods);
    $('ssAmount').max=String(data.availableAmount);$('ssAmount').value='';
    $('ssDate').value=localDate();
    $('ssForm').classList.remove('d-none');
   }
   await load();
  }catch(e){if(chosen===selectionSeq)handleError(e,true);}
 }
 window.addEventListener('procurement-settlement-select',e=>{if(e.detail?.reference)selectInvoice(e.detail);});
 $('ssForm')?.addEventListener('submit',async e=>{
  e.preventDefault();if(!invoice||busy)return;
  busy=true;$('ssSubmit').disabled=true;
  const selected=invoice.reference,chosen=selectionSeq;
  try{
   requestKey ||= crypto.randomUUID();
   await api(base+selected+'/settlements','POST',{
    clientRequestId:requestKey,invoiceRowVersion:invoice.rowVersion,
    bankAccountId:Number($('ssBank').value),accountingPeriodId:Number($('ssPeriod').value),
    plannedDate:$('ssDate').value,amount:Number($('ssAmount').value),reason:$('ssReason').value.trim()});
   if(chosen!==selectionSeq||invoice?.reference!==selected)return;
   requestKey=null;status(t('Instruction submitted; GL and bank unchanged','নির্দেশনা জমা; GL ও ব্যাংক অপরিবর্তিত'));
   await selectInvoice(invoice);
  }catch(e){if(chosen===selectionSeq&&invoice?.reference===selected)handleError(e);}finally{busy=false;$('ssSubmit').disabled=false;}
 });
 for(const id of ['ssAmount','ssDate','ssBank','ssPeriod','ssReason'])$(id)?.addEventListener('input',()=>{requestKey=null;});
 $('ssWithdrawClose').addEventListener('click',()=>$('ssWithdrawDialog').close());
 $('ssWithdrawReason').addEventListener('input',()=>{withdrawKey=null;});
 $('ssWithdrawForm').addEventListener('submit',async e=>{
  e.preventDefault();if(!withdraw||!invoice||busy||$('ssWithdrawDialog').open===false)return;
  busy=true;$('ssWithdrawSubmit').disabled=true;
  const selected=invoice.reference,selectedInstruction=withdraw,chosen=selectionSeq;
  try{
   withdrawKey ||= crypto.randomUUID();
   await api(base+selected+'/settlements/'+selectedInstruction.reference+'/withdraw','POST',{
    clientRequestId:withdrawKey,rowVersion:selectedInstruction.rowVersion,
    reason:$('ssWithdrawReason').value.trim()});
   if(chosen===selectionSeq&&invoice?.reference===selected&&withdraw?.reference===selectedInstruction.reference){
    withdrawKey=null;withdraw=null;$('ssWithdrawDialog').close();
    status(t('Instruction withdrawn; GL and bank unchanged','নির্দেশনা প্রত্যাহার; GL ও ব্যাংক অপরিবর্তিত'));
    await selectInvoice(invoice);
   }
  }catch(e){if(chosen===selectionSeq&&invoice?.reference===selected)handleError(e);}finally{busy=false;$('ssWithdrawSubmit').disabled=false;}
 });
 $('ssClose').addEventListener('click',()=>$('ssReviewDialog').close());
 $('ssReviewForm').addEventListener('submit',async e=>{
  e.preventDefault();if(!review||!invoice||busy)return;busy=true;$('ssReviewSubmit').disabled=true;
  const selected=invoice.reference,selectedReview=review,chosen=selectionSeq;
  try{
   await api(base+selected+'/settlements/'+selectedReview.reference+'/review','POST',{
    rowVersion:selectedReview.rowVersion,approve:$('ssDecision').value==='Approve',reason:$('ssReviewReason').value.trim()});
   if(chosen===selectionSeq&&invoice?.reference===selected&&review?.reference===selectedReview.reference){
    $('ssReviewDialog').close();status(t('Decision saved; no disbursement executed','সিদ্ধান্ত সংরক্ষিত; পরিশোধ হয়নি'));
    await selectInvoice(invoice);
   }
  }catch(e){if(chosen===selectionSeq&&invoice?.reference===selected)handleError(e);}finally{busy=false;$('ssReviewSubmit').disabled=false;}
 });
 $('ssPostClose')?.addEventListener('click',()=>$('ssPostDialog').close());
 for(const id of ['ssExecutedDate','ssBankReference'])$(id)?.addEventListener('input',()=>{postKey=null;});
 $('ssPostForm')?.addEventListener('submit',async e=>{
  e.preventDefault();if(!post||!invoice||busy)return;busy=true;$('ssPostSubmit').disabled=true;
  const selected=invoice.reference,source=post,chosen=selectionSeq;
  try{
   postKey ||= crypto.randomUUID();
   await api(base+selected+'/settlements/'+source.reference+'/post','POST',{
    clientRequestId:postKey,rowVersion:source.rowVersion,executedDate:$('ssExecutedDate').value,
    bankTransactionReference:$('ssBankReference').value.trim()});
   if(chosen===selectionSeq&&invoice?.reference===selected&&post?.reference===source.reference){
    postKey=null;post=null;$('ssPostDialog').close();
    status(t('Confirmed external disbursement posted to Finance GL','বাইরে নিশ্চিত পরিশোধ ফাইন্যান্স GL-এ পোস্ট হয়েছে'));
    await selectInvoice(invoice);
   }
  }catch(e){if(chosen===selectionSeq&&invoice?.reference===selected)handleError(e);}
  finally{busy=false;$('ssPostSubmit').disabled=false;}
 });
 $('ssReverseClose')?.addEventListener('click',()=>$('ssReverseDialog').close());
 for(const id of ['ssReverseDate','ssReversePeriod','ssReverseReason'])$(id)?.addEventListener('input',()=>{reverseKey=null;});
 $('ssReverseForm')?.addEventListener('submit',async e=>{
  e.preventDefault();if(!reverse||!invoice||busy)return;busy=true;$('ssReverseSubmit').disabled=true;
  const selected=invoice.reference,source=reverse,chosen=selectionSeq;
  try{
   reverseKey ||= crypto.randomUUID();
   await api(base+selected+'/settlements/'+source.reference+'/reversal','POST',{
    clientRequestId:reverseKey,originalRowVersion:source.rowVersion,
    accountingPeriodId:Number($('ssReversePeriod').value),reversalDate:$('ssReverseDate').value,
    reason:$('ssReverseReason').value.trim()});
   if(chosen===selectionSeq&&invoice?.reference===selected&&reverse?.reference===source.reference){
    reverseKey=null;reverse=null;$('ssReverseDialog').close();
    status(t('Full reversal submitted; independently review before GL posting','সম্পূর্ণ রিভার্সাল জমা; GL পোস্টের আগে স্বতন্ত্র যাচাই প্রয়োজন'));
    await selectInvoice(invoice);
   }
  }catch(e){if(chosen===selectionSeq&&invoice?.reference===selected)handleError(e);}
  finally{busy=false;$('ssReverseSubmit').disabled=false;}
 });
 $('ssContraClose')?.addEventListener('click',()=>$('ssContraDialog').close());
 $('ssContraForm')?.addEventListener('submit',async e=>{
  e.preventDefault();if(!contra||!invoice||busy)return;busy=true;$('ssContraSubmit').disabled=true;
  const selected=invoice.reference,source=contra,chosen=selectionSeq;
  try{
   await api(base+selected+'/settlements/'+source.reference+'/reversal/'+source.reversalReference+'/review','POST',{
    rowVersion:source.reversalRowVersion,approve:$('ssContraDecision').value==='Approve',
    reason:$('ssContraReason').value.trim()});
   if(chosen===selectionSeq&&invoice?.reference===selected&&contra?.reference===source.reference){
    contra=null;$('ssContraDialog').close();
    status(t('Independent reversal decision saved','স্বতন্ত্র রিভার্সাল সিদ্ধান্ত সংরক্ষিত'));
    await selectInvoice(invoice);
   }
  }catch(e){if(chosen===selectionSeq&&invoice?.reference===selected)handleError(e);}
  finally{busy=false;$('ssContraSubmit').disabled=false;}
 });
 $('ssPrev').addEventListener('click',()=>{if(page>1){page--;load();}});
 $('ssNext').addEventListener('click',()=>{if(page<pages){page++;load();}});
})();
