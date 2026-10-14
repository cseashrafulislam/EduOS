(() => {
'use strict';
const el = id => document.getElementById(id);
const state = {page:1, rows:[], selected:null};
const notice = message => {el('mySubmissionAlert').className='alert alert-info';el('mySubmissionAlert').textContent=message;};
async function get(url) {const r=await fetch(url);const j=await r.json();if(!r.ok||!j.success)throw Error(j.message);return j.data;}
async function load() {
 try {
  const d=await get('/api/lms/assignment-work/mine?page='+state.page+'&search='+encodeURIComponent(el('mySubmissionSearch').value));
  state.rows=d.items||[];state.pages=Math.max(1,d.totalPages||1);el('mySubmissionCount').textContent=d.totalCount+' assignments';
  const tbody=el('mySubmissionRows');tbody.replaceChildren();
  for(const row of state.rows) {
   const tr=document.createElement('tr');
   for(const value of [row.courseTitle,row.assignmentTitle,row.dueAt,row.state,row.marks]) {
    const td=document.createElement('td');td.textContent=value??'—';tr.append(td);
   }
   const td=document.createElement('td'),b=document.createElement('button');
   b.type='button';b.textContent=row.canSubmit?'Submit':'View files';
   b.disabled=!row.canSubmit&&!row.submissionId;
   b.onclick=()=>row.canSubmit?choose(row):files(row.submissionId);
   td.append(b);tr.append(td);tbody.append(tr);
  }
  el('mySubmissionPage').textContent='Page '+state.page+' / '+state.pages;
  el('mySubmissionPrev').disabled=state.page<=1;el('mySubmissionNext').disabled=state.page>=state.pages;
 } catch(error) {notice(error.message);}
}

function choose(row) {
 state.selected=row;el('mySubmissionHeading').textContent=row.assignmentTitle;
 el('mySubmissionText').value='';el('mySubmissionFiles').value='';
 el('mySubmissionForm').hidden=false;
}
async function files(id) {
 try {
  const rows=await get('/api/lms/assignment-work/submissions/'+id+'/files');
  const body=el('mySubmissionFileRows');body.replaceChildren();
  for(const row of rows) {
   const tr=document.createElement('tr');
   for(const value of [row.fileName,row.fileAssetReference]) {
    const td=document.createElement('td');td.textContent=value;tr.append(td);
   }
   body.append(tr);
  }
  el('mySubmissionFilePanel').hidden=false;
 } catch(error) {notice(error.message);}
}
async function post(data) {
 const token=document.querySelector('meta[name="request-verification-token"]')?.content;
 if(!token)throw Error('Security token missing.');
 const r=await fetch('/api/lms/assignments/submit',{method:'POST',credentials:'same-origin',
  headers:{'Content-Type':'application/json',RequestVerificationToken:token},body:JSON.stringify(data)});
 const j=await r.json();if(!r.ok||!j.success)throw Error(j.message||'Submission failed.');
 return j.data;
}
el('mySubmissionForm').onsubmit=async event=>{
 event.preventDefault();if(!state.selected)return;
 const refs=el('mySubmissionFiles').value.trim().split(/[,;\s]+/).filter(Boolean);
 const guid=/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;
 if(refs.length>10||refs.some(x=>!guid.test(x))||new Set(refs.map(x=>x.toLowerCase())).size!==refs.length)
  return notice('Maximum 10 distinct, valid file GUIDs.');
 if(!el('mySubmissionText').value.trim()&&!refs.length)return notice('Answer or file required.');
 try{
  await post({clientRequestId:crypto.randomUUID(),assignmentReference:state.selected.assignmentReference,
   courseEnrollmentId:state.selected.courseEnrollmentId,submissionText:el('mySubmissionText').value.trim(),fileAssetReferences:refs});
  el('mySubmissionForm').hidden=true;state.selected=null;notice('Submitted.');await load();
 }catch(error){notice(error.message);}
};
el('mySubmissionCancel').onclick=()=>{el('mySubmissionForm').hidden=true;};
el('mySubmissionRefresh').onclick=load;
el('mySubmissionPrev').onclick=()=>{state.page--;load();};
el('mySubmissionNext').onclick=()=>{state.page++;load();};
el('mySubmissionCsv').onclick=()=>{
 const lines=[['Course','Assignment','Due','Status'],...state.rows.map(x=>[x.courseTitle,x.assignmentTitle,x.dueAt,x.state])];
 const quote=v=>{let t=String(v??'');if(/^[=+@-]/.test(t))t="'"+t;return '"'+t.replace(/"/g,'""')+'"';};
 const blob=new Blob([lines.map(x=>x.map(quote).join(',')).join('\r\n')],{type:'text/csv'});
 const a=document.createElement('a'),url=URL.createObjectURL(blob);a.href=url;a.download='submissions.csv';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
};
el('mySubmissionPrint').onclick=()=>window.print();
load();
})();
