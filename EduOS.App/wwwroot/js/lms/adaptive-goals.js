(()=>{'use strict';const root=document.getElementById('adaptiveGoals');if(!root)return;
const $=id=>document.getElementById(id),bn=root.dataset.lang==='bn',el=(tag,value)=>{const n=document.createElement(tag);if(value!==undefined)n.textContent=String(value);return n;};
const identity=[document.body?.dataset.authTenantId||'',document.body?.dataset.authSessionReference||''];
const safe=()=>identity[0]===(document.body?.dataset.authTenantId||'')&&identity[1]===(document.body?.dataset.authSessionReference||'')&&!window.EduOSSessionTransitionPending?.();
let skills=[],goals=[],epoch=0,coursePage=0,rosterPage=0,saving=false;
function alert(kind,message){const n=$('goalAlert');n.className='alert alert-'+kind;n.textContent=message;n.focus();}
async function api(url,method='GET',data){if(!safe())throw Error('Session changed. Reload.');const headers={Accept:'application/json'};
 if(method!=='GET'){const token=document.querySelector('meta[name="request-verification-token"]')?.content;
  if(!token)throw Error('Security token missing. Reload.');headers.RequestVerificationToken=token;headers['Content-Type']='application/json';}
 const response=await fetch(url,{method,cache:'no-store',credentials:'same-origin',headers,...(data?{body:JSON.stringify(data)}:{})});
 const json=await response.json().catch(()=>null);if(!safe())throw Error('Session changed. Reload.');
 if(!response.ok||!json?.success)throw Error(json?.message||'Request failed.');return json.data;}
function select(id,items,placeholder){const n=$(id);n.replaceChildren(new Option(placeholder,''));for(const row of items)n.add(new Option(row.label,row.value));}
function current(){const chosen=skills.find(x=>x.key===$('goalSkill').value);return chosen?goals.find(x=>x.courseSectionId===chosen.courseSectionId&&x.skillCode===chosen.skillCode):null;}
function fill(){const goal=current();$('goalPercent').value=goal?.targetPercent??80;$('goalNote').value=goal?.teacherNote||'';}
function render(){const table=$('goalRows');table.replaceChildren();for(const goal of goals){const tr=el('tr');
 tr.append(el('td',goal.skillCode),el('td',goal.targetPercent+'%'),el('td',goal.teacherNote||''));
 const actions=el('td'),btn=el('button',bn?'সম্পাদনা':'Edit');btn.type='button';btn.className='btn btn-sm btn-outline-primary';
 btn.onclick=()=>{const mapped=skills.find(x=>x.courseSectionId===goal.courseSectionId&&x.skillCode===goal.skillCode);
  if(mapped){$('goalSkill').value=mapped.key;fill();$('goalPercent').focus();}};actions.append(btn);tr.append(actions);table.append(tr);}}
async function moreCourses(){const button=$('goalMoreCourses'),next=coursePage+1;button.disabled=true;
 try{const data=await api('/api/lms/adaptive/teacher/courses?page='+next+'&pageSize=30');
  const selectBox=$('goalCourse');for(const course of data.items||[])selectBox.add(new Option(course.title,course.reference));
  coursePage=next;button.classList.toggle('d-none',!data.hasNextPage);
 }catch(e){alert('danger',e.message);}finally{button.disabled=false;}}
async function moreStudents(){const ref=$('goalCourse').value,k=epoch,next=rosterPage+1;
 if(!ref)return;const button=$('goalMoreStudents');button.disabled=true;
 try{const data=await api('/api/lms/adaptive/teacher/course/'+encodeURIComponent(ref)+'/roster?page='+next+'&pageSize=50');
  if(k!==epoch||ref!==$('goalCourse').value)return;
  const selectBox=$('goalStudent');for(const item of data.items||[])
   selectBox.add(new Option(item.studentCode+' · '+item.fullName,item.studentReference));
  rosterPage=next;button.classList.toggle('d-none',!data.hasNextPage);
 }catch(e){if(k===epoch)alert('danger',e.message);}finally{button.disabled=false;}}
async function courseChanged(){const ref=$('goalCourse').value,k=++epoch;skills=[];goals=[];rosterPage=0;render();
 $('goalMoreStudents').classList.add('d-none');
 select('goalStudent',[],bn?'শিক্ষার্থী বাছাই করুন':'Choose learner');select('goalSkill',[],bn?'দক্ষতা বাছাই করুন':'Choose skill');if(!ref)return;
 try{const mapped=await api('/api/lms/adaptive/teacher/course/'+encodeURIComponent(ref)+'/skills');if(k!==epoch)return;
 skills=(mapped||[]).map(x=>({...x,key:x.courseSectionId+':'+x.skillCode}));
 select('goalSkill',skills.map(x=>({label:x.chapter+' / '+x.skillCode,value:x.key})),bn?'দক্ষতা বাছাই করুন':'Choose skill');
 await moreStudents();
 }catch(e){if(k===epoch)alert('danger',e.message);}}
async function studentChanged(){const course=$('goalCourse').value,student=$('goalStudent').value,k=epoch;goals=[];render();if(!course||!student)return;
 try{const data=await api('/api/lms/adaptive/teacher/course/'+encodeURIComponent(course)+'/goals/'+encodeURIComponent(student));
  if(k!==epoch||student!==$('goalStudent').value)return;goals=data||[];render();fill();}catch(e){if(k===epoch)alert('danger',e.message);}}
async function save(event){event.preventDefault();if(saving||!event.currentTarget.reportValidity())return;
 const mapped=skills.find(x=>x.key===$('goalSkill').value);if(!mapped)return alert('warning','Choose a mapped skill.');
 const k=epoch;
 const request={courseReference:$('goalCourse').value,studentReference:$('goalStudent').value,
  courseSectionId:mapped.courseSectionId,skillCode:mapped.skillCode,targetPercent:Number($('goalPercent').value),
  teacherNote:$('goalNote').value.trim()||null,rowVersion:current()?.rowVersion||null};
 saving=true;$('goalSave').disabled=true;try{await api('/api/lms/adaptive/teacher/goals','PUT',request);
  if(k!==epoch||request.studentReference!==$('goalStudent').value)return;
  alert('success',bn?'লক্ষ্য সংরক্ষিত হয়েছে।':'Goal saved.');await studentChanged();}catch(e){alert('danger',e.message);}finally{saving=false;$('goalSave').disabled=false;}}
$('goalCourse').onchange=courseChanged;$('goalStudent').onchange=studentChanged;$('goalSkill').onchange=fill;$('goalForm').onsubmit=save;
$('goalMoreCourses').onclick=moreCourses;$('goalMoreStudents').onclick=moreStudents;
window.addEventListener('pagehide',()=>{epoch++;skills=[];goals=[];render();});
moreCourses();
})();
