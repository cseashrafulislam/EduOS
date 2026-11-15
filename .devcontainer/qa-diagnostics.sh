#!/usr/bin/env bash
# Isolated Codespaces QA timeout diagnostics. No SQL text or credentials are printed.
set -euo pipefail
umask 077
if [ "${EDUOS_QA_DIAGNOSTICS:-}" != "1" ] ||
   [ "${EDUOS_QA_ORIGIN:-}" != "https://qa.eduos.test:7243" ]; then
  echo "Diagnostics restricted to isolated QA."
  exit 2
fi
repo="$(cd "$(dirname "$0")/.." && pwd)"
qa_home="$HOME/.config/eduos/codespaces-qa"
password_file="$HOME/.config/eduos/dev-sql-password"
stamp="$(date -u +%Y%m%dT%H%M%SZ)-$$"
output="$repo/qa-artifacts/diagnostics-$stamp"
mkdir -p "$output"
chmod 700 "$output"
if [ -f "$qa_home/app.log" ]; then
  tail -n 250 "$qa_home/app.log" > "$output/app.log"
  chmod 600 "$output/app.log"
fi
echo "[QA diagnostics] Private app log: $output/app.log"
if [ ! -s "$password_file" ] ||
   ! docker inspect --format '{{.State.Running}}' eduos-local-sqlserver 2>/dev/null | grep -qx true; then
  echo "[QA diagnostics] SQL Server unavailable; app log still captured."
  exit 0
fi
# Use the container-internal SQL client. SQLCMDPASSWORD is supplied on stdin,
# never interpolated into a command or written into the diagnostic artifact.
if printf '%s\n' "$(cat "$password_file")" | docker exec -i eduos-local-sqlserver /bin/bash -c '
  IFS= read -r SQLCMDPASSWORD || exit 1
  export SQLCMDPASSWORD
  if [ -x /opt/mssql-tools18/bin/sqlcmd ]; then
    sqlcmd=/opt/mssql-tools18/bin/sqlcmd
  elif [ -x /opt/mssql-tools/bin/sqlcmd ]; then
    sqlcmd=/opt/mssql-tools/bin/sqlcmd
  else
    exit 2
  fi
  "$sqlcmd" -S localhost -U sa -C -d EduOS_Demo_Codespaces -W -l 5 -t 8 -Q "
    SET NOCOUNT ON;
    SELECT TOP (20) r.session_id, r.status, r.command,
      r.blocking_session_id AS blocked_by, r.wait_type, r.wait_time AS wait_ms,
      r.total_elapsed_time AS elapsed_ms
    FROM sys.dm_exec_requests AS r
    WHERE r.database_id = DB_ID() AND r.session_id <> @@SPID
    ORDER BY r.total_elapsed_time DESC;
    SELECT TOP (20) s.session_id, s.status, s.open_transaction_count,
      s.last_request_end_time
    FROM sys.dm_exec_sessions AS s
    WHERE s.is_user_process = 1 AND s.database_id = DB_ID()
      AND s.open_transaction_count > 0
    ORDER BY s.last_request_end_time;"
' > "$output/sql-sessions.txt" 2>&1; then
  chmod 600 "$output/sql-sessions.txt"
  echo "[QA diagnostics] SQL active requests / open transactions:"
  cat "$output/sql-sessions.txt"
else
  echo "[QA diagnostics] SQL metadata query failed (see private diagnostic artifact)."
fi
echo "[QA diagnostics] Artifacts: $output"
