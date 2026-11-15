#!/usr/bin/env bash
set -euo pipefail
action="${1:-}"
container="eduos-local-sqlserver"
password_file="${HOME}/.config/eduos/dev-sql-password"
prepare_password() {
  if [ ! -f "$password_file" ]; then
    umask 077
    mkdir -p "$(dirname "$password_file")"
    printf 'EduOS_%s_aA1!\n' "$(openssl rand -hex 20)" > "$password_file"
    chmod 600 "$password_file"
  fi
}
is_running() {
  docker inspect --format '{{.State.Running}}' "$container" 2>/dev/null | grep -qx true
}
# Existing SQL data volumes retain their original sa password.
# Password input travels via stdin to sqlcmd; no secret is logged.
authenticate() {
  local password="$1"
  [ -n "$password" ] || return 1
  printf '%s\n' "$password" | docker exec -i "$container" /bin/bash -c '
    IFS= read -r SQLCMDPASSWORD || exit 1
    export SQLCMDPASSWORD
    if [ -x /opt/mssql-tools18/bin/sqlcmd ]; then
      client=/opt/mssql-tools18/bin/sqlcmd
    elif [ -x /opt/mssql-tools/bin/sqlcmd ]; then
      client=/opt/mssql-tools/bin/sqlcmd
    else
      exit 2
    fi
    "$client" -S localhost -U sa -C -b -l 5 -Q "SELECT 1" >/dev/null 2>&1
  ' >/dev/null 2>&1
}
save_verified_password() {
  local tmp
  umask 077
  mkdir -p "$(dirname "$password_file")"
  tmp="$(mktemp "$password_file.new.XXXXXX")"
  printf '%s\n' "$1" > "$tmp"
  chmod 600 "$tmp"
  mv -f "$tmp" "$password_file"
}
# An authenticated SQL query is authoritative for readiness. A Docker log
# grep can give a false negative under pipefail if grep exits early (SIGPIPE).
sql_ready() {
  local saved initial
  saved="$(cat "$password_file")"
  if authenticate "$saved"; then return 0; fi
  # Preserve existing SQL volumes when the saved credential differs from
  # the existing container's original credential. Never reset SQL data.
  initial="$(docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' "$container" 2>/dev/null |
    sed -n 's/^MSSQL_SA_PASSWORD=//p' | head -n 1)"
  if [ -n "$initial" ] && [ "$initial" != "$saved" ] && authenticate "$initial"; then
    save_verified_password "$initial"
    echo "Re-synchronized existing SQL credential; database preserved."
    return 0
  fi
  return 1
}
report_startup_failure() {
  local log_dir log_file
  log_dir="$HOME/.config/eduos"
  mkdir -p "$log_dir"
  chmod 700 "$log_dir"
  log_file="$log_dir/sqlserver-startup.log"
  # Keep detailed SQL logs private; report only non-sensitive diagnostics.
  (umask 077; docker logs --tail 400 "$container" > "$log_file" 2>&1) || true
  chmod 600 "$log_file" 2>/dev/null || true
  echo "SQL container state (no data deleted):" >&2
  docker inspect --format 'state={{.State.Status}} exit={{.State.ExitCode}} oom={{.State.OOMKilled}}' "$container" >&2 || true
  if grep -Eiq 'out of memory|insufficient memory|not enough memory|memory allocation' "$log_file"; then
    echo "Startup diagnosis: SQL Server reported a memory/resource issue." >&2
  elif grep -Eiq 'permission denied|access denied|operation not permitted' "$log_file"; then
    echo "Startup diagnosis: SQL Server reported a filesystem/permission issue." >&2
  elif grep -Eiq 'password validation failed|password does not meet' "$log_file"; then
    echo "Startup diagnosis: SQL Server reported a password configuration issue." >&2
  else
    echo "Startup diagnosis: SQL readiness/authentication unavailable; inspect private SQL logs." >&2
  fi
  echo "Private diagnostic log: $log_file (owner-readable; do not paste secrets)." >&2
}
verify_password() {
  local saved initial attempt
  saved="$(cat "$password_file")"
  for attempt in 1 2 3 4 5; do
    if authenticate "$saved"; then return 0; fi
    sleep 2
  done
  initial="$(docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' "$container" |
    sed -n 's/^MSSQL_SA_PASSWORD=//p' | head -n 1)"
  if [ -n "$initial" ] && [ "$initial" != "$saved" ] && authenticate "$initial"; then
    save_verified_password "$initial"
    echo "Re-synchronized existing SQL credential from container; databases unchanged."
    return 0
  fi
  echo "SQL login sa failed (18456): saved password and SQL data volume disagree." >&2
  echo "No data was deleted. If you know the valid old password, run: bash .devcontainer/sqlserver.sh adopt-password" >&2
  return 1
}
case "$action" in
  start)
    command -v docker >/dev/null || { echo "Docker required" >&2; exit 1; }
    command -v openssl >/dev/null || { echo "openssl required" >&2; exit 1; }
    prepare_password
    if is_running; then
      verify_password
      echo "EduOS SQL Server running; sa authentication verified at 127.0.0.1:14333."
      exit 0
    fi
    if docker container inspect "$container" >/dev/null 2>&1; then
      docker start "$container" >/dev/null
    else
      docker run -d --name "$container" \
        -p 127.0.0.1:14333:1433 \
        -e ACCEPT_EULA=Y -e MSSQL_PID=Developer \
        -e MSSQL_MEMORY_LIMIT_MB=2048 \
        -e MSSQL_SA_PASSWORD="$(cat "$password_file")" \
        -v eduos-local-sql-data:/var/opt/mssql \
        mcr.microsoft.com/mssql/server:2022-latest >/dev/null
    fi
    for i in $(seq 1 90); do
      if sql_ready; then
        echo "SQL Server ready and authenticated at 127.0.0.1:14333 (dev only)."
        exit 0
      fi
      if ! is_running; then
        echo "SQL Server container stopped unexpectedly." >&2
        report_startup_failure
        exit 1
      fi
      sleep 2
    done
    echo "SQL Server did not accept an authenticated readiness query in time." >&2
    report_startup_failure
    exit 1
    ;;
  stop)
    if docker container inspect "$container" >/dev/null 2>&1; then
      docker stop "$container" >/dev/null
      echo "Development SQL Server stopped. Named data volume preserved."
    else
      echo "Development SQL Server does not exist."
    fi
    ;;
  status)
    if is_running; then
      echo "EduOS SQL Server running (localhost:14333)."
      if [ -s "$password_file" ]; then verify_password
      else echo "SQL password file missing; run start." >&2; exit 1; fi
    else echo "EduOS SQL Server is not running."; fi
    ;;
  adopt-password)
    if ! is_running || [ ! -t 0 ]; then
      echo "Run this command interactively against a running local container." >&2
      exit 1
    fi
    IFS= read -r -s -p "Existing sa password (hidden): " entered
    printf '\n'
    if authenticate "$entered"; then
      save_verified_password "$entered"
      unset entered
      echo "Local password file synchronized; SQL database unchanged."
    else
      unset entered
      echo "Invalid password; no data or credentials changed." >&2
      exit 1
    fi
    ;;
  exec)
    shift
    if [ "$#" -eq 0 ]; then echo "Provide a command after exec." >&2; exit 2; fi
    if ! is_running || [ ! -f "$password_file" ]; then
      echo "Start isolated SQL Server first: bash .devcontainer/sqlserver.sh start" >&2
      exit 1
    fi
    verify_password
    export ConnectionStrings__DefaultConnection="Server=127.0.0.1,14333;Database=EduOS_Development;User Id=sa;Password=$(cat "$password_file");Encrypt=True;TrustServerCertificate=True;Connect Timeout=30"
    export DatabaseInitialization__Enabled=false
    export DatabaseInitialization__ApplyMigrations=false
    exec "$@"
    ;;
  migrate)
    shift
    # Operator-controlled action against EduOS_Development only.
    exec bash "$0" exec dotnet ef database update \
      --project EduOS.Persistence --startup-project EduOS.App \
      --context EduOSDbContext --configuration Release "$@"
    ;;
  *)
    echo "Usage: bash .devcontainer/sqlserver.sh {start|stop|status|adopt-password|migrate|exec COMMAND...}" >&2
    echo "Dedicated local-only SQL Server. Password stored in ~/.config/eduos, never in Git." >&2
    exit 2
    ;;
esac
