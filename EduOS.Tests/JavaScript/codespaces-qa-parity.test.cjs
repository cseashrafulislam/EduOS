'use strict';
// Keep private Codespaces QA equivalent to the real disposable Actions gates.
// Source contract only: it never connects to SQL Server or a Codespace.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const test=require('node:test');
const repo=path.resolve(__dirname,'../..');
const qa=fs.readFileSync(path.join(repo,'.devcontainer/qa.sh'),'utf8');
const workflow=fs.readFileSync(
    path.join(repo,'.github/workflows/real-data-entry-qa.yml'),'utf8');

test('Codespaces all-scope executes the Actions real-browser gates in dependency order',()=>{
    const files=[
        'real-data-entry.cjs','real-academic-foundation.cjs',
        'real-student-enrollment.cjs','real-menu-route-sweep.cjs',
        'real-tenantadmin-page-render.cjs','real-roleexclusive-page-render.cjs',
        'real-privileged-route-coverage.cjs'
    ];
    let prior=-1;
    for(const name of files){
        const command='node tests/browser/'+name;
        const offset=qa.indexOf(command);
        assert(offset>prior,'Codespaces gate missing or out of order: '+name);
        assert(workflow.includes(command),'Actions is missing corresponding gate: '+name);
        prior=offset;
    }
    const routeReport='node scripts/qa/compare-real-route-coverage.cjs';
    assert(qa.includes(routeReport),'Codespaces route gap report missing');
    assert(qa.includes('QA_PLATFORM_PASSWORD'),'Codespaces private platform password missing');
    assert(workflow.includes('QA_PLATFORM_PASSWORD'),'Actions private platform password missing');
    assert(workflow.includes(routeReport),'Actions route gap report missing');
    for(const key of [
        'QA_YEAR_NAME','QA_TERM_NAME','QA_PROGRAM_NAME','QA_LEVEL_NAME',
        'QA_SUBJECT_NAME','QA_SECTION_NAME','QA_CURRICULUM_NAME',
        'QA_BATCH_NAME','QA_STUDENT_NAME','QA_GUARDIAN_NAME','QA_STUDENT_ROLL'
    ]) assert(qa.includes('export '+key+'='),'Synthetic fixture missing: '+key);
});
test('Codespaces checks all three independently persisted SQL acceptance suites',()=>{
    for(const type of [
        'SqlServerRealDataEntryEvidenceTests',
        'SqlServerRealAcademicFoundationEvidenceTests',
        'SqlServerRealStudentEnrollmentEvidenceTests'
    ]) assert(qa.includes('FullyQualifiedName~'+type),'Missing SQL gate '+type);
    assert(qa.includes('qa-artifacts/test-results'),'TRX evidence not retained privately');
});
test('Codespaces refuses arbitrary branches, stale binaries and stale browser evidence',()=>{
    assert(qa.includes('guard_branch'));
    assert(qa.includes('git rev-parse HEAD'));
    assert(qa.includes('EduOS_Demo_Codespaces'));
    assert(qa.includes('EDUOS_QA_ORIGIN="$origin"'));
    assert(qa.includes('QA_EVIDENCE'));
    assert(qa.includes("assert.equal(data.sha,sha"));
    assert(!qa.includes('EDUOS_E2E_BASE_URL'),'No hosted production target permitted');
});

test('First-run Codespaces QA starts private SQL before requiring its password file',()=>{
    const start=qa.slice(qa.indexOf('\nstart() {'),qa.indexOf('\nstatus() {'));
    const full=qa.slice(qa.indexOf('\nrun_tests() {'),qa.indexOf('\nstop() {'));
    assert(start.includes('bash .devcontainer/sqlserver.sh start'),
        'The app start command must bootstrap private SQL');
    assert(start.indexOf('bash .devcontainer/sqlserver.sh start')<
        start.indexOf('\n  connection\n'),
        'Private SQL must start before connecting during application bootstrap');
    assert(full.includes('\n  start\n  connection\n  browser\n'),
        'Full QA must run the idempotent bootstrap before SQL validation and browser tests');
});

test('Codespaces SQL startup uses authenticated readiness instead of Docker log grep',()=>{
    const sql=fs.readFileSync(path.join(repo,'.devcontainer/sqlserver.sh'),'utf8');
    assert(sql.includes('sql_ready() {'),'Missing authenticated SQL readiness probe');
    assert(sql.includes('if authenticate "$saved"; then return 0; fi'));
    assert(sql.includes('if sql_ready; then'),'Startup does not consult SQL readiness');
    assert(!sql.includes('docker logs "$container" 2>&1 | grep -q'),
        'Docker log grep -q is unreliable with pipefail and long logs');
    assert(sql.includes('report_startup_failure'),
        'SQL start failure must capture private, safe diagnostic evidence');
    assert(sql.includes('eduos-local-sql-data'),
        'Existing persistent SQL data volume must be preserved');
    assert(!/docker (?:rm|volume rm|volume prune)/.test(sql),
        'QA startup must not delete SQL containers or volumes');
});
