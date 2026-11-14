'use strict';
// Focused UX C exam source audit only; not browser, SQL, posting or Final QA.
const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'../..'),read=p=>fs.readFileSync(path.join(root,p),'utf8');
const routes=['ComponentMarks','Omr','SubjectCompletions'];
const view=name=>read('EduOS.App/Views/AssessmentAdministration/'+name+'.cshtml');
test('the first three UX C examination routes have one shared title and no empty local header',()=>{
 for(const name of routes){const src=view(name);assert.match(src,/ViewData\["Title"\]/);
 assert.doesNotMatch(src,/<h1\b|<header\b|content-header-card/i,name);}
});
test('OMR manual stays in the existing scope card and remains accessible to JS',()=>{
 const v=view('Omr'),js=read('EduOS.App/wwwroot/js/assessment/omr-workspace.js');
 assert.match(v,/id="omrHelpOpen"[^>]*aria-controls="omrHelp"/);
 assert.match(v,/id="omrHelp"/);assert.match(js,/omrHelpOpen/);
 assert.match(v,/<select id="omrComponent"[^>]*>[\s\S]*?<\/select>\s*<button[^>]*id="omrHelpOpen"/);
});
test('subject completion filter is a real nested grid and cannot waste left card width',()=>{
 const v=view('SubjectCompletions');
 assert.match(v,/<form id="completionFilter" class="card mb-2"><div class="card-body"><div class="row g-2 align-items-end">/);
 assert.match(v,/class="col-md-4"[^>]*>[\s\S]*?id="completionBatchSearch"/);
 assert.match(v,/class="col-md-8"[^>]*>[\s\S]*?id="completionBatch"/);
 assert.equal((v.match(/<form\b/g)||[]).length,(v.match(/<\/form>/g)||[]).length);
});
test('each exam component route has a full bilingual contextual Page Help guide',()=>{
 const js=read('EduOS.App/wwwroot/js/platform/page-context-help.js').toLowerCase();
 for(const name of routes)assert.ok(js.includes("'/assessmentadministration/"+name.toLowerCase()+"/index':"),name);
});
test('three screens keep server/role-bound operations and their source scripts',()=>{
 const omr=view('Omr'),component=view('ComponentMarks'),completions=view('SubjectCompletions');
 assert.match(omr,/data-can-post=/);assert.match(omr,/id="omrPost"/);
 assert.match(component,/id="cmSave"/);assert.match(completions,/id="completionCsv"/);
 for(const name of routes)for(const m of view(name).matchAll(/<script[^>]*src=["']~\/([^"']+\.js)["']/g))
  assert.ok(fs.existsSync(path.join(root,'EduOS.App/wwwroot/'+m[1])),name+' missing '+m[1]);
});