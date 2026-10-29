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
function circulationQuery(){
 const query=new URLSearchParams({page:String(state.page),pageSize:'20',
  search:el('circulationSearch').value.trim(),status:el('circulationStatus').value});
 const campus=el('circulationCampus').value.trim();
 if(campus){
  if(!/^[1-9]\d*$/.test(campus))throw Error('Campus ID must be a positive whole number.');
  query.set('campusId',campus);
 }
 return query;
}
async function load(){
 const seq=++state.request;
 try{
  const result=await api('/api/library/circulation?'+circulationQuery());
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
// Always fetch a fresh, server-authorized page rather than exporting stale browser rows.
async function csv(){
 const button=el('circulationCsv');
 if(button.disabled)return;
 button.disabled=true;
 try{
  const query=circulationQuery();
  query.set('language',document.documentElement.lang?.toLowerCase().startsWith('bn')?'bn-BD':'en-BD');
  const response=await fetch('/api/library/circulation/export?'+query,{
   method:'GET',credentials:'same-origin',cache:'no-store',headers:{Accept:'text/csv'}});
  if(!response.ok){
   const error=await response.json().catch(()=>null);
   throw Error(error?.message||'Circulation export was denied.');
  }
  const blob=await response.blob();
  const url=URL.createObjectURL(blob);
  try{
   const link=document.createElement('a');link.href=url;link.download='library-circulation-page.csv';
   document.body.append(link);link.click();link.remove();
  }finally{setTimeout(()=>URL.revokeObjectURL(url),1000);}
 }catch(error){message(error.message);}
 finally{button.disabled=false;}
}

const direct={key:crypto.randomUUID(),studentPage:0,bookPage:0,copyPage:0,
 studentSeq:0,bookSeq:0,copySeq:0,studentTerm:'',bookTerm:'',bookReference:'',busy:false};
const issueStatus=text=>{el('directIssueStatus').textContent=text;};
function newIssueKey(){direct.key=crypto.randomUUID();}
function clearIssueCopies(){
 ++direct.copySeq;direct.copyPage=0;
 el('directIssueCopy').replaceChildren(new Option('Select an available copy',''));
 el('directIssueCopyMore').hidden=true;
}
async function findIssueStudents(page=1){
 const term=el('directIssueStudentQuery').value.trim();
 if(term.length<2||term.length>100){issueStatus('Enter at least 2 characters to find a student.');return;}
 const seq=++direct.studentSeq;
 if(page===1){el('directIssueStudent').replaceChildren(new Option('Select an eligible student',''));direct.studentPage=0;}
 el('directIssueStudentMore').hidden=true;
 try{
  const data=await api('/api/library/reservations/eligible-students?'+new URLSearchParams({search:term,page:String(page),pageSize:'20'}));
  if(seq!==direct.studentSeq||el('directIssueStudentQuery').value.trim()!==term)return;
  for(const s of data?.items||[])el('directIssueStudent').add(new Option(s.studentCode+' — '+s.studentName,s.studentReference));
  direct.studentPage=page;direct.studentTerm=term;
  el('directIssueStudentMore').hidden=!data?.hasNextPage;
  issueStatus((data?.totalCount||0)+' eligible student(s) found.');
 }catch(e){if(seq===direct.studentSeq){issueStatus('Student search failed.');message(e.message);}}
}
async function findIssueBooks(page=1){
 const term=el('directIssueBookQuery').value.trim();
 const seq=++direct.bookSeq;
 if(page===1){el('directIssueBook').replaceChildren(new Option('Select a book',''));direct.bookPage=0;clearIssueCopies();}
 el('directIssueBookMore').hidden=true;
 try{
  const data=await api('/api/library/catalog?'+new URLSearchParams({search:term,page:String(page),pageSize:'20'}));
  if(seq!==direct.bookSeq||el('directIssueBookQuery').value.trim()!==term)return;
  for(const b of data?.items||[])el('directIssueBook').add(new Option(b.title+' ('+b.availableCopies+' available)',b.reference));
  direct.bookPage=page;direct.bookTerm=term;
  el('directIssueBookMore').hidden=!data?.hasNextPage;
  issueStatus((data?.totalCount||0)+' matching book(s).');
 }catch(e){if(seq===direct.bookSeq){issueStatus('Book search failed.');message(e.message);}}
}
async function findIssueCopies(page=1){
 const book=el('directIssueBook').value,student=el('directIssueStudent').value;
 if(!book||!student){clearIssueCopies();issueStatus('Choose an eligible student and book to see campus-available copies.');return;}
 if(page===1){clearIssueCopies();direct.bookReference=book;}
 const seq=++direct.copySeq;
 el('directIssueCopyMore').hidden=true;
 try{
  const data=await api('/api/library/books/'+encodeURIComponent(book)+'/available-copies?'+
   new URLSearchParams({studentReference:student,page:String(page),pageSize:'50'}));
  if(seq!==direct.copySeq||el('directIssueBook').value!==book||el('directIssueStudent').value!==student)return;
  let added=0;
  for(const copy of data?.items||[]){
   if(Number(copy.state)!==1)continue;
   el('directIssueCopy').add(new Option(copy.accessionNumber+
    (copy.libraryBranchId?' (branch '+copy.libraryBranchId+')':' (unassigned)'),String(copy.id)));
   added++;
  }
  direct.copyPage=page;
  el('directIssueCopyMore').hidden=!data?.hasNextPage;
  issueStatus('Available copies on this page: '+added+'. Select the correct campus branch; the server validates student eligibility.');
 }catch(e){if(seq===direct.copySeq){issueStatus('Copy lookup failed.');message(e.message);}}
}
el('directIssueStudentSearch').addEventListener('click',()=>findIssueStudents(1));
el('directIssueStudentMore').addEventListener('click',()=>findIssueStudents(direct.studentPage+1));
el('directIssueBookSearch').addEventListener('click',()=>findIssueBooks(1));
el('directIssueBookMore').addEventListener('click',()=>findIssueBooks(direct.bookPage+1));
el('directIssueCopyMore').addEventListener('click',()=>findIssueCopies(direct.copyPage+1));
el('directIssueStudentQuery').addEventListener('input',()=>{
 ++direct.studentSeq;direct.studentPage=0;newIssueKey();
 el('directIssueStudent').replaceChildren(new Option('Select an eligible student',''));
 el('directIssueStudentMore').hidden=true;clearIssueCopies();
});
el('directIssueBookQuery').addEventListener('input',()=>{
 ++direct.bookSeq;direct.bookPage=0;newIssueKey();
 el('directIssueBook').replaceChildren(new Option('Select a book',''));
 el('directIssueBookMore').hidden=true;clearIssueCopies();
});
el('directIssueStudent').addEventListener('change',()=>{newIssueKey();findIssueCopies(1);});
el('directIssueBook').addEventListener('change',()=>{newIssueKey();findIssueCopies(1);});
el('directIssueCopy').addEventListener('change',newIssueKey);
el('directIssueDueDate').addEventListener('change',newIssueKey);
const utcToday=new Date().toISOString().slice(0,10);
el('directIssueDueDate').min=utcToday;
el('directIssueDueDate').value=new Date(Date.now()+14*86400000).toISOString().slice(0,10);
el('directIssueForm').addEventListener('submit',async e=>{
 e.preventDefault();
 if(direct.busy)return;
 const studentReference=el('directIssueStudent').value,copyValue=Number(el('directIssueCopy').value);
 const dueDate=el('directIssueDueDate').value;
 if(!studentReference||!Number.isSafeInteger(copyValue)||copyValue<=0||!dueDate){
  message('Choose an eligible student, an available copy and a due date.');return;
 }
 if(dueDate<new Date().toISOString().slice(0,10)){
  message('Due date cannot be before the current UTC date.');return;
 }
 direct.busy=true;el('directIssueSubmit').disabled=true;
 try{
  await api('/api/library/issues','POST',{clientRequestId:direct.key,
   studentReference,bookCopyId:copyValue,dueDate});
  newIssueKey();message('Copy issued successfully.',true);
  await Promise.all([findIssueCopies(1),load()]);
 }catch(error){message(error.message);issueStatus('If this book has an active reservation, fulfill it from the reservation queue.');}
 finally{direct.busy=false;el('directIssueSubmit').disabled=false;}
});

el('circulationFilters').addEventListener('submit',e=>{e.preventDefault();state.page=1;load();});
el('circulationPrevious').addEventListener('click',()=>{if(state.page>1){state.page--;load();}});
el('circulationNext').addEventListener('click',()=>{if(state.page<state.pages){state.page++;load();}});
el('circulationCsv').addEventListener('click',csv);
el('circulationPrint').addEventListener('click',()=>window.print());
load();
})();
