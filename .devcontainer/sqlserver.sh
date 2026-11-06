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
case "$action" in
  start)
    command -v docker >/dev/null || { echo "Docker required" >&2; exit 1; }
    command -v openssl >/dev/null || { echo "openssl required" >&2; exit 1; }
    prepare_password
    if is_running; then
      echo "EduOS isolated SQL Server is already running on 127.0.0.1:14333."
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
      if docker logs "$container" 2>&1 | grep -q 'SQL Server is now ready for client connections'; then
        echo "SQL Server ready at 127.0.0.1:14333 (dev only; bound to loopback)."
        exit 0
      fi
      if ! is_running; then
        echo "SQL Server container stopped unexpectedly." >&2
        exit 1
      fi
      sleep 2
    done
    echo "SQL Server did not become ready in time." >&2
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
    if is_running; then echo "EduOS SQL Server running (localhost:14333)."
    else echo "EduOS SQL Server is not running."; fi
    ;;
  exec)
    shift
    if [ "$#" -eq 0 ]; then echo "Provide a command after exec." >&2; exit 2; fi
    if ! is_running || [ ! -f "$password_file" ]; then
      echo "Start isolated SQL Server first: bash .devcontainer/sqlserver.sh start" >&2
      exit 1
    fi
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
    echo "Usage: bash .devcontainer/sqlserver.sh {start|stop|status|migrate|exec COMMAND...}" >&2
    echo "Dedicated local-only SQL Server. Password stored in ~/.config/eduos, never in Git." >&2
    exit 2
    ;;
esac
