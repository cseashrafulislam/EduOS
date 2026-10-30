(() => {
'use strict';
const root=document.getElementById('assignmentFilesApp');if(!root)return;
const $=id=>document.getElementById(id),author=root.dataset.author==='true';
let courses=[],assignments=[],rows=[],editing=null,sequence=0,pendingUpload=null,coursePage=0,coursePages=1;
const initialTenant=document.body?.dataset.authTenantId||'';
const initialSession=document.body?.dataset.authSessionReference||'';
const downloading=new Set();
const sessionValid=()=>document.body?.dataset.authTenantId===initialTenant&&
 document.body?.dataset.authSessionReference===initialSession&&
 window.EduOSSessionTransitionPending?.()!==true;
function clearSensitive(){
 courses=[];assignments=[];rows=[];editing=null;pendingUpload=null;sequence++;coursePage=0;coursePages=1;
 for(const controller of downloading)controller.abort();downloading.clear();
 $('assignmentFilesRows').replaceChildren();$('assignmentFilesCount').textContent='';
 $('assignmentFilesAssignment').replaceChildren();$('assignmentFilesCourse').replaceChildren();
 $('assignmentFilesMoreCourses').hidden=true;
 if(author)$('assignmentFilesForm').reset();
}
const opt=(value,text)=>{const n=document.createElement('option');n.value=value;n.textContent=text;return n;};
const notify=(kind,message)=>{const n=$('assignmentFilesAlert');n.className='alert alert-'+kind;n.textContent=message;};
const selected=()=>$('assignmentFilesAssignment').value;
async function api(url,method='GET',body){
 if(!sessionValid())throw Error('Session changed. Reload.');
 const headers={Accept:'application/json'};
 if(body!==undefined)headers['Content-Type']='application/json';
 if(method!=='GET'){
  const token=document.querySelector('meta[name="request-verification-token"]')?.content;
  if(!token)throw Error('Missing security token. Reload.');
  headers.RequestVerificationToken=token;
 }
 const r=await fetch(url,{method,credentials:'same-origin',cache:'no-store',headers,...(body===undefined?{}:{body:JSON.stringify(body)})});
 const j=await r.json().catch(()=>null);
 if(!sessionValid()){clearSensitive();throw Error('Session changed. Reload.');}
 if(!r.ok||!j?.success)throw Error(j?.message||'Request failed.');
 return j.data;
}
function fill(id,items,placeholder){
 const n=$(id),previous=n.value;n.replaceChildren(opt('',placeholder));
 items.forEach(x=>n.append(opt(x.value,x.label)));
 if(items.some(x=>x.value===previous))n.value=previous;
}
function reset(){
 editing=null;pendingUpload=null;
 if(author){$('assignmentFilesForm').reset();$('assignmentFilesUpload').required=true;
  $('assignmentFilesSave').textContent='Save';}
}
async function download(row,button){
 if(!sessionValid()){clearSensitive();return;}
 const controller=new AbortController();downloading.add(controller);button.disabled=true;
 try{
  const response=await fetch('/api/lms/assignment-attachments/'+row.id+'/download',
   {credentials:'same-origin',cache:'no-store',signal:controller.signal});
  if(!response.ok){const j=await response.json().catch(()=>({}));throw Error(j.message||'Download unavailable.');}
  const blob=await response.blob();if(controller.signal.aborted||!sessionValid())return clearSensitive();
  const name=String(row.fileName||'handout').replace(/[\\/\u0000-\u001f]/g,'_').slice(0,255);
  const url=URL.createObjectURL(blob),a=document.createElement('a');
  a.href=url;a.download=name;document.body.append(a);a.click();a.remove();
  setTimeout(()=>URL.revokeObjectURL(url),1000);
 }catch(e){if(e.name!=='AbortError')notify('danger',e.message);}
 finally{downloading.delete(controller);button.disabled=false;}
}
function draw(){
 const body=$('assignmentFilesRows');body.replaceChildren();
 $('assignmentFilesCount').textContent=rows.length+' verified file(s)';
 for(const row of rows){
  const tr=document.createElement('tr');
  for(const value of [row.title||'—',row.fileName]){
   const td=document.createElement('td');td.textContent=value;tr.append(td);
  }
  const downloadCell=document.createElement('td');
  if(row.fileDownloadable){
   const button=document.createElement('button');button.type='button';
   button.className='btn btn-sm btn-outline-primary';button.textContent='Download';
   button.onclick=()=>download(row,button);downloadCell.append(button);
  }else downloadCell.textContent='Unavailable';
  tr.append(downloadCell);
  if(author){
   const td=document.createElement('td');
   for(const [label,action] of [['Edit',()=>{
    editing=row;pendingUpload=null;$('assignmentFilesUpload').value='';
    $('assignmentFilesUpload').required=false;$('assignmentFilesReference').value=row.fileAssetReference;
    $('assignmentFilesTitle').value=row.title||'';$('assignmentFilesSave').textContent='Update';
   }],['Remove',async()=>{
    if(!confirm('Remove this file link?'))return;
    try{await api('/api/lms/assignment-attachments/'+row.id+'/remove','POST',{rowVersion:row.rowVersion});
     await files();notify('success','Attachment removed.');
    }catch(e){notify('danger',e.message);}
   }]]){
    const b=document.createElement('button');b.type='button';b.className='btn btn-sm btn-outline-primary me-1';
    b.textContent=label;b.addEventListener('click',action);td.append(b);
   }
   tr.append(td);
  }
  body.append(tr);
 }
 if(!rows.length){
  const tr=document.createElement('tr'),td=document.createElement('td');td.colSpan=author?4:3;
  td.className='p-3 text-muted';td.textContent='No attachments to display.';tr.append(td);body.append(tr);
 }
}
async function files(){
 const ref=selected(),seq=++sequence;rows=[];reset();draw();if(!ref)return;
 try{const data=await api('/api/lms/assignment-attachments/'+encodeURIComponent(ref));
  if(seq===sequence){rows=Array.isArray(data)?data:[];draw();}
 }catch(e){if(seq===sequence)notify('danger',e.message);}
}
async function chooseCourse(){
 const ref=$('assignmentFilesCourse').value;assignments=[];
 fill('assignmentFilesAssignment',[],'Choose an assignment');
 if(!ref)return files();
 try{const data=await api('/api/lms/courses/'+encodeURIComponent(ref));
  assignments=(data?.assignments||[]).filter(x=>author||x.isPublished);
  fill('assignmentFilesAssignment',assignments.map(x=>({value:x.reference,label:x.title})),'Choose an assignment');
  await files();
 }catch(e){notify('danger',e.message);}
}
async function loadMoreCourses(){
 const button=$('assignmentFilesMoreCourses');if(coursePage>=coursePages)return;
 button.disabled=true;
 try{
  const data=await api('/api/lms/courses?page='+(coursePage+1)+'&pageSize=50');
  const current=$('assignmentFilesCourse').value;
  coursePage=data.page;coursePages=Math.max(1,data.totalPages||1);
  const existing=new Set(courses.map(x=>x.reference));
  for(const item of data.items||[]){if(!existing.has(item.reference)){courses.push(item);existing.add(item.reference);}}
  fill('assignmentFilesCourse',courses.map(x=>({value:x.reference,label:x.title+' ('+x.code+')'})),'Choose a course');
  if(current)$('assignmentFilesCourse').value=current;
  button.hidden=coursePage>=coursePages;
 }catch(e){notify('danger',e.message);}finally{button.disabled=false;}
}
async function load(){
 coursePage=0;coursePages=1;courses=[];
 await loadMoreCourses();await chooseCourse();
}
async function upload(ref,file){
 if(!sessionValid())throw Error('Session changed. Reload.');
 const signature=file.name+':'+file.size+':'+file.lastModified;
 if(!pendingUpload||pendingUpload.signature!==signature)
  pendingUpload={signature,requestId:crypto.randomUUID(),reference:null};
 if(pendingUpload.reference)return pendingUpload.reference;
 const token=document.querySelector('meta[name="request-verification-token"]')?.content;
 if(!token)throw Error('Missing security token. Reload.');
 const form=new FormData();form.append('file',file);form.append('requestId',pendingUpload.requestId);
 notify('info','Scanning handout…');
 const response=await fetch('/api/lms/assignment-attachments/'+encodeURIComponent(ref)+'/files',
  {method:'POST',credentials:'same-origin',cache:'no-store',headers:{RequestVerificationToken:token},body:form});
 const result=await response.json().catch(()=>null);
 if(!sessionValid()){clearSensitive();throw Error('Session changed. Reload.');}
 if(!response.ok||!result?.success)throw Error(result?.message||'Upload failed.');
 pendingUpload.reference=result.data.reference;
 return pendingUpload.reference;
}
async function save(e){
 e.preventDefault();if(!selected())return notify('warning','Choose an assignment.');
 const file=$('assignmentFilesUpload').files?.[0];
 if(!file&&!editing)return notify('warning','Select a handout file.');
 if(file&&(file.size<1||file.size>5242880))return notify('warning','Choose a file up to 5 MB.');
 const btn=$('assignmentFilesSave');btn.disabled=true;
 try{
  const ref=file?await upload(selected(),file):editing.fileAssetReference;
  const body={assignmentReference:selected(),fileAssetReference:ref,
   title:$('assignmentFilesTitle').value.trim()||null,rowVersion:editing?.rowVersion||null};
  await api('/api/lms/assignment-attachments'+(editing?'/'+editing.id:''),editing?'PUT':'POST',body);
  await files();notify('success','Attachment saved.');
 }catch(err){notify('danger',err.message);}finally{btn.disabled=false;}
}
function exportCsv(){
 const title=assignments.find(x=>x.reference===selected())?.title||'';
 const quote=v=>{let s=String(v??'');if(/^[=+@\-\t\r]/.test(s))s="'"+s;return '"'+s.replace(/"/g,'""')+'"';};
 const data=[['Assignment','Title','File name','File reference'],...rows.map(x=>[title,x.title,x.fileName,x.fileAssetReference])];
 const url=URL.createObjectURL(new Blob(['\uFEFF'+data.map(x=>x.map(quote).join(',')).join('\r\n')],{type:'text/csv'}));
 const a=document.createElement('a');a.href=url;a.download='assignment-attachments.csv';
 document.body.append(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(url),1000);
}
$('assignmentFilesCourse').addEventListener('change',chooseCourse);
$('assignmentFilesAssignment').addEventListener('change',files);
$('assignmentFilesRefresh').addEventListener('click',files);
$('assignmentFilesMoreCourses').addEventListener('click',loadMoreCourses);
$('assignmentFilesCsv').addEventListener('click',exportCsv);
$('assignmentFilesPrint').addEventListener('click',()=>window.print());
if(author){$('assignmentFilesForm').addEventListener('submit',save);$('assignmentFilesClear').addEventListener('click',reset);}
new MutationObserver(()=>{if(!sessionValid())clearSensitive();})
 .observe(document.body,{attributes:true,attributeFilter:['data-auth-tenant-id','data-auth-session-reference']});
window.addEventListener('pagehide',clearSensitive);
load();
})();
