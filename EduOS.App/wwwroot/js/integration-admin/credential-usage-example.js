(() => {
"use strict";
const button=document.getElementById("iaCopyApiExample");
const example=document.getElementById("iaApiExample");
if(!button || !example)return;
button.addEventListener("click",async()=>{
 try{
  await navigator.clipboard.writeText(example.textContent);
  button.textContent="Example copied";
  setTimeout(()=>{button.textContent="Copy placeholder example";},2000);
 }catch{
  button.textContent="Select and copy the example manually";
 }
});
})();
