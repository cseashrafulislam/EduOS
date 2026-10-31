(() => {
'use strict';
const root=document.getElementById('poCommitWorkspace');if(!root)return;
const $=id=>document.getElementById(id);
const bn=(document.documentElement.lang||'').toLowerCase().startsWith('bn');
const en={title:'Approved supplier commitments',orders:'Purchase orders',print:'Print current page',
 export:'Export CSV',help:'Current approved PO amounts and expected delivery aging only. Received, returned and outstanding quantities cannot be determined until Goods Receipt integration. Print covers this page; CSV is limited to 500 filtered orders.',
 search:'Order or supplier',campus:'Campus ID',agingDate:'Aging date',overdueOnly:'Overdue only',
 filter:'Filter',number:'Order',supplier:'Supplier',approved:'Approved UTC',expected:'Expected',
 schedule:'Schedule',days:'Days overdue',lines:'Lines',amount:'Approved commitment',
 approvalStatus:'Approval status',Approved:'Approved',CancelRequested:'Cancellation requested',
 supplierPromised:'Supplier promised',supplierReference:'Confirmation reference',
 previous:'Previous',next:'Next',page:'Page',of:'of',empty:'No approved commitments.',
 failed:'Unable to load commitments.',Unscheduled:'Unscheduled',Overdue:'Overdue',
 DueToday:'Due today',Upcoming:'Upcoming'};
const bd={title:'অনুমোদিত সরবরাহ প্রতিশ্রুতি',orders:'ক্রয়াদেশ',print:'এই পৃষ্ঠা প্রিন্ট',
 export:'CSV রপ্তানি',help:'এটি বর্তমান অনুমোদিত ক্রয়াদেশের মূল্য ও সম্ভাব্য সরবরাহের সময় দেখায়। পণ্য গ্রহণ, ফেরত বা অবশিষ্ট পরিমাণ এখানে গণনা করা হয় না। প্রিন্ট শুধু এই পৃষ্ঠা; CSV সর্বোচ্চ ৫০০টি ফিল্টারকৃত ক্রয়াদেশ।',
 search:'ক্রয়াদেশ বা সরবরাহকারী',campus:'ক্যাম্পাস আইডি',agingDate:'হিসাবের তারিখ',
 overdueOnly:'শুধু বিলম্বিত',filter:'ফিল্টার',number:'ক্রয়াদেশ',supplier:'সরবরাহকারী',
 approved:'অনুমোদনের সময় (UTC)',expected:'প্রত্যাশিত তারিখ',schedule:'অবস্থা',
 days:'বিলম্বের দিন',lines:'লাইন',amount:'অনুমোদিত মূল্য',previous:'আগের',next:'পরের',
 approvalStatus:'অনুমোদনের অবস্থা',Approved:'অনুমোদিত',CancelRequested:'বাতিলের আবেদন চলমান',
 supplierPromised:'সরবরাহকারীর প্রতিশ্রুত তারিখ',supplierReference:'নিশ্চিতকরণ রেফারেন্স',
 page:'পৃষ্ঠা',of:'এর',empty:'কোনো অনুমোদিত ক্রয়াদেশ নেই।',failed:'ক্রয়াদেশ পাওয়া যায়নি।',
 Unscheduled:'তারিখ নেই',Overdue:'বিলম্বিত',DueToday:'আজ নির্ধারিত',Upcoming:'আসন্ন'};
const t=k=>(bn?bd:en)[k]||k;
root.querySelectorAll('[data-i18n]').forEach(el=>{el.textContent=t(el.dataset.i18n);});
let page=1,pages=1,epoch=0;
const base='/api/procurement/purchase-orders/commitments';
function params(obj){return '?'+new URLSearchParams(Object.entries(obj).filter(([,v])=>v!==''&&v!==null&&v!==undefined)).toString();}
function filters(){return {campusId:$('poCommitCampus').value,agingDate:$('poCommitDate').value,
 overdueOnly:$('poCommitOverdue').checked,search:$('poCommitSearch').value.trim()};}
function cell(tr,value){const td=document.createElement('td');td.textContent=String(value??'—');tr.append(td);}
function error(message){$('poCommitAlert').className='alert alert-danger';$('poCommitAlert').textContent=message;}
function date(value){return value?new Date(value).toLocaleDateString(bn?'bn-BD':'en-BD'):'—';}
async function load(){
 const seq=++epoch,f=filters();
 try{
  const response=await fetch(base+params({page,pageSize:25,...f}),
   {credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
  const body=await response.json().catch(()=>null);
  if(!response.ok||!body?.success)throw Error(body?.message||t('failed'));
  if(seq!==epoch)return;
  $('poCommitAlert').className='d-none';$('poCommitAlert').textContent='';
  const data=body.data;pages=data.totalPages||1;$('poCommitRows').replaceChildren();
  for(const x of data.items||[]){
   const tr=document.createElement('tr');
   for(const v of [x.orderNumber,x.supplierName||x.supplierId,x.campusId,
    t(x.stateCode),date(x.approvedAt),date(x.expectedDeliveryDate),t(x.deliverySchedule),
    x.daysOverdue,x.lineCount,Number(x.approvedAmount).toLocaleString(bn?'bn-BD':'en-BD',
      {minimumFractionDigits:2,maximumFractionDigits:2})+' '+x.currencyCode,
    date(x.supplierPromisedDeliveryDate),x.supplierConfirmationReference||'—'])cell(tr,v);
   $('poCommitRows').append(tr);
  }
  if(!data.items?.length){const tr=document.createElement('tr'),td=document.createElement('td');
   td.colSpan=12;td.textContent=t('empty');tr.append(td);$('poCommitRows').append(tr);}
  $('poCommitPage').textContent=t('page')+' '+page+' '+t('of')+' '+pages;
  $('poCommitPrev').disabled=page<=1;$('poCommitNext').disabled=page>=pages;
  $('poCommitScope').textContent=t('title')+' · '+(f.agingDate||new Date().toISOString().slice(0,10))+
   ' · '+page+'/'+pages+' · '+data.totalCount;
 }catch(e){if(seq===epoch)error(e.message);}
}
$('poCommitFilters').addEventListener('submit',e=>{e.preventDefault();page=1;load();});
$('poCommitPrev').addEventListener('click',()=>{if(page>1){page--;load();}});
$('poCommitNext').addEventListener('click',()=>{if(page<pages){page++;load();}});
$('poCommitExport').addEventListener('click',()=>{window.location.assign(base+'/export'+params(filters()));});
$('poCommitPrint').addEventListener('click',()=>window.print());
$('poCommitHelp').addEventListener('click',()=>{
 const open=!$('poCommitHelpText').classList.toggle('d-none');
 $('poCommitHelp').setAttribute('aria-expanded',String(open));
});
window.addEventListener('pagehide',()=>{++epoch;$('poCommitRows').replaceChildren();});
load();
})();
