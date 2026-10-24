(() => {
'use strict';
const root=document.getElementById('lmsCompletionApp');if(!root)return;
const $=id=>document.getElementById(id);
const state={courses:[],coursePage:1,coursePages:1,rows:[],page:1,pages:1,request:0,busy:false};
const option=(value,label)=>{const o=document.createElement('option');o.value=value;o.textContent=label;return o;};
const cell=(tr,value)=>{const td=document.createElement('td');td.textContent=String(value??'');tr.append(td);return td;};
function alertUser(type,message){const node=$('lmsCompletionAlert');node.className='alert alert-'+type;node.textContent=message;node.focus();}
async function api(url,method='GET',body){
 const headers={Accept:'application/json'};
 if(method!=='GET'){const token=document.querySelector('meta[name="request-verification-token"]')?.content;
  if(!token)throw Error('Security token missing. Reload the page.');
  headers.RequestVerificationToken=token;headers['Content-Type']='application/json';}
 const response=await fetch(url,{method,credentials:'same-origin',cache:'no-store',headers,
  ...(body===undefined?{}:{body:JSON.stringify(body)})});
 const result=await response.json().catch(()=>null);
 if(!response.ok||!result?.success)throw Error(result?.message||'Request failed.');
 return result.data;
}
async function loadCourses(){
 const old=$('lmsCompletionCourse').value;
 try{const data=await api('/api/lms/courses?page='+state.coursePage+'&pageSize=100');
  state.courses=data?.items||[];state.coursePages=Math.max(1,data?.totalPages||1);
  const select=$('lmsCompletionCourse');select.replaceChildren(option('','Choose a course'));
  state.courses.forEach(x=>select.append(option(x.reference,x.title+' ('+x.code+')')));
  if(state.courses.some(x=>x.reference===old))select.value=old;
  $('lmsCompletionCoursePage').textContent='Page '+state.coursePage+' / '+state.coursePages;
  $('lmsCompletionCoursePrev').disabled=state.coursePage<=1;
  $('lmsCompletionCourseNext').disabled=state.coursePage>=state.coursePages;
  state.page=1;await loadRegister();
 }catch(e){alertUser('danger',e.message);}
}
function render(){
 const body=$('lmsCompletionRows');body.replaceChildren();
 for(const row of state.rows){
  const tr=document.createElement('tr'),elig=row.eligibility||{},completed=row.state===2;
  cell(tr,row.studentName+' ('+row.studentCode+')');
  cell(tr,completed?'Completed':row.state===1?'Active':'Inactive');
  cell(tr,(elig.completedLessons||0)+' / '+(elig.requiredLessons||0));
  cell(tr,(elig.gradedAssignments||0)+' / '+(elig.requiredAssignments||0));
  cell(tr,(elig.passedQuizzes||0)+' / '+(elig.requiredQuizzes||0));
  cell(tr,completed?'Completed':elig.ready?'Ready':(elig.blockers||[]).join(' '));
  const td=cell(tr,'');
  if(row.state===1&&elig.ready){
   const btn=document.createElement('button');btn.type='button';btn.className='btn btn-sm btn-primary';
   btn.textContent='Complete';btn.disabled=state.busy;btn.addEventListener('click',()=>complete(row,btn));td.append(btn);
  }
  body.append(tr);
 }
 if(!state.rows.length){const tr=document.createElement('tr'),td=cell(tr,'No enrollments found.');td.colSpan=7;body.append(tr);}
 $('lmsCompletionPage').textContent='Page '+state.page+' / '+state.pages;
 $('lmsCompletionPrev').disabled=state.page<=1;$('lmsCompletionNext').disabled=state.page>=state.pages;
}
async function loadRegister(){
 const course=$('lmsCompletionCourse').value,seq=++state.request;
 if(!course){state.rows=[];state.pages=1;render();return;}
 const query=new URLSearchParams({page:String(state.page),pageSize:'20',search:$('lmsCompletionSearch').value.trim()});
 try{const data=await api('/api/lms/courses/'+encodeURIComponent(course)+'/completion-register?'+query);
  if(seq!==state.request)return;
  state.rows=data?.items||[];state.pages=Math.max(1,data?.totalPages||1);render();
 }catch(e){if(seq===state.request)alertUser('danger',e.message);}
}
async function complete(row,button){
 if(state.busy||!window.confirm('Record course completion for '+row.studentName+'?'))return;
 state.busy=true;button.disabled=true;
 try{await api('/api/lms/courses/enrollments/'+encodeURIComponent(row.enrollmentId)+'/complete','POST',
  {rowVersion:row.rowVersion});
  alertUser('success','Course completion recorded.');await loadRegister();
 }catch(e){alertUser('danger',e.message);await loadRegister();}
 finally{state.busy=false;}
}
function csv(){
 const headers=['Student code','Student name','State','Lessons','Assignments','Quizzes','Eligibility'];
 const rows=state.rows.map(x=>{const e=x.eligibility||{};return [x.studentCode,x.studentName,x.state===2?'Completed':x.state===1?'Active':'Inactive',
  (e.completedLessons||0)+'/'+(e.requiredLessons||0),(e.gradedAssignments||0)+'/'+(e.requiredAssignments||0),
  (e.passedQuizzes||0)+'/'+(e.requiredQuizzes||0),x.state===2?'Completed':e.ready?'Ready':(e.blockers||[]).join(' ')];});
 const safe=v=>{let s=String(v??'');if(/^[=+@\-\t\r]/.test(s))s="'"+s;return '"'+s.replaceAll('"','""')+'"';};
 const blob=new Blob([String.fromCharCode(0xfeff),[headers,...rows].map(x=>x.map(safe).join(',')).join('\r\n')],{type:'text/csv;charset=utf-8'});
 const url=URL.createObjectURL(blob),link=document.createElement('a');link.href=url;link.download='lms-course-completions.csv';
 link.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
}
$('lmsCompletionCourse').addEventListener('change',()=>{state.page=1;loadRegister();});
$('lmsCompletionSearchButton').addEventListener('click',()=>{state.page=1;loadRegister();});
$('lmsCompletionSearch').addEventListener('keydown',e=>{if(e.key==='Enter'){e.preventDefault();state.page=1;loadRegister();}});
$('lmsCompletionCoursePrev').addEventListener('click',()=>{if(state.coursePage>1){state.coursePage--;loadCourses();}});
$('lmsCompletionCourseNext').addEventListener('click',()=>{if(state.coursePage<state.coursePages){state.coursePage++;loadCourses();}});
$('lmsCompletionPrev').addEventListener('click',()=>{if(state.page>1){state.page--;loadRegister();}});
$('lmsCompletionNext').addEventListener('click',()=>{if(state.page<state.pages){state.page++;loadRegister();}});
$('lmsCompletionCsv').addEventListener('click',csv);
$('lmsCompletionPrint').addEventListener('click',()=>window.print());
loadCourses();
})();
