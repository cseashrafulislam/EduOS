(() => {
"use strict";
const select=document.getElementById("iaDeliveryExportEndpoint");
const button=document.getElementById("iaDeliveryExport");
const notice=document.getElementById("iaNotice");
if(!select||!button)return;
const base="/api/integration-admin";
function error(message){notice.textContent=message;notice.className="alert alert-danger";notice.classList.remove("d-none");}
async function loadEndpoints(){
 select.replaceChildren(new Option("Choose a webhook", ""));
 let page=1, pages=1;
 do {
  const response=await fetch(base+"/webhooks?page="+page+"&pageSize=100",{
   method:"GET",credentials:"same-origin",cache:"no-store",headers:{"Accept":"application/json"}
  });
  const result=await response.json();
  if(!response.ok||!result.success)throw Error(result.message||"Cannot load webhook endpoints.");
  for(const item of result.data.items||[])select.add(new Option(item.name+" (#"+item.id+")",String(item.id)));
  pages=result.data.totalPages||1;
  page++;
 }while(page<=pages && page<=100);
}
select.addEventListener("change",()=>{button.disabled=!select.value;});
button.addEventListener("click",async()=>{
 const id=Number(select.value);
 if(!Number.isSafeInteger(id)||id<=0)return;
 button.disabled=true;
 try{
  const response=await fetch(base+"/webhooks/"+encodeURIComponent(id)+"/deliveries/export",{
   method:"GET",credentials:"same-origin",cache:"no-store",headers:{"Accept":"text/csv"}
  });
  if(!response.ok){
   let message="Delivery export failed ("+response.status+").";
   try{const body=await response.json();message=body.message||message;}catch{}
   throw Error(message);
  }
  const blob=await response.blob(), objectUrl=URL.createObjectURL(blob);
  try{
   const anchor=document.createElement("a");
   anchor.href=objectUrl;anchor.download="webhook-deliveries-"+id+".csv";
   document.body.append(anchor);anchor.click();anchor.remove();
  }finally{setTimeout(()=>URL.revokeObjectURL(objectUrl),1000);}
 }catch(e){error(e.message);}
 finally{button.disabled=!select.value;}
});
loadEndpoints().catch(e=>error(e.message));
})();
