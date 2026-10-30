(() => {
'use strict';
const el = id => document.getElementById(id);
const state = {page:1, rows:[], selected:null, uploadKeys:new Map(), submitId:null, downloading:new Set(), submitting:false};
const initialTenant=document.body?.dataset.authTenantId||'';
const initialSession=document.body?.dataset.authSessionReference||'';
const sessionValid=()=>document.body?.dataset.authTenantId===initialTenant&&
 document.body?.dataset.authSessionReference===initialSession&&
 window.EduOSSessionTransitionPending?.()!==true;
function clearSensitive(){
 state.rows=[];state.selected=null;state.uploadKeys.clear();state.submitId=null;
 for(const controller of state.downloading)controller.abort();state.downloading.clear();
 el('mySubmissionForm').hidden=true;el('mySubmissionFilePanel').hidden=true;
 el('mySubmissionRows').replaceChildren();el('mySubmissionFileRows').replaceChildren();
 el('mySubmissionText').value='';el('mySubmissionUploadFiles').value='';
}
const notice = message => {el('mySubmissionAlert').className='alert alert-info';el('mySubmissionAlert').textContent=message;};
async function get(url) {if(!sessionValid())throw Error('Session changed. Reload the page.');
 const r=await fetch(url,{credentials:'same-origin',cache:'no-store'});const j=await r.json();
 if(!sessionValid()){clearSensitive();throw Error('Session changed. Reload the page.');}
 if(!r.ok||!j.success)throw Error(j.message);return j.data;}
async function load() {
 try {
  el('mySubmissionFilePanel').hidden=true;
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
 state.selected=row;state.submitId=crypto.randomUUID();state.uploadKeys.clear();
 el('mySubmissionHeading').textContent=row.assignmentTitle;
 el('mySubmissionText').value='';el('mySubmissionUploadFiles').value='';
 el('mySubmissionForm').hidden=false;
}
async function files(id) {
 try {
  const rows=await get('/api/lms/assignment-work/submissions/'+id+'/files');
  const body=el('mySubmissionFileRows');body.replaceChildren();
  for(const row of rows) {
   const tr=document.createElement('tr');
   const name=document.createElement('td');name.textContent=row.fileName;tr.append(name);
   const action=document.createElement('td'),button=document.createElement('button');
   button.type='button';button.className='btn btn-outline-primary btn-sm';
   button.textContent=(document.documentElement.lang||'').startsWith('bn')?'ডাউনলোড':'Download';
   button.onclick=()=>download(id,row.fileAssetReference,row.fileName,button);
   action.append(button);tr.append(action);
   body.append(tr);
  }
  el('mySubmissionFilePanel').hidden=false;
 } catch(error) {notice(error.message);}
}
async function download(submissionId, reference, name, button) {
 if(!sessionValid())return clearSensitive();
 const controller=new AbortController();state.downloading.add(controller);button.disabled=true;
 try {
  const url='/api/lms/assignment-work/submissions/'+submissionId+'/files/'+encodeURIComponent(reference)+'/download';
  const response=await fetch(url,{credentials:'same-origin',cache:'no-store',signal:controller.signal});
  if(!response.ok) {
   const result=await response.json().catch(()=>({}));throw Error(result.message||'Download unavailable.');
  }
  const blob=await response.blob();if(controller.signal.aborted||!sessionValid())return clearSensitive();
  const safeName=String(name||'submission').replace(/[\\/\u0000-\u001f]/g,'_').slice(0,255);
  const objectUrl=URL.createObjectURL(blob),link=document.createElement('a');
  link.href=objectUrl;link.download=safeName;document.body.append(link);link.click();link.remove();
  setTimeout(()=>URL.revokeObjectURL(objectUrl),1000);
 }catch(error){if(error.name!=='AbortError')notice(error.message);}
 finally{state.downloading.delete(controller);button.disabled=false;}
}
async function upload(row, file, requestId) {
 if(!sessionValid())throw Error('Session changed. Reload the page.');
 const token=document.querySelector('meta[name="request-verification-token"]')?.content;
 if(!token)throw Error('Security token missing.');
 const form=new FormData();form.append('file',file);form.append('requestId',requestId);
 const url='/api/lms/assignment-work/assignments/'+encodeURIComponent(row.assignmentReference)+
  '/enrollments/'+row.courseEnrollmentId+'/files';
 const response=await fetch(url,{method:'POST',credentials:'same-origin',cache:'no-store',
  headers:{RequestVerificationToken:token},body:form});
 const result=await response.json();if(!sessionValid())throw Error('Session changed. Reload the page.');
 if(!response.ok||!result.success)throw Error(result.message||'Upload failed.');
 return result.data;
}
async function post(data) {
 if(!sessionValid())throw Error('Session changed. Reload the page.');
 const token=document.querySelector('meta[name="request-verification-token"]')?.content;
 if(!token)throw Error('Security token missing.');
 const r=await fetch('/api/lms/assignments/submit',{method:'POST',credentials:'same-origin',
  headers:{'Content-Type':'application/json',RequestVerificationToken:token},body:JSON.stringify(data)});
 const j=await r.json();if(!sessionValid())throw Error('Session changed. Reload the page.');
 if(!r.ok||!j.success)throw Error(j.message||'Submission failed.');
 return j.data;
}
el('mySubmissionForm').onsubmit=async event=>{
 event.preventDefault();if(!state.selected||state.submitting)return;
 if(!sessionValid()){clearSensitive();return notice('Session changed. Reload the page.');}
 const files=Array.from(el('mySubmissionUploadFiles').files||[]);
 if(files.length>10||files.some(x=>x.size<1||x.size>5242880))
  return notice('Choose up to 10 files, 5 MB each.');
 if(!el('mySubmissionText').value.trim()&&!files.length)return notice('Answer or file required.');
 state.submitting=true;el('mySubmissionSend').disabled=true;
 try{
  const refs=[];
  for(let i=0;i<files.length;i++) {
   const file=files[i],key=i+':'+file.name+':'+file.size+':'+file.lastModified;
   if(!state.uploadKeys.has(key))state.uploadKeys.set(key,{requestId:crypto.randomUUID(),reference:null});
   const pending=state.uploadKeys.get(key);
   if(!pending.reference) {
    notice('Scanning attachment '+(i+1)+'/'+files.length+'…');
    const uploaded=await upload(state.selected,file,pending.requestId);pending.reference=uploaded.reference;
   }
   refs.push(pending.reference);
  }
  await post({clientRequestId:state.submitId,assignmentReference:state.selected.assignmentReference,
   courseEnrollmentId:state.selected.courseEnrollmentId,submissionText:el('mySubmissionText').value.trim(),fileAssetReferences:refs});
  el('mySubmissionForm').hidden=true;state.selected=null;state.uploadKeys.clear();notice('Submitted.');await load();
 }catch(error){notice(error.message);}
 finally{state.submitting=false;el('mySubmissionSend').disabled=false;}
};
el('mySubmissionCancel').onclick=()=>{if(!state.submitting){el('mySubmissionForm').hidden=true;state.selected=null;state.uploadKeys.clear();}};
el('mySubmissionFileClose').onclick=()=>{el('mySubmissionFilePanel').hidden=true;};
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
new MutationObserver(()=>{if(!sessionValid())clearSensitive();})
 .observe(document.body,{attributes:true,attributeFilter:['data-auth-tenant-id','data-auth-session-reference']});
window.addEventListener('pagehide',clearSensitive);
load();
})();
