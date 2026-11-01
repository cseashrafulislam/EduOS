#!/usr/bin/env python3
"""Read-only full-history exposure scan. Never emit source paths or secret values."""
import io
import json
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path

# Previous 1 MiB cap left large historic blobs unexamined. Stream up to this
# per-object limit with constant memory; report anything still skipped.
MAX_BLOB_SCAN = 128 * 1024 * 1024
LARGE_BLOB = 1024 * 1024
SCAN_CHUNK = 64 * 1024
SCAN_OVERLAP = 512
SECRETS = [
    rb'gh[pousr]_[A-Za-z0-9_]{30,}',
    rb'github_pat_[A-Za-z0-9_]{40,}',
    rb'AKIA[0-9A-Z]{16}',
    rb'-----BEGIN (?:RSA |EC |OPENSSH |ENCRYPTED )?PRIVATE KEY-----',
    rb'xox[baprs]-[A-Za-z0-9-]{20,}',
    rb'sk_live_[A-Za-z0-9]{20,}',
]
PATTERNS = [re.compile(p) for p in SECRETS]


def unsafe_path(value):
    parts = value.replace('\\', '/').lower().strip('/').split('/')
    name = parts[-1]
    return (name != '.env.example' and
            (name == '.env' or name.startswith('.env.') or
             name in ('appsettings.production.json', 'id_rsa', 'id_ed25519') or
             name.endswith(('.pfx', '.p12', '.pem', '.key', '.jks', '.keystore', '.mdf', '.ldf', '.bak')) or
             any(p in ('private-uploads', 'tenant-uploads', 'customer-uploads') for p in parts) or
             ('wwwroot' in parts and 'uploads' in parts)))


def git(*args, cwd=None):
    return subprocess.check_output(('git',) + args, cwd=cwd, stderr=subprocess.DEVNULL)


def read_exact(stream, size):
    chunks = []
    remaining = size
    while remaining:
        block = stream.read(min(remaining, SCAN_CHUNK))
        if not block:
            raise RuntimeError('Truncated Git object stream')
        chunks.append(block)
        remaining -= len(block)
    return b''.join(chunks)


def consume_blob(stream, size, inspect):
    """Consume exactly size bytes; inspect with constant memory across chunk boundaries."""
    tail = b''
    matched = False
    remaining = size
    while remaining:
        chunk = stream.read(min(remaining, SCAN_CHUNK))
        if not chunk:
            raise RuntimeError('Truncated Git object stream')
        remaining -= len(chunk)
        if inspect and not matched:
            window = tail + chunk
            matched = any(pattern.search(window) for pattern in PATTERNS)
            tail = window[-SCAN_OVERLAP:]
    return matched


def scan(cwd=None):
    if git('rev-parse', '--is-shallow-repository', cwd=cwd).strip() != b'false':
        raise RuntimeError('Shallow Git history')
    paths = [x.decode('utf-8', 'replace') for x in git('ls-files', '-z', cwd=cwd).split(b'\0') if x]
    history = git('rev-list', '--objects', '--all', cwd=cwd).splitlines()
    # rev-list emits only one name per object. Commit history also captures
    # renames/deletions and multiple historical names for reused blobs.
    changed_paths = git('log', '--all', '--name-only', '--format=', '-z', cwd=cwd).split(b'\0')
    forbidden_paths = set()
    for raw in changed_paths:
        name = raw.strip(b'\r\n').decode('utf-8', 'replace')
        if name and unsafe_path(name):
            forbidden_paths.add(name)
    objects, seen, old_path_objects = [], set(), 0
    for line in history:
        oid, _, path = line.partition(b' ')
        if path:
            name = path.decode('utf-8', 'replace')
            if unsafe_path(name):
                old_path_objects += 1
                forbidden_paths.add(name)
        if len(oid) in (40, 64) and oid not in seen:
            seen.add(oid)
            objects.append(oid)
    proc = subprocess.Popen(('git', 'cat-file', '--batch'), cwd=cwd, stdin=subprocess.PIPE,
                            stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, bufsize=0)
    checked = large_checked = skipped = matches = inspected_bytes = 0
    try:
        for oid in objects:
            proc.stdin.write(oid + b'\n')
            header = proc.stdout.readline().split()
            if len(header) != 3 or header[0] != oid:
                raise RuntimeError('Malformed Git object header')
            kind, size = header[1], int(header[2])
            inspect = kind == b'blob' and size <= MAX_BLOB_SCAN
            if kind == b'blob':
                if inspect:
                    checked += 1
                    inspected_bytes += size
                    large_checked += size > LARGE_BLOB
                else:
                    skipped += 1
            matches += consume_blob(proc.stdout, size, inspect)
            if read_exact(proc.stdout, 1) != b'\n':
                raise RuntimeError('Malformed Git object boundary')
    finally:
        proc.stdin.close()
        proc.stdout.close()
        proc.wait(timeout=30)
    return dict(tracked_files=len(paths), current_forbidden_paths=sum(map(unsafe_path, paths)),
                historical_forbidden_path_objects=old_path_objects,
                historical_forbidden_unique_paths=len(forbidden_paths),
                history_blobs_scanned=checked, history_large_blobs_scanned=large_checked,
                history_large_blobs_not_scanned=skipped,
                history_blob_bytes_scanned=inspected_bytes,
                high_confidence_secret_blob_matches=matches)


