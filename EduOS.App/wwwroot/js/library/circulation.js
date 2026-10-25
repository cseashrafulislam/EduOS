(() => {
'use strict';
const root=document.getElementById('libraryCirculationApp');if(!root)return;
const el=id=>document.getElementById(id);
const state={page:1,pages:1,rows:[],request:0,busy:false};
function message(text,ok=false){const n=el('circulationAlert');n.className='alert alert-'+(ok?'success':'danger');n.textContent=text;n.focus();}
async function api(url,method='GET',body){
 const headers={Accept:'application/json'};
 if(method!=='GET'){
  const token=document.querySelector('meta[name="request-verification-token"]')?.content;
  if(!token)throw Error('Security token missing. Reload the page.');
  headers.RequestVerificationToken=token;headers['Content-Type']='application/json';
 }
 const res=await fetch(url,{method,credentials:'same-origin',cache:'no-store',headers,
  ...(body===undefined?{}:{body:JSON.stringify(body)})});
 const data=await res.json().catch(()=>null);
 if(!res.ok||!data?.success)throw Error(data?.message||'Library request failed.');
 return data.data;
}
function cell(tr,value){const td=document.createElement('td');td.textContent=value===null||value===undefined||value===''?'—':String(value);tr.append(td);return td;}
function label(state){return state===1?'Issued':state===2?'Returned':state===3?'Lost':state===4?'Damaged':'Other';}
function render(){
 const body=el('circulationRows');body.replaceChildren();
 for(const row of state.rows){
  const i=row.issue,tr=document.createElement('tr');
  [i.studentName,i.bookTitle,i.accessionNumber,row.campusId,i.issueDate,i.dueDate,i.returnDate,label(i.state),row.daysOverdue].forEach(v=>cell(tr,v));
  const td=cell(tr,'');
  if(i.state===1){
   const b=document.createElement('button');b.type='button';b.className='btn btn-sm btn-outline-primary';
   b.textContent='Return';b.disabled=state.busy;
   b.addEventListener('click',()=>closeIssue(row,b));td.append(b);
  }
  body.append(tr);
 }
 if(!state.rows.length){const tr=document.createElement('tr');cell(tr,'No matching circulation records.').colSpan=10;body.append(tr);}
 el('circulationPage').textContent='Page '+state.page+' / '+state.pages;
 el('circulationPrevious').disabled=state.page<=1;
 el('circulationNext').disabled=state.page>=state.pages;
}
async function load(){
 const seq=++state.request;
 const query=new URLSearchParams({page:String(state.page),pageSize:'20',search:el('circulationSearch').value.trim(),
  status:el('circulationStatus').value});
 const campus=el('circulationCampus').value.trim();
 if(campus){
  if(!/^[1-9]\d*$/.test(campus)){message('Campus ID must be a positive whole number.');return;}
  query.set('campusId',campus);
 }
 try{
  const result=await api('/api/library/circulation?'+query);
  if(seq!==state.request)return;
  state.rows=result?.items||[];state.pages=Math.max(1,result?.totalPages||1);
  el('circulationCount').textContent='('+Number(result?.totalCount||0)+')';render();
 }catch(e){if(seq===state.request){state.rows=[];state.pages=1;render();message(e.message);}}
}
async function closeIssue(row,button){
 if(state.busy||!window.confirm('Return '+row.issue.bookTitle+' for '+row.issue.studentName+'?'))return;
 state.busy=true;button.disabled=true;
 try{
  const payload={rowVersion:row.issue.rowVersion};
  const date=el('circulationReturnDate').value;
  if(date)payload.returnDate=date;
  await api('/api/library/issues/'+encodeURIComponent(row.issue.reference)+'/close','POST',payload);
  message('Return recorded and copy availability updated.',true);
 }catch(e){message(e.message);}
 finally{state.busy=false;await load();}
}
function csv(){
 const headers=['Student','Book','Accession','Campus','Issue date','Due date','Return date','Status','Overdue days'];
 const rows=state.rows.map(r=>{const i=r.issue;return [i.studentName,i.bookTitle,i.accessionNumber,r.campusId,
  i.issueDate,i.dueDate,i.returnDate,label(i.state),r.daysOverdue];});
 const safe=v=>{let s=String(v??'');if(/^[=+@\-\t\r]/.test(s))s="'"+s;return '"'+s.replaceAll('"','""')+'"';};
 const content=String.fromCharCode(0xfeff)+[headers,...rows].map(r=>r.map(safe).join(',')).join('\r\n');
 const url=URL.createObjectURL(new Blob([content],{type:'text/csv;charset=utf-8'}));
 const a=document.createElement('a');a.href=url;a.download='library-circulation.csv';a.click();
 setTimeout(()=>URL.revokeObjectURL(url),1000);
}
el('circulationFilters').addEventListener('submit',e=>{e.preventDefault();state.page=1;load();});
el('circulationPrevious').addEventListener('click',()=>{if(state.page>1){state.page--;load();}});
el('circulationNext').addEventListener('click',()=>{if(state.page<state.pages){state.page++;load();}});
el('circulationCsv').addEventListener('click',csv);
el('circulationPrint').addEventListener('click',()=>window.print());
load();
})();
