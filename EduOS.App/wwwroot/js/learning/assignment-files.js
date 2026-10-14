(() => {
'use strict';
const root=document.getElementById('assignmentFilesApp');if(!root)return;
const $=id=>document.getElementById(id),author=root.dataset.author==='true';
let courses=[],assignments=[],rows=[],editing=null,sequence=0;
const opt=(value,text)=>{const n=document.createElement('option');n.value=value;n.textContent=text;return n;};
const notify=(kind,message)=>{const n=$('assignmentFilesAlert');n.className='alert alert-'+kind;n.textContent=message;};
const selected=()=>$('assignmentFilesAssignment').value;
async function api(url,method='GET',body){
 const headers={Accept:'application/json'};
 if(body!==undefined)headers['Content-Type']='application/json';
 if(method!=='GET'){
  const token=document.querySelector('meta[name="request-verification-token"]')?.content;
  if(!token)throw Error('Missing security token. Reload.');
  headers.RequestVerificationToken=token;
 }
 const r=await fetch(url,{method,credentials:'same-origin',cache:'no-store',headers,...(body===undefined?{}:{body:JSON.stringify(body)})});
 const j=await r.json().catch(()=>null);
 if(!r.ok||!j?.success)throw Error(j?.message||'Request failed.');
 return j.data;
}
function fill(id,items,placeholder){
 const n=$(id),previous=n.value;n.replaceChildren(opt('',placeholder));
 items.forEach(x=>n.append(opt(x.value,x.label)));
 if(items.some(x=>x.value===previous))n.value=previous;
}
function reset(){
 editing=null;
 if(author){$('assignmentFilesForm').reset();$('assignmentFilesSave').textContent='Save';}
}
function draw(){
 const body=$('assignmentFilesRows');body.replaceChildren();
 $('assignmentFilesCount').textContent=rows.length+' verified file(s)';
 for(const row of rows){
  const tr=document.createElement('tr');
  for(const value of [row.title||'—',row.fileName,row.fileAssetReference]){
   const td=document.createElement('td');td.textContent=value;tr.append(td);
  }
  if(author){
   const td=document.createElement('td');
   for(const [label,action] of [['Edit',()=>{
    editing=row;$('assignmentFilesReference').value=row.fileAssetReference;
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
async function load(){
 try{const data=await api('/api/lms/courses?page=1&pageSize=100');
  courses=data?.items||[];
  fill('assignmentFilesCourse',courses.map(x=>({value:x.reference,label:x.title+' ('+x.code+')'})),'Choose a course');
  await chooseCourse();
 }catch(e){notify('danger',e.message);}
}
async function save(e){
 e.preventDefault();if(!selected())return notify('warning','Choose an assignment.');
 const ref=$('assignmentFilesReference').value.trim();
 if(!/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(ref))
  return notify('warning','Enter a valid verified file GUID.');
 const body={assignmentReference:selected(),fileAssetReference:ref,
  title:$('assignmentFilesTitle').value.trim()||null,rowVersion:editing?.rowVersion||null};
 const btn=$('assignmentFilesSave');btn.disabled=true;
 try{await api('/api/lms/assignment-attachments'+(editing?'/'+editing.id:''),editing?'PUT':'POST',body);
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
$('assignmentFilesCsv').addEventListener('click',exportCsv);
$('assignmentFilesPrint').addEventListener('click',()=>window.print());
if(author){$('assignmentFilesForm').addEventListener('submit',save);$('assignmentFilesClear').addEventListener('click',reset);}
load();
})();
