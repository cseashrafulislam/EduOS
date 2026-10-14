(() => {
 "use strict";
 const $=id=>document.getElementById(id),root="/api/document-verification";
 const token=document.querySelector('meta[name="request-verification-token"]')?.content;
 const msg=(s,bad=false)=>{const n=$("verificationAlert");n.textContent=s;n.className="alert "+(bad?"alert-danger":"alert-success");};
 async function request(path,method="GET",body){
  const headers={Accept:"application/json"};
  if(method!=="GET"){if(!token)throw Error("Security token missing.");headers.RequestVerificationToken=token;headers["Content-Type"]="application/json";}
  const r=await fetch(root+path,{method,headers,credentials:"same-origin",cache:"no-store",body:body?JSON.stringify(body):undefined});
  const j=await r.json().catch(()=>null);if(!r.ok||!j?.success)throw Error(j?.message||"Request failed");return j.data;
 }
 window.documentVerification={$,msg,request};
})();