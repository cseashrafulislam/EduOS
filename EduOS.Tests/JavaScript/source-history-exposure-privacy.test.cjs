'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync, execFileSync } = require('node:child_process');

const root = path.resolve(__dirname, '..', '..');
const scanner = path.join(root, 'scripts/security/audit_git_exposure.py');
const workflow = path.join(root, '.github/workflows/source-history-exposure.yml');

function git(cwd, ...args) {
  return execFileSync('git', args, { cwd, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
}
function commit(cwd, message) {
  git(cwd, 'add', '-A');
  git(cwd, '-c', 'user.name=QA', '-c', 'user.email=qa@example.invalid', 'commit', '-qm', message);
}

test('full-history detector verifies large blobs, chunk boundaries and historical paths using disposable Git', () => {
  const run = spawnSync('python3', [scanner, '--self-test'], { encoding: 'utf8' });
  assert.equal(run.status, 0, run.stderr);
  assert.match(run.stdout, /synthetic history\/large-blob\/boundary\/privacy self-test: PASS/);
  assert.doesNotMatch(run.stdout, /ghp_[A-Z]{30,}/);
});

test('audit flags historical private path without disclosing it, and detects synthetic token without logging it', () => {
  const cwd = fs.mkdtempSync(path.join(os.tmpdir(), 'eduos-history-qa-'));
  try {
    git(cwd, 'init', '-q', '--initial-branch=fixture');
    const historicPath = path.join(cwd, '.env.production');
    fs.writeFileSync(historicPath, 'synthetic=yes\n');
    commit(cwd, 'synthetic historic private config');
    fs.unlinkSync(historicPath);
    fs.writeFileSync(path.join(cwd, 'safe.txt'), 'safe\n');
    commit(cwd, 'remove historic config');
    const flagged = spawnSync('python3', [scanner], { cwd, encoding: 'utf8' });
    assert.equal(flagged.status, 3, flagged.stderr);
    const counts = JSON.parse(flagged.stdout.trim().split('\n')[0]);
    assert.equal(counts.current_forbidden_paths, 0);
    assert.ok(counts.historical_forbidden_unique_paths >= 1);
    assert.equal(counts.high_confidence_secret_blob_matches, 0);
    assert.doesNotMatch(flagged.stdout, /\.env\.production|synthetic=yes/);

    const syntheticToken = 'ghp_' + 'Z'.repeat(40);
    fs.writeFileSync(path.join(cwd, 'sample.txt'), syntheticToken);
    commit(cwd, 'synthetic token fixture');
    const sensitive = spawnSync('python3', [scanner], { cwd, encoding: 'utf8' });
    assert.equal(sensitive.status, 2, sensitive.stderr);
    assert.ok(JSON.parse(sensitive.stdout.split('\n')[0]).high_confidence_secret_blob_matches >= 1);
    assert.ok(!sensitive.stdout.includes(syntheticToken));
    assert.doesNotMatch(sensitive.stdout, /sample\.txt|\.env\.production/);
  } finally {
    fs.rmSync(cwd, { recursive: true, force: true });
  }
});

test('history workflow uses read-only complete checkout and never persists credentials', () => {
  const source = fs.readFileSync(workflow, 'utf8');
  assert.match(source, /fetch-depth:\s*0/);
  assert.match(source, /persist-credentials:\s*false/);
  assert.match(source, /permissions:\s*\n\s*contents:\s*read/);
  assert.match(source, /audit_git_exposure\.py --self-test/);
  assert.match(source, /run: python3 scripts\/security\/audit_git_exposure\.py\s*$/m);
});
