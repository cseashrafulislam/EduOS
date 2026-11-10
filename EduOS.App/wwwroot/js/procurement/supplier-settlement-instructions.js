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
 let requestSignature=null,withdrawSignature=null,postSignature=null,reverseSignature=null;
 const base='/api/procurement/supplier-invoices/';
 const money=x=>Number(x).toLocaleString(bn?'bn-BD':'en-BD',{minimumFractionDigits:2,maximumFractionDigits:2});
 const localDate=()=>{const day=new Date();return [day.getFullYear(),String(day.getMonth()+1).padStart(2,'0'),String(day.getDate()).padStart(2,'0')].join('-');};
 const cell=x=>{const td=document.createElement('td');td.textContent=String(x??'');return td;};
 function status(message,error=false){$('ssStatus').className='small '+(error?'text-danger':'text-muted');$('ssStatus').textContent=message;}
 const editorIds=['ssForm','ssWithdrawForm','ssReviewForm','ssPostForm','ssReverseForm','ssContraForm'];
 const guards={};
 for(const id of editorIds){const form=$(id);if(form?.elements&&window.EduOSFormGuard)
  guards[id]=window.EduOSFormGuard.create(form);}
 const dialogs=[['ssWithdrawDialog','ssWithdrawForm'],['ssReviewDialog','ssReviewForm'],
  ['ssPostDialog','ssPostForm'],['ssReverseDialog','ssReverseForm'],['ssContraDialog','ssContraForm']];
 function closeEditor(dialogId,force=false){
  const formId=dialogs.find(x=>x[0]===dialogId)?.[1],dialog=$(dialogId);
  if(!force&&(busy||guards[formId]?.isDirty()&&!guards[formId].confirmDiscard()))return false;
  guards[formId]?.markClean();if(dialog?.open)dialog.close();return true;
 }
 for(const [dialogId,formId] of dialogs)$(dialogId)?.addEventListener('cancel',e=>{
  if(busy||guards[formId]?.isDirty()&&!guards[formId].confirmDiscard())e.preventDefault();
  else guards[formId]?.markClean();
 });
 function lock(formId,locked){if($(formId))$(formId).inert=locked;}
 function keyedBody(body,signature,key){
  const fingerprint=JSON.stringify(body);
  return {same:signature===fingerprint,signature:fingerprint,key:signature===fingerprint?key:null};
 }
 // Clear every sensitive row and action on failed reads, revoked access or
 // concurrency conflicts. Selection sequence also invalidates late responses.
 function clearSelection(){
  ++selectionSeq;++seq;invoice=null;availablePeriods=[];page=1;pages=1;
  review=null;withdraw=null;post=null;reverse=null;contra=null;
  requestKey=null;withdrawKey=null;postKey=null;reverseKey=null;
  requestSignature=null;withdrawSignature=null;postSignature=null;reverseSignature=null;
  $('ssSelected').textContent=t('No authorized invoice selected','অনুমোদিত ইনভয়েস নির্বাচিত নেই');
  $('ssRows').replaceChildren();$('ssPage').textContent='—';
  $('ssPrev').disabled=true;$('ssNext').disabled=true;
  $('ssForm')?.classList.add('d-none');
  for(const [dialogId] of dialogs)closeEditor(dialogId,true);
  guards.ssForm?.markClean();
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
      withdraw=x;withdrawKey=null;withdrawSignature=null;
      $('ssWithdrawReason').value='';guards.ssWithdrawForm?.markClean();$('ssWithdrawDialog').showModal();
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
      guards.ssReviewForm?.markClean();$('ssReviewDialog').showModal();
     });action.append(b);
    }
    if(reviewer&&x.state==='Approved'&&
       Number(x.makerId)!==Number(document.getElementById('supplierInvoiceMatch')?.dataset.userId)){
     const b=document.createElement('button');b.type='button';b.className='btn btn-primary btn-sm ms-1';
     b.textContent=t('Bank confirmed / Post GL','ব্যাংক নিশ্চিত / GL পোস্ট');
     b.addEventListener('click',()=>{post=x;postKey=null;postSignature=null;
      $('ssExecutedDate').value=x.plannedDate;$('ssBankReference').value='';
      guards.ssPostForm?.markClean();$('ssPostDialog').showModal();});action.append(b);
    }
    if(maker&&x.state==='Posted'&&!['Submitted','Posted'].includes(x.reversalState)){
     const b=document.createElement('button');b.type='button';b.className='btn btn-outline-danger btn-sm ms-1';
     b.textContent=t('Request GL reversal','GL রিভার্সালের আবেদন');
     b.addEventListener('click',()=>{reverse=x;reverseKey=null;reverseSignature=null;
      options($('ssReversePeriod'),availablePeriods);
      $('ssReverseDate').value=localDate();$('ssReverseReason').value='';
      guards.ssReverseForm?.markClean();$('ssReverseDialog').showModal();});action.append(b);
    }
    if(reviewer&&x.reversalState==='Submitted'&&x.reversalReference){
     const b=document.createElement('button');b.type='button';b.className='btn btn-outline-danger btn-sm ms-1';
     b.textContent=t('Review reversal','রিভার্সাল যাচাই');
     b.addEventListener('click',()=>{contra=x;$('ssContraReason').value='';
      $('ssContraDecision').value='Approve';guards.ssContraForm?.markClean();
      $('ssContraDialog').showModal();});action.append(b);
    }
    tr.append(action);$('ssRows').append(tr);
   }
   if(!(result.items||[]).length){const tr=document.createElement('tr'),td=cell(t('No instructions','নির্দেশনা নেই'));td.colSpan=6;tr.append(td);$('ssRows').append(tr);}
   $('ssPage').textContent=page+' / '+pages;$('ssPrev').disabled=page<=1;$('ssNext').disabled=page>=pages;
  }catch(e){if(n===seq)handleError(e,true);}
 }
 async function selectInvoice(x){
  if(invoice&&invoice.reference!==x.reference&&guards.ssForm?.isDirty()&&
   !guards.ssForm.confirmDiscard())return;
  if(dialogs.some(([id,form])=>$(id)?.open&&guards[form]?.isDirty()&&
   !guards[form].confirmDiscard()))return;
  const chosen=++selectionSeq;
  for(const [dialogId] of dialogs)closeEditor(dialogId,true);
  review=null;withdraw=null;withdrawKey=null;post=null;postKey=null;reverse=null;reverseKey=null;contra=null;
  invoice=x;requestKey=null;requestSignature=null;withdrawSignature=null;
  postSignature=null;reverseSignature=null;page=1;seq++;
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
    guards.ssForm?.markClean();$('ssForm').classList.remove('d-none');
   }
   await load();
  }catch(e){if(chosen===selectionSeq)handleError(e,true);}
 }
 window.addEventListener('procurement-settlement-select',e=>{if(e.detail?.reference)selectInvoice(e.detail);});
 $('ssForm')?.addEventListener('submit',async e=>{
  e.preventDefault();if(!invoice||busy||$('ssForm').reportValidity?.()===false)return;
  busy=true;$('ssSubmit').disabled=true;lock('ssForm',true);
  const selected=invoice.reference,chosen=selectionSeq;
  try{
   const body={invoiceRowVersion:invoice.rowVersion,
    bankAccountId:Number($('ssBank').value),accountingPeriodId:Number($('ssPeriod').value),
    plannedDate:$('ssDate').value,amount:Number($('ssAmount').value),reason:$('ssReason').value.trim()};
   const key=keyedBody({reference:selected,...body},requestSignature,requestKey);
   requestSignature=key.signature;requestKey=key.key;
   requestKey ||= crypto.randomUUID();
   await api(base+selected+'/settlements','POST',{clientRequestId:requestKey,...body});
   if(chosen!==selectionSeq||invoice?.reference!==selected)return;
   requestKey=null;requestSignature=null;guards.ssForm?.markClean();
   await selectInvoice(invoice);
   if(invoice?.reference===selected)status(t('Instruction submitted; GL and bank unchanged','নির্দেশনা জমা; GL ও ব্যাংক অপরিবর্তিত'));
  }catch(e){if(chosen===selectionSeq&&invoice?.reference===selected)handleError(e);}
  finally{busy=false;$('ssSubmit').disabled=false;lock('ssForm',false);}
 });
 for(const id of ['ssAmount','ssDate','ssBank','ssPeriod','ssReason'])$(id)?.addEventListener('input',()=>{requestKey=null;});
 $('ssWithdrawClose').addEventListener('click',()=>closeEditor('ssWithdrawDialog'));
 $('ssWithdrawReason').addEventListener('input',()=>{withdrawKey=null;});
 $('ssWithdrawForm').addEventListener('submit',async e=>{
  e.preventDefault();if(!withdraw||!invoice||busy||$('ssWithdrawDialog').open===false)return;
  busy=true;$('ssWithdrawSubmit').disabled=true;lock('ssWithdrawForm',true);
  const selected=invoice.reference,selectedInstruction=withdraw,chosen=selectionSeq;
  try{
   const body={rowVersion:selectedInstruction.rowVersion,reason:$('ssWithdrawReason').value.trim()};
   const key=keyedBody({selected,source:selectedInstruction.reference,...body},withdrawSignature,withdrawKey);
   withdrawSignature=key.signature;withdrawKey=key.key;
   withdrawKey ||= crypto.randomUUID();
   await api(base+selected+'/settlements/'+selectedInstruction.reference+'/withdraw','POST',
    {clientRequestId:withdrawKey,...body});
   if(chosen===selectionSeq&&invoice?.reference===selected&&withdraw?.reference===selectedInstruction.reference){
    withdrawKey=null;withdrawSignature=null;withdraw=null;closeEditor('ssWithdrawDialog',true);
    await selectInvoice(invoice);
    if(invoice?.reference===selected)status(t('Instruction withdrawn; GL and bank unchanged','নির্দেশনা প্রত্যাহার; GL ও ব্যাংক অপরিবর্তিত'));
   }
  }catch(e){if(chosen===selectionSeq&&invoice?.reference===selected)handleError(e);}
  finally{busy=false;$('ssWithdrawSubmit').disabled=false;lock('ssWithdrawForm',false);}
 });
 $('ssClose').addEventListener('click',()=>closeEditor('ssReviewDialog'));
 $('ssReviewForm').addEventListener('submit',async e=>{
  e.preventDefault();if(!review||!invoice||busy)return;
  busy=true;$('ssReviewSubmit').disabled=true;lock('ssReviewForm',true);
  const selected=invoice.reference,selectedReview=review,chosen=selectionSeq;
  try{
   await api(base+selected+'/settlements/'+selectedReview.reference+'/review','POST',{
    rowVersion:selectedReview.rowVersion,approve:$('ssDecision').value==='Approve',reason:$('ssReviewReason').value.trim()});
   if(chosen===selectionSeq&&invoice?.reference===selected&&review?.reference===selectedReview.reference){
    closeEditor('ssReviewDialog',true);
    await selectInvoice(invoice);
    if(invoice?.reference===selected)status(t('Decision saved; no disbursement executed','সিদ্ধান্ত সংরক্ষিত; পরিশোধ হয়নি'));
   }
  }catch(e){if(chosen===selectionSeq&&invoice?.reference===selected)handleError(e);}
  finally{busy=false;$('ssReviewSubmit').disabled=false;lock('ssReviewForm',false);}
 });
 $('ssPostClose')?.addEventListener('click',()=>closeEditor('ssPostDialog'));
 for(const id of ['ssExecutedDate','ssBankReference'])$(id)?.addEventListener('input',()=>{postKey=null;});
 $('ssPostForm')?.addEventListener('submit',async e=>{
  e.preventDefault();if(!post||!invoice||busy)return;
  busy=true;$('ssPostSubmit').disabled=true;lock('ssPostForm',true);
  const selected=invoice.reference,source=post,chosen=selectionSeq;
  try{
   const body={rowVersion:source.rowVersion,executedDate:$('ssExecutedDate').value,
    bankTransactionReference:$('ssBankReference').value.trim()};
   const key=keyedBody({selected,source:source.reference,...body},postSignature,postKey);
   postSignature=key.signature;postKey=key.key;
   postKey ||= crypto.randomUUID();
   await api(base+selected+'/settlements/'+source.reference+'/post','POST',
    {clientRequestId:postKey,...body});
   if(chosen===selectionSeq&&invoice?.reference===selected&&post?.reference===source.reference){
    postKey=null;postSignature=null;post=null;closeEditor('ssPostDialog',true);
    await selectInvoice(invoice);
    if(invoice?.reference===selected)status(t('Confirmed external disbursement posted to Finance GL','বাইরে নিশ্চিত পরিশোধ ফাইন্যান্স GL-এ পোস্ট হয়েছে'));
   }
  }catch(e){if(chosen===selectionSeq&&invoice?.reference===selected)handleError(e);}
  finally{busy=false;$('ssPostSubmit').disabled=false;lock('ssPostForm',false);}
 });
 $('ssReverseClose')?.addEventListener('click',()=>closeEditor('ssReverseDialog'));
 for(const id of ['ssReverseDate','ssReversePeriod','ssReverseReason'])$(id)?.addEventListener('input',()=>{reverseKey=null;});
 $('ssReverseForm')?.addEventListener('submit',async e=>{
  e.preventDefault();if(!reverse||!invoice||busy)return;
  busy=true;$('ssReverseSubmit').disabled=true;lock('ssReverseForm',true);
  const selected=invoice.reference,source=reverse,chosen=selectionSeq;
  try{
   const body={originalRowVersion:source.rowVersion,
    accountingPeriodId:Number($('ssReversePeriod').value),reversalDate:$('ssReverseDate').value,
    reason:$('ssReverseReason').value.trim()};
   const key=keyedBody({selected,source:source.reference,...body},reverseSignature,reverseKey);
   reverseSignature=key.signature;reverseKey=key.key;
   reverseKey ||= crypto.randomUUID();
   await api(base+selected+'/settlements/'+source.reference+'/reversal','POST',
    {clientRequestId:reverseKey,...body});
   if(chosen===selectionSeq&&invoice?.reference===selected&&reverse?.reference===source.reference){
    reverseKey=null;reverseSignature=null;reverse=null;closeEditor('ssReverseDialog',true);
    await selectInvoice(invoice);
    if(invoice?.reference===selected)status(t('Full reversal submitted; independently review before GL posting','সম্পূর্ণ রিভার্সাল জমা; GL পোস্টের আগে স্বতন্ত্র যাচাই প্রয়োজন'));
   }
  }catch(e){if(chosen===selectionSeq&&invoice?.reference===selected)handleError(e);}
  finally{busy=false;$('ssReverseSubmit').disabled=false;lock('ssReverseForm',false);}
 });
 $('ssContraClose')?.addEventListener('click',()=>closeEditor('ssContraDialog'));
 $('ssContraForm')?.addEventListener('submit',async e=>{
  e.preventDefault();if(!contra||!invoice||busy)return;
  busy=true;$('ssContraSubmit').disabled=true;lock('ssContraForm',true);
  const selected=invoice.reference,source=contra,chosen=selectionSeq;
  try{
   await api(base+selected+'/settlements/'+source.reference+'/reversal/'+source.reversalReference+'/review','POST',{
    rowVersion:source.reversalRowVersion,approve:$('ssContraDecision').value==='Approve',
    reason:$('ssContraReason').value.trim()});
   if(chosen===selectionSeq&&invoice?.reference===selected&&contra?.reference===source.reference){
    contra=null;closeEditor('ssContraDialog',true);
    await selectInvoice(invoice);
    if(invoice?.reference===selected)status(t('Independent reversal decision saved','স্বতন্ত্র রিভার্সাল সিদ্ধান্ত সংরক্ষিত'));
   }
  }catch(e){if(chosen===selectionSeq&&invoice?.reference===selected)handleError(e);}
  finally{busy=false;$('ssContraSubmit').disabled=false;lock('ssContraForm',false);}
 });
 $('ssPrev').addEventListener('click',()=>{if(page>1){page--;load();}});
 $('ssNext').addEventListener('click',()=>{if(page<pages){page++;load();}});
})();
