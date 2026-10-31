// Repository-level CI safety net: never commit tenant uploads or credential artifacts.
// Checks the Git index (not only .gitignore), so forced additions cannot bypass it.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { execFileSync, spawnSync } = require('node:child_process');
const { readFileSync, statSync } = require('node:fs');
const { resolve, join } = require('node:path');

const root = resolve(__dirname, '../..');
const git = args => execFileSync('git', args, {
    cwd: root, encoding: 'utf8', maxBuffer: 16 * 1024 * 1024
});
const tracked = () => git(['ls-files', '-z']).split('\0').filter(Boolean);

function forbiddenPath(file) {
    const normalized = file.replaceAll('\\', '/').toLowerCase();
    const segments = normalized.split('/');
    const name = segments.at(-1);
    return segments.includes('private-uploads') || segments.includes('.secrets') ||
        normalized.includes('/wwwroot/uploads/') || normalized.startsWith('wwwroot/uploads/') ||
        name === '.env' || (name.startsWith('.env.') && name !== '.env.example') ||
        name === 'appsettings.local.json' ||
        /\.(?:pfx|p12|publishsettings|mdf|ldf|ndf)$/.test(name);
}

function ignored(file) {
    const result = spawnSync('git', ['check-ignore', '--no-index', '-q', '--', file], {
        cwd: root, encoding: 'utf8'
    });
    assert.ok([0, 1].includes(result.status),
        `git check-ignore failed for ${file}: ${result.stderr}`);
    return result.status === 0;
}

test('sensitive path policy rejects tenant uploads and secrets, not templates', () => {
    for (const file of [
        'EduOS.App/App_Data/private-uploads/tenant-a/document.png',
        'EduOS.App/wwwroot/uploads/customer-photo.jpg',
        '.secrets/production.json', '.env', 'EduOS.App/.env.production',
        'EduOS.App/appsettings.Local.json', 'certs/signing.pfx',
        'deploy/production.publishsettings', 'backup/live.mdf'
    ]) assert.equal(forbiddenPath(file), true, file);
    for (const file of ['.env.example', 'EduOS.App/appsettings.json',
        'docs/operations/upload-policy.md', 'EduOS.Tests/Fixtures/sample.csv']) {
        assert.equal(forbiddenPath(file), false, file);
    }
});

test('Git index and ignore policy protect tenant uploads and credential files', () => {
    const violations = tracked().filter(forbiddenPath);
    assert.deepEqual(violations, [],
        'Private customer files or credential artifacts are tracked; remove from the index without deleting customer storage.');
    for (const file of [
        'EduOS.App/App_Data/private-uploads/tenant-a/document.png',
        'EduOS.App/wwwroot/uploads/customer-photo.jpg',
        'EduOS.App/.env.production', 'EduOS.App/appsettings.Local.json',
        'certs/signing.pfx'
    ]) assert.equal(ignored(file), true, `Missing .gitignore protection for ${file}`);
    assert.equal(ignored('.env.example'), false, 'Public environment template must remain trackable.');
});

test('tracked source contains no high-confidence private keys or provider access tokens', () => {
    // Restrict to source/config/docs; skip binaries and oversized generated/vendor files.
    const extensions = /(?:\.(?:cs|json|ya?ml|xml|config|js|mjs|cjs|ts|tsx|ps1|sh|py|md|txt)|(^|\/)Dockerfile)$/i;
    const markers = [
        /-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----/,
        /\bAKIA[A-Z0-9]{16}\b/,
        /\bgh[pousr]_[A-Za-z0-9]{36,}\b/,
        /\bgithub_pat_[A-Za-z0-9_]{80,}\b/
    ];
    const violations = [];
    let checked = 0;
    for (const file of tracked()) {
        if (!extensions.test(file) || file.includes('/lib/') || file.includes('/vendor/')) continue;
        const full = join(root, file);
        const stat = statSync(full);
        if (!stat.isFile() || stat.size > 1_000_000) continue;
        const content = readFileSync(full);
        if (content.includes(0)) continue;
        checked++;
        const text = content.toString('utf8');
        if (markers.some(pattern => pattern.test(text))) violations.push(file);
    }
    assert.ok(checked > 0, 'No source files were scanned.');
    assert.deepEqual(violations, [], 'High-confidence secret marker in tracked source.');
});
