'use strict';
// UX C source-only checks. These do not replace SQL, browser, security or Full Final QA.
const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'../..'),read=p=>fs.readFileSync(path.join(root,p),'utf8');
const examViews=['AssessmentComponents','AssessmentSchedules','GradeSchemes','CertificateTemplates','Certificates','Transcripts','Index','MarkReviews','Setup','Attempts','ResultRescrutiny','ResultFollowUp','GradeDistribution','ExamReports','AdmitCards'];
const examRoutes=['AssessmentComponents','AssessmentSchedules','GradeSchemes','CertificateTemplates','Certificates','Transcripts','Marks','MarkReviews','Results','Publications','Setup','Attempts','ResultRescrutiny','ResultFollowUp','GradeDistribution','ExamReports','AdmitCards'];
const views=examViews.map(n=>'EduOS.App/Views/AssessmentAdministration/'+n+'.cshtml').concat([
 'EduOS.App/Views/ResearchProposals/Index.cshtml','EduOS.App/Views/ResearchGrants/Index.cshtml',
 'EduOS.App/Views/Accreditation/Index.cshtml','EduOS.App/Views/Accreditation/Obe.cshtml']);
test('UX C outstanding routes retain one shared title, balanced forms and no duplicate top header',()=>{
 for(const p of views){const src=read(p);assert.match(src,/ViewData\["Title"\]/,p);
  assert.doesNotMatch(src.slice(0,1600),/<header\b|<h1\b|content-header-card/i,p);
  assert.equal((src.match(/<form\b/gi)||[]).length,(src.match(/<\/form>/gi)||[]).length,p);
 }
});
test('all seventeen new examination routes and two research/accreditation pairs have Page Help guides',()=>{
 const help=read('EduOS.App/wwwroot/js/platform/page-context-help.js').toLowerCase();
 for(const name of examRoutes)assert.ok(help.includes("'/assessmentadministration/"+name.toLowerCase()+"/index':"),name);
 for(const name of ['researchproposals','researchgrants','accreditation','accreditation/obe'])
  assert.ok(help.includes("'/"+name+"/index':"),name);
});
test('exam internal create codes are hidden and generated server-side with stable retry intent',()=>{
 const views=['AssessmentComponents','GradeSchemes','CertificateTemplates'];
 const ids=['componentCode','gradeSchemeCode','certificateTemplateCode'];
 for(let i=0;i<views.length;i++)
  assert.match(read('EduOS.App/Views/AssessmentAdministration/'+views[i]+'.cshtml'),
   new RegExp('id="'+ids[i]+'" type="hidden"'));
 const documents=read('EduOS.Service/Services/Assessment/AssessmentAdministrationService.Documents.cs');
 const setup=read('EduOS.Service/Services/Assessment/AssessmentAdministrationService.Setup.cs');
 for(const prefix of ['CMP-','CTP-'])assert.ok(documents.includes(prefix));
 for(const prefix of ['GRD-','ASM-'])assert.ok(setup.includes(prefix));
 for(const js of ['assessment-components.js','grade-schemes.js','certificate-templates.js']){
  const src=read('EduOS.App/wwwroot/js/assessment/'+js);
  assert.match(src,/createRequestId/,js);assert.match(src,/clientRequestId/,js);
 }
});
test('grant application selects paged, owned, ethics-approved proposals instead of typing a GUID',()=>{
 const view=read('EduOS.App/Views/ResearchGrants/Index.cshtml');
 const js=read('EduOS.App/wwwroot/js/research/research-grants.js');
 const service=read('EduOS.Service/Services/Research/ResearchGrantService.cs');
 const api=read('EduOS.App/Controllers/Api/ResearchGrantsApiController.cs');
 assert.match(view,/<select id="grantProposal"/);
 assert.doesNotMatch(view,/<input id="grantProposal"[^>]*maxlength="36"/);
 assert.match(js,/eligible-proposals\?page=/);assert.match(js,/proposalGeneration/);
 assert.match(service,/p.OwnerUserId = \{1\}/);assert.match(service,/p.StateCode = N'EthicsApproved'/);
 assert.match(service,/CampusGrant/);assert.match(api,/HttpGet\("eligible-proposals"\)/);
});
test('research and accreditation remove all obsolete Refresh controls and preserve OBE navigation',()=>{
 const modules=['ResearchProposals','ResearchGrants','Accreditation'];
 const js=['research-proposals.js','research-grants.js','accreditation.js'];
 const ids=['researchRefresh','grantRefresh','accRefresh'];
 for(let i=0;i<modules.length;i++){
  const view=read('EduOS.App/Views/'+modules[i]+'/Index.cshtml');
  const script=read('EduOS.App/wwwroot/js/research/'+js[i]);
  assert.ok(!view.includes(ids[i]),modules[i]);assert.ok(!script.includes(ids[i]),js[i]);
 }
 const acc=read('EduOS.App/Views/Accreditation/Index.cshtml');
 assert.match(acc,/id="accQueueHeading"[\s\S]*?asp-action="Obe"/);
 assert.match(acc,/id="accStandardCode"/);assert.match(acc,/id="accOutcomeCode"/);
});
test('the lane ledger distinguishes all 38 source UX routes from deferred Full Final QA',()=>{
 const doc=read('docs/ux/lanes/UX_C.md');
 assert.equal((doc.match(/^- \[x\] \x60\//gm)||[]).length,38);
 assert.match(doc,/Full Final QA is deferred/);
});
