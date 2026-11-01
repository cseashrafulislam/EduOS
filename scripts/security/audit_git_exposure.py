#!/usr/bin/env python3
"""Read-only full-Git-history exposure scan; aggregate output only, never file paths or values."""
import argparse
import json
import re
import subprocess
import sys

MAX_BLOB = 1024 * 1024
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


def git(*args):
    return subprocess.check_output(('git',) + args, stderr=subprocess.DEVNULL)


def read_exact(stream, size):
    chunks = []
    remaining = size
    while remaining:
        block = stream.read(min(remaining, 65536))
        if not block:
            raise RuntimeError('Truncated Git object stream')
        chunks.append(block)
        remaining -= len(block)
    return b''.join(chunks)


def scan():
    if git('rev-parse', '--is-shallow-repository').strip() != b'false':
        raise RuntimeError('Shallow Git history')
    paths = [x.decode('utf-8', 'replace') for x in git('ls-files', '-z').split(b'\0') if x]
    history = git('rev-list', '--objects', '--all').splitlines()
    objects, seen, old_paths = [], set(), 0
    for line in history:
        oid, _, path = line.partition(b' ')
        old_paths += bool(path and unsafe_path(path.decode('utf-8', 'replace')))
        if len(oid) in (40, 64) and oid not in seen:
            seen.add(oid)
            objects.append(oid)
    proc = subprocess.Popen(('git', 'cat-file', '--batch'), stdin=subprocess.PIPE,
                            stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, bufsize=0)
    checked = large = matches = 0
    try:
        for oid in objects:
            proc.stdin.write(oid + b'\n')
            header = proc.stdout.readline().split()
            if len(header) != 3 or header[0] != oid:
                raise RuntimeError('Malformed Git object header')
            kind, size = header[1], int(header[2])
            if kind == b'blob' and size <= MAX_BLOB:
                data = read_exact(proc.stdout, size)
                if len(data) != size:
                    raise RuntimeError('Truncated blob')
                checked += 1
                # Ignore only the exact historical synthetic self-test source line.
                # Do not ignore the same key marker elsewhere or other content.
                fixture = (b"assert any(p.search(b'-----BEGIN " +
                           b"OPENSSH PRIVATE KEY-----') for p in PATTERNS)")
                inspected = data.replace(fixture, b'<known synthetic self-test>')
                matches += bool(b'\0' not in inspected[:8192] and
                                any(p.search(inspected) for p in PATTERNS))
            else:
                large += kind == b'blob'
                while size:
                    block = proc.stdout.read(min(size, 65536))
                    if not block:
                        raise RuntimeError('Truncated object')
                    size -= len(block)
            if read_exact(proc.stdout, 1) != b'\n':
                raise RuntimeError('Malformed object boundary')
    finally:
        proc.stdin.close()
        proc.stdout.close()
        proc.wait(timeout=30)
    return dict(tracked_files=len(paths), current_forbidden_paths=sum(map(unsafe_path, paths)),
                historical_forbidden_path_objects=old_paths, history_blobs_scanned=checked,
                history_large_blobs_not_scanned=large, high_confidence_secret_blob_matches=matches)


def main():
    if '--self-test' in sys.argv:
        assert unsafe_path('wwwroot/uploads/customer/file.pdf')
        assert unsafe_path('App_Data/private-uploads/a.jpg')
        assert unsafe_path('config/.env.production')
        assert not unsafe_path('.env.example')
        assert not unsafe_path('EduOS.Core/Common/PrivateCsvCell.cs')
        assert any(p.search(b'-----BEGIN ' + b'OPENSSH PRIVATE KEY-----') for p in PATTERNS)
        print('Detector synthetic self-test: PASS')
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
    if result['historical_forbidden_path_objects'] or result['history_large_blobs_not_scanned']:
        print('HISTORY REVIEW REQUIRED: flagged paths or large blobs not scanned.')
        return 3
    print('No high-confidence matches in inspected objects; not a comprehensive secret scan.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
