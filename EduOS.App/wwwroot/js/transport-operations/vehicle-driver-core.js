(() => {
'use strict';
const el=id=>document.getElementById(id);
const form=el('transportVehicleDriverForm');if(!form)return;
const token=form.querySelector('[name="__RequestVerificationToken"]').value;
async function api(path,method='GET',body){
 const headers={Accept:'application/json'};
 if(body){headers['Content-Type']='application/json';headers.RequestVerificationToken=token;}
 const response=await fetch(path,{method,headers,credentials:'same-origin',cache:'no-store',
  ...(body?{body:JSON.stringify(body)}:{})});
 const result=await response.json().catch(()=>null);
 if(!response.ok||!result?.success)throw Error(result?.message||'Request failed');
 return result.data;
}
function notify(message,ok=false){
 const box=el('transportVehicleDriverAlert');box.className='alert alert-'+(ok?'success':'danger');
 box.textContent=message;box.focus();
}
function option(select,id,label){
 const node=document.createElement('option');node.value=String(id);node.textContent=label;select.append(node);
}
window.vehicleDriverUI={el,form,api,notify,option,endpoint:'/api/transport/vehicle-driver-links',
 today:()=>new Date().toLocaleDateString('en-CA'),vehicles:[],drivers:[],requestId:crypto.randomUUID()};
})();
