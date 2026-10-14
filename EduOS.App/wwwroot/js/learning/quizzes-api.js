(()=>{'use strict';
window.EduQuizApi=async(url,method='GET',data)=>{
const headers={Accept:'application/json'};
if(data!==undefined)headers['Content-Type']='application/json';
// The site-wide fetch wrapper attaches the server-issued CSRF header to same-origin writes.
const response=await fetch(url,{method,credentials:'same-origin',cache:'no-store',headers,
...(data===undefined?{}:{body:JSON.stringify(data)})});
const json=await response.json().catch(()=>null);
if(!response.ok||!json?.success)throw Error(json?.message||'Request failed');
return json.data;};
})();
