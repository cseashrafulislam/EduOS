(() => {
 'use strict';
 const root=document.getElementById('supplierPayableAging');if(!root)return;
 const $=id=>document.getElementById(id);
 const bn=(document.documentElement.lang||'').toLowerCase().startsWith('bn');
 if(bn)root.querySelectorAll('[data-bn]').forEach(x=>{x.textContent=x.dataset.bn;});
 const t=(en,bangla)=>bn?bangla:en;
 const money=v=>Number(v).toLocaleString(bn?'bn-BD':'en-BD',{minimumFractionDigits:2,maximumFractionDigits:2});
 const cell=v=>{const td=document.createElement('td');td.textContent=String(v??'');return td;};
 const base='/api/procurement/supplier-invoices/aging';
 let page=1,pages=1,seq=0;
 const params=()=>new URLSearchParams({dueDateReference:$('paDate').value,
  overdueOnly:String($('paOverdue').checked),page:String(page),pageSize:'25'});
 function status(message,error=false){const e=$('paStatus');e.className='small '+(error?'text-danger':'text-muted');e.textContent=message;}
 async function load(){
  const n=++seq;$('paPrev').disabled=true;$('paNext').disabled=true;
  try{
   const r=await fetch(base+'?'+params(),{credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
   const result=await r.json();
   if(!r.ok||!result?.success)throw new Error(result?.message||'Report unavailable');
   if(n!==seq)return;
   const data=result.data;pages=Math.max(1,data.totalPages||1);$('paRows').replaceChildren();
   let gross=0;
   for(const x of data.items||[]){
    const tr=document.createElement('tr');gross+=Number(x.grossPostedAmount);
    for(const v of [x.supplierInvoiceNumber,x.orderNumber,x.supplierId,x.campusId,x.dueDate,
     x.daysOverdue,x.agingBucket,money(x.grossPostedAmount),
     x.reversalPending?t('Pending','অপেক্ষমাণ'):'—'])tr.appendChild(cell(v));
    $('paRows').appendChild(tr);
   }
   if(!(data.items||[]).length){const tr=document.createElement('tr'),td=cell(t('No posted payables','পোস্টকৃত দায় নেই'));td.colSpan=9;tr.appendChild(td);$('paRows').appendChild(tr);}
   $('paPage').textContent=page+' / '+pages;
   $('paPrev').disabled=page<=1;$('paNext').disabled=page>=pages;
   status(t('Page gross BDT ','পৃষ্ঠার মোট দায় ৳')+money(gross)+t(' · Settlement unverified',' · পরিশোধ যাচাই হয়নি'));
  }catch(e){if(n===seq)status(e.message,true);}
 }
 $('paForm').addEventListener('submit',e=>{e.preventDefault();page=1;load();});
 $('paPrev').addEventListener('click',()=>{if(page>1){page--;load();}});
 $('paNext').addEventListener('click',()=>{if(page<pages){page++;load();}});
 $('paExport').addEventListener('click',()=>{const p=params();p.delete('page');p.delete('pageSize');window.location.assign(base+'/export?'+p);});
 $('paDate').value=new Date().toISOString().slice(0,10);
 load();
})();