def self_test():
    assert unsafe_path('wwwroot/uploads/customer/file.pdf')
    assert unsafe_path('App_Data/private-uploads/a.jpg')
    assert unsafe_path('config/.env.production')
    assert not unsafe_path('.env.example')
    assert not unsafe_path('EduOS.Core/Common/PrivateCsvCell.cs')
    marker = b'ghp_' + b'Q' * 40  # Synthetic only; never a usable token.
    boundary = b'x' * (SCAN_CHUNK - 2) + marker + b'z' * (LARGE_BLOB + 5)
    assert consume_blob(io.BytesIO(boundary), len(boundary), True)
    assert not consume_blob(io.BytesIO(boundary), len(boundary), False)
    assert not consume_blob(io.BytesIO(b'x' * (SCAN_CHUNK + 5)), SCAN_CHUNK + 5, True)
    with tempfile.TemporaryDirectory(prefix='eduos-exposure-test-') as temp:
        git('init', '-q', '--initial-branch=fixture', cwd=temp)

        def commit(message):
            git('add', '-A', cwd=temp)
            git('-c', 'user.name=QA', '-c', 'user.email=qa@example.invalid',
                'commit', '-qm', message, cwd=temp)

        Path(temp, '.env.production').write_text('synthetic=yes\n', encoding='utf-8')
        commit('synthetic historic config')
        os.unlink(Path(temp, '.env.production'))
        Path(temp, 'safe.txt').write_bytes(boundary)
        commit('synthetic large boundary marker')
        result = scan(temp)
        assert result['current_forbidden_paths'] == 0
        assert result['historical_forbidden_unique_paths'] >= 1
        assert result['history_large_blobs_scanned'] >= 1
        assert result['history_large_blobs_not_scanned'] == 0
        assert result['high_confidence_secret_blob_matches'] >= 1
        assert marker.decode() not in json.dumps(result)
    print('Detector synthetic history/large-blob/boundary/privacy self-test: PASS')


def main():
    if '--self-test' in sys.argv:
        self_test()
        return 0
    try:
        result = scan()
    except (OSError, RuntimeError, ValueError, subprocess.CalledProcessError) as exc:
        print('Exposure audit incomplete:', type(exc).__name__, file=sys.stderr)
        return 4
    print(json.dumps(result, sort_keys=True))
    if result['current_forbidden_paths'] or result['high_confidence_secret_blob_matches']:
        print('SECURITY REVIEW REQUIRED: potential tracked/history exposure. Report privately.')
        return 2
    if result['historical_forbidden_unique_paths'] or result['history_large_blobs_not_scanned']:
        print('HISTORY REVIEW REQUIRED: flagged paths or uninspected blobs. Report privately.')
        return 3
    print('No high-confidence matches in inspected objects; not a comprehensive secret scan.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
