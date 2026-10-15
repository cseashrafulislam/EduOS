(() => {
"use strict";
const form=document.getElementById("tsForm"),toggle=document.getElementById("tsSensitive"),value=document.getElementById("tsValue"),save=document.getElementById("tsSave");
if(!form||!toggle||!value)return;
function update(){value.type=toggle.checked?"password":"text";}
toggle.addEventListener("change",update);update();
form.addEventListener("submit",event=>{
 const id=form.querySelector('input[name="id"]');
 if(toggle.checked&&(!id||!id.value)&&!value.value.trim()){
  event.preventDefault();value.setCustomValidity("A new protected setting requires a value.");value.reportValidity();return;
 }
 if(save)save.disabled=true;
});
value.addEventListener("input",()=>value.setCustomValidity(""));
})();
