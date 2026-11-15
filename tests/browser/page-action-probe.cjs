'use strict';
// Read-only Chromium affordance census. A button/link is NOT proof of a
// successful backend action, file download, approval or correct report.
const actions=['create','edit','delete','approve','print','excel','pdf','report'];
async function probePageActions(page,selector='main#main-content'){
    return page.evaluate(({selector,actions})=>{
        const root=document.querySelector(selector)||document.querySelector('main');
        if(!root) return {sampled:false,controls:0,forms:0,tableCount:0,
            hints:Object.fromEntries(actions.map(a=>[a,{present:0,visible:0}]))};
        const expressions={
            create:/(^|[^a-z])(create|add|new|register|enroll|issue|record|open form)([^a-z]|$)|নতুন|যোগ করুন|তৈরি|নিবন্ধন/i,
            edit:/(^|[^a-z])(edit|update|modify|change)([^a-z]|$)|সম্পাদনা|সংশোধন/i,
            delete:/(^|[^a-z])(delete|remove|purge|discard)([^a-z]|$)|মুছুন|বাতিল|অপসারণ/i,
            approve:/(^|[^a-z])(approve|reject|authorize|verify|post journal|submit for approval)([^a-z]|$)|অনুমোদন|প্রত্যাখ্যান/i,
            print:/(^|[^a-z])(print|printer)([^a-z]|$)|প্রিন্ট|মুদ্রণ/i,
            excel:/(^|[^a-z])(excel|xlsx|csv|spreadsheet)([^a-z]|$)|এক্সেল/i,
            pdf:/(^|[^a-z])(pdf|portable document)([^a-z]|$)|পিডিএফ/i,
            report:/(^|[^a-z])(report|statement|trial balance)([^a-z]|$)|রিপোর্ট|বিবরণী/i
        };
        const hints=Object.fromEntries(actions.map(a=>[a,{present:0,visible:0}]));
        const controls=[...root.querySelectorAll(
            'button,a[href],input[type="submit"],input[type="button"],[role="button"]')];
        for(const element of controls){
            // Only labels/IDs are inspected; no entered data, links, form
            // values, student names, tokens or printable content is exported.
            const label=[
                element.getAttribute('aria-label'),element.getAttribute('title'),
                element.getAttribute('data-action'),element.id,
                element.getAttribute('name'),element.textContent
            ].filter(Boolean).join(' ').slice(0,160).replace(/([a-z])([A-Z])/g,'$1 $2');
            const visible=element.getClientRects().length>0&&
                getComputedStyle(element).visibility!=='hidden';
            for(const action of actions){
                if(!expressions[action].test(label))continue;
                hints[action].present++;
                if(visible)hints[action].visible++;
            }
        }
        return {sampled:true,controls:controls.length,forms:root.querySelectorAll('form').length,
            tableCount:root.querySelectorAll('table').length,hints};
    },{selector,actions});
}
module.exports={actions,probePageActions};
