(() => {
"use strict";
const filter=document.getElementById("ttFilter"),rows=document.getElementById("ttRows"),count=document.getElementById("ttCount");
if(!filter||!rows||!count)return;
const entries=Array.from(rows.querySelectorAll("tr"));
function applyFilter(){
 const query=filter.value.trim().toLocaleLowerCase(),visible=entries.filter(row=>{
  const match=row.textContent.toLocaleLowerCase().includes(query);
  row.hidden=!match;
  return match;
 }).length;
 count.textContent=visible+" of "+entries.length+" entries shown (maximum 500 loaded).";
}
filter.addEventListener("input",applyFilter);
const form=document.getElementById("ttForm"),save=document.getElementById("ttSave");
form?.addEventListener("submit",()=>{if(save)save.disabled=true;});
rows.querySelectorAll(".tt-reset-form").forEach(resetForm=>{
 resetForm.addEventListener("submit",event=>{
  if(!window.confirm("Reset this terminology override to the default?")){event.preventDefault();return;}
  const button=resetForm.querySelector('button[type="submit"]');
  if(button)button.disabled=true;
 });
});
})();
