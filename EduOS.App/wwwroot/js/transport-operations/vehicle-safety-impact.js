(() => {
'use strict';
const el = id => document.getElementById(id);
if (!el('transportSafetyImpactPanel')) return;
const endpoint = '/api/transport/vehicle-cases/safety-impact';
const date = el('transportSafetyImpactDate'), search = el('transportSafetyImpactSearch');
const attention = el('transportSafetyImpactAttention');
const status = el('transportSafetyImpactStatus'), body = el('transportSafetyImpactRows');
const exportButton = el('transportSafetyImpactExport'), printButton = el('transportSafetyImpactPrint');
const prev = el('transportSafetyImpactPrev'), next = el('transportSafetyImpactNext');
const localToday = () => { const d = new Date(); return [d.getFullYear(),String(d.getMonth()+1).padStart(2,'0'),String(d.getDate()).padStart(2,'0')].join('-'); };
date.value = localToday(); date.min = date.value;
const max = new Date(); max.setDate(max.getDate()+90);
date.max = [max.getFullYear(),String(max.getMonth()+1).padStart(2,'0'),String(max.getDate()).padStart(2,'0')].join('-');
let page = 1, pages = 1, generation = 0, loaded = '', rows = [];
const filters = () => new URLSearchParams({
 operatingDate:date.value,search:search.value.trim(),attentionOnly:String(attention.checked)
}).toString();
const cell = value => { const td=document.createElement('td');td.textContent=value == null || value === '' ? '—' : String(value);return td; };
const invalidate = () => { generation++;loaded='';rows=[];body.replaceChildren();exportButton.disabled=true;printButton.disabled=true;prev.disabled=true;next.disabled=true; };
async function api(url) {
 const response=await fetch(url,{credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
 const data=await response.json().catch(()=>null);
 if(!response.ok || !data?.success) throw Error(data?.message || 'Safety impact request failed.');
 return data.data;
}
async function load() {
 invalidate();
 const serial=generation, query=filters(), url=new URLSearchParams(query);
 url.set('page',String(page));url.set('pageSize','25');
 status.textContent='Loading authorized safety impact…';
 try {
  const result=await api(endpoint+'?'+url);
  if(serial!==generation || query!==filters()) return;
  rows=Array.isArray(result.items)?result.items:[];
  for(const item of rows) {
   const tr=document.createElement('tr');
   for(const value of [item.campusName,item.vehicleNumber,item.reservedRiders,item.capacity,item.openCases,
     item.highSeverityCases,item.crossCampusCaseHold?'Yes':'No',item.safetyState]) tr.append(cell(value));
   body.append(tr);
  }
  if(!rows.length){const tr=document.createElement('tr'),td=cell('No matching vehicles / কোনো গাড়ি পাওয়া যায়নি');td.colSpan=8;tr.append(td);body.append(tr);}
  pages=Math.max(1,Math.ceil((Number(result.totalCount)||0)/25));
  el('transportSafetyImpactPage').textContent='Page '+page+' of '+pages+' · '+result.totalCount+' vehicles';
  prev.disabled=page<=1;next.disabled=page>=pages;
  loaded=query;exportButton.disabled=!rows.length;printButton.disabled=!rows.length;
  status.textContent='Open safety cases hold the vehicle; no dispatch is authorized by this report.';
 } catch(error){if(serial===generation)status.textContent=error.message;}
}
for(const control of [date,search,attention])control.addEventListener('input',()=>{invalidate();status.textContent='Filters changed. Apply to refresh.';});
el('transportSafetyImpactFilters').addEventListener('submit',event=>{event.preventDefault();if(!date.reportValidity())return;page=1;load();});
prev.addEventListener('click',()=>{if(page>1){page--;load();}});
next.addEventListener('click',()=>{if(page<pages){page++;load();}});
exportButton.addEventListener('click',async()=>{
 if(loaded!==filters())return;
 try{
  const response=await fetch(endpoint+'/export?'+loaded,{credentials:'same-origin',cache:'no-store'});
  if(!response.ok){const error=await response.json().catch(()=>null);throw Error(error?.message || 'Export failed.');}
  const blob=await response.blob(),url=URL.createObjectURL(blob),anchor=document.createElement('a');
  anchor.href=url;anchor.download='transport-vehicle-safety-impact.csv';anchor.click();
  setTimeout(()=>URL.revokeObjectURL(url),1000);
 }catch(error){status.textContent=error.message;}
});
printButton.addEventListener('click',async()=>{
 if(loaded!==filters()||!rows.length)return;
 try{
  const query=new URLSearchParams(loaded);query.set('page',String(page));query.set('pageSize','25');
  const fresh=await api(endpoint+'?'+query);
  if(fresh.totalCount<rows.length||fresh.items.length!==rows.length||
   fresh.items.some((item,i)=>JSON.stringify(item)!==JSON.stringify(rows[i])))
   throw Error('Safety impact changed. Refresh before printing.');
  const win=window.open('','_blank','width=900,height=700');
  if(!win){status.textContent='Allow print window.';return;}
  const doc=win.document;doc.title='Vehicle safety impact';
  const heading=doc.createElement('h2');heading.textContent='Vehicle safety impact — '+date.value;doc.body.append(heading);
  const table=doc.createElement('table');table.border='1';table.cellPadding='5';
  const head=doc.createElement('tr');
  for(const name of ['Campus','Vehicle','Reserved','Capacity','Open cases','High','Other-campus hold','Safety state']){
   const th=doc.createElement('th');th.textContent=name;head.append(th);
  }
  table.append(head);
  for(const item of fresh.items){const tr=doc.createElement('tr');
   for(const value of [item.campusName,item.vehicleNumber,item.reservedRiders,item.capacity,item.openCases,
    item.highSeverityCases,item.crossCampusCaseHold?'Yes':'No',item.safetyState])tr.append(cell(value));
   table.append(tr);
  }
  doc.body.append(table);win.focus();win.print();
 }catch(error){status.textContent=error.message;}
});
load();
})();
