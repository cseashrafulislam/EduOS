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
PATTERN_KINDS = ('github_classic', 'github_fine_grained', 'aws_access_key_id',
                 'private_key_header', 'slack_token', 'live_payment_key')


def redact_path(value):
    """Prevent recognized token patterns embedded in filenames from entering local reports."""
    data = value.encode('utf-8', 'replace')
    for pattern in PATTERNS:
        data = pattern.sub(b'[REDACTED_PATTERN]', data)
    return data.decode('utf-8', 'replace')


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


def consume_blob(stream, size, inspect, match_kinds=None):
    """Consume exactly size bytes; inspect with constant memory across chunk boundaries."""
    tail = b''
    matched = False
    remaining = size
    while remaining:
        chunk = stream.read(min(remaining, SCAN_CHUNK))
        if not chunk:
            raise RuntimeError('Truncated Git object stream')
        remaining -= len(chunk)
        if inspect and (not matched or match_kinds is not None):
            window = tail + chunk
            if match_kinds is None:
                matched = any(pattern.search(window) for pattern in PATTERNS)
            else:
                for kind, pattern in zip(PATTERN_KINDS, PATTERNS):
                    if pattern.search(window):
                        matched = True
                        match_kinds.add(kind)
            tail = window[-SCAN_OVERLAP:]
    return matched


def scan(cwd=None, private_findings=None):
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
            objects.append((oid, path))
    proc = subprocess.Popen(('git', 'cat-file', '--batch'), cwd=cwd, stdin=subprocess.PIPE,
                            stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, bufsize=0)
    checked = large_checked = skipped = matches = inspected_bytes = 0
    try:
        for oid, path in objects:
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
            kinds = set() if private_findings is not None and inspect else None
            hit = consume_blob(proc.stdout, size, inspect, kinds)
            matches += hit
            if hit and private_findings is not None:
                private_findings.setdefault('secret_blob_indicators', []).append({
                    'object_id': oid.decode('ascii'),
                    'path_hint': redact_path(path.decode('utf-8', 'replace')),
                    'signature_kinds': sorted(kinds)})
            if read_exact(proc.stdout, 1) != b'\n':
                raise RuntimeError('Malformed Git object boundary')
    finally:
        proc.stdin.close()
        proc.stdout.close()
        proc.wait(timeout=30)
    if private_findings is not None:
        private_findings['current_sensitive_paths'] = sorted(redact_path(p) for p in paths if unsafe_path(p))
        private_findings['historical_sensitive_paths'] = sorted(redact_path(p) for p in forbidden_paths)
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
        Path(temp, '.env.' + marker.decode()).write_text('synthetic=yes\n', encoding='utf-8')
        commit('synthetic historic config')
        os.unlink(Path(temp, '.env.production'))
        os.unlink(Path(temp, '.env.' + marker.decode()))
        Path(temp, 'safe.txt').write_bytes(boundary)
        commit('synthetic large boundary marker')
        private = {}
        result = scan(temp, private_findings=private)
        assert '.env.production' in private['historical_sensitive_paths']
        assert any('github_classic' in row['signature_kinds'] for row in private['secret_blob_indicators'])
        assert marker.decode() not in json.dumps(private)
        assert result['current_forbidden_paths'] == 0
        assert result['historical_forbidden_unique_paths'] >= 1
        assert result['history_large_blobs_scanned'] >= 1
        assert result['history_large_blobs_not_scanned'] == 0
        assert result['high_confidence_secret_blob_matches'] >= 1
        assert marker.decode() not in json.dumps(result)
    print('Detector synthetic history/large-blob/boundary/privacy self-test: PASS')


def local_private_report(destination):
    """Explicit offline-only triage. Report paths/object IDs, NEVER secret values."""
    if (os.environ.get('GITHUB_ACTIONS') or
            os.environ.get('EDUOS_PRIVATE_TRIAGE') != 'I_ACKNOWLEDGE_LOCAL_ONLY' or
            not sys.stdin.isatty()):
        print('Private triage requires an interactive trusted workstation and explicit opt-in.', file=sys.stderr)
        return 5
    try:
        root = Path(git('rev-parse', '--show-toplevel').decode().strip()).resolve()
        target = Path(destination).expanduser()
        if not target.is_absolute():
            raise ValueError('Private report destination must be absolute')
        parent = target.parent.resolve(strict=True)
        if parent == root or root in parent.parents:
            raise ValueError('Private report must be outside the repository')
        if parent.stat().st_mode & 0o077:
            raise ValueError('Private report directory must be owner-only (0700)')
        if target.is_symlink() or target.exists():
            raise ValueError('Private report destination must not exist')
        details = {}
        aggregate = scan(private_findings=details)
        flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL
        if hasattr(os, 'O_NOFOLLOW'):
            flags |= os.O_NOFOLLOW
        fd = os.open(str(target), flags, 0o600)
        with os.fdopen(fd, 'w', encoding='utf-8') as handle:
            json.dump({'aggregate': aggregate, 'local_only_indicators': details},
                      handle, sort_keys=True, indent=2)
            handle.write('\n')
        print('Private local report written. No credential values were recorded.')
        return 0
    except (OSError, RuntimeError, ValueError, subprocess.CalledProcessError) as exc:
        print('Private triage not completed:', type(exc).__name__, file=sys.stderr)
        return 5


def main():
    if '--local-report' in sys.argv:
        if len(sys.argv) != 3 or sys.argv[1] != '--local-report':
            print('Usage: --local-report ABSOLUTE_PRIVATE_PATH', file=sys.stderr)
            return 5
        return local_private_report(sys.argv[2])
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
