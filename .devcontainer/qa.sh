#!/usr/bin/env bash
# Private Codespaces Staging QA. Hard-coded localhost SQL and fictional demo tenant.
set -euo pipefail
umask 077
repo="$(cd "$(dirname "$0")/.." && pwd)"
qa_home="$HOME/.config/eduos/codespaces-qa"
secret_file="$qa_home/secrets"
password_file="$HOME/.config/eduos/dev-sql-password"
pid_file="$qa_home/app.pid"
sha_file="$qa_home/app.commit"
app_log="$qa_home/app.log"
origin="https://qa.eduos.test:7243"
cd "$repo"
usage() {
  echo "Usage: bash .devcontainer/qa.sh {start|status|test|test-academic|stop|stop-all}"
}
guard_branch() {
  if [ "$(git branch --show-current)" != "Ashraful" ]; then
    echo "Refusing QA outside the Ashraful branch." >&2
    exit 1
  fi
}
prepare() {
  command -v openssl >/dev/null || { echo "openssl required" >&2; exit 1; }
  mkdir -p "$qa_home" "$qa_home/keys" "$qa_home/private" "$qa_home/node"
  chmod 700 "$qa_home" "$qa_home/keys" "$qa_home/private" "$qa_home/node"
  if [ ! -s "$secret_file" ]; then
    {
      printf 'QA_DEMO_PASSWORD=%q\n' "Qa_$(openssl rand -hex 24)_aA9!"
      printf 'QA_JWT_SECRET=%q\n' "$(openssl rand -hex 40)"
      printf 'QA_CERT_PASSWORD=%q\n' "$(openssl rand -hex 24)"
    } > "$secret_file"
    chmod 600 "$secret_file"
  fi
  # Generated only by this script, owner-readable outside the Git checkout.
  # shellcheck disable=SC1090
  source "$secret_file"
  if [ -z "${QA_PLATFORM_PASSWORD:-}" ]; then
    printf 'QA_PLATFORM_PASSWORD=%q\n' "Qa_Plat_$(openssl rand -hex 30)_aA9!" >> "$secret_file"
    chmod 600 "$secret_file"
    source "$secret_file"
  fi
  export QA_DEMO_PASSWORD QA_JWT_SECRET QA_CERT_PASSWORD QA_PLATFORM_PASSWORD
  if [ ! -s "$qa_home/qa.pfx" ]; then
    openssl req -x509 -nodes -newkey rsa:2048 -days 30 \
      -keyout "$qa_home/qa.key" -out "$qa_home/qa.crt" -subj "/CN=qa.eduos.test" \
      -addext "subjectAltName=DNS:qa.eduos.test,DNS:localhost" >/dev/null 2>&1
    openssl pkcs12 -export -out "$qa_home/qa.pfx" \
      -inkey "$qa_home/qa.key" -in "$qa_home/qa.crt" \
      -passout "pass:$QA_CERT_PASSWORD"
    chmod 600 "$qa_home/qa.key" "$qa_home/qa.crt" "$qa_home/qa.pfx"
  fi
  if ! grep -Eq '(^|[[:space:]])qa[.]eduos[.]test([[:space:]]|$)' /etc/hosts; then
    command -v sudo >/dev/null || { echo "sudo required to register local QA hostname" >&2; exit 1; }
    printf '127.0.0.1 qa.eduos.test\n' | sudo tee -a /etc/hosts >/dev/null
  fi
  codespace="$(printenv CODESPACE_NAME || true)"
  if [ -n "$codespace" ] && ! [[ "$codespace" =~ ^[a-z0-9][a-z0-9-]{0,79}$ ]]; then
    echo "Invalid Codespaces hostname." >&2
    exit 1
  fi
  allowed_hosts="qa.eduos.test;localhost;127.0.0.1"
  preview_origin="https://localhost:7243"
  if [ -n "$codespace" ]; then
    allowed_hosts="$allowed_hosts;$codespace-7243.app.github.dev"
    preview_origin="https://$codespace-7243.app.github.dev"
  fi
  export NO_PROXY="qa.eduos.test,localhost,127.0.0.1"
  export no_proxy="$NO_PROXY"
}
connection() {
  if [ ! -s "$password_file" ]; then
    echo "Run bash .devcontainer/sqlserver.sh start first." >&2
    exit 1
  fi
  bash "$repo/.devcontainer/sqlserver.sh" status
  sql_password="$(cat "$password_file")"
  export ConnectionStrings__DefaultConnection="Server=127.0.0.1,14333;Database=EduOS_Demo_Codespaces;User Id=sa;Password=$sql_password;Encrypt=True;TrustServerCertificate=True;Connect Timeout=30"
  export DatabaseInitialization__Enabled=false
  export DatabaseInitialization__ApplyMigrations=false
}
app_pid() {
  [ -s "$pid_file" ] || return 1
  pid="$(cat "$pid_file")"
  [[ "$pid" =~ ^[0-9]+$ ]] || return 1
  ps -p "$pid" -o args= 2>/dev/null |
    grep -Fq "$repo/EduOS.App/bin/Release/net10.0/EduOS.App.dll" || return 1
  printf '%s\n' "$pid"
}
ready() {
  curl --noproxy '*' -kfsS --connect-timeout 3 --max-time 7 "$origin/health/ready" >/dev/null 2>&1
}
start() {
  guard_branch
  prepare
  bash .devcontainer/sqlserver.sh start
  connection
  local head
  head="$(git rev-parse HEAD)"
  if app_pid >/dev/null && ready; then
    if [ -s "$sha_file" ] && [ "$(cat "$sha_file")" = "$head" ]; then
      echo "Already running tested app commit $head: $origin"
      return
    fi
    echo "Staging app was built from a different or unknown commit; restarting safely."
    stop
  elif app_pid >/dev/null; then
    echo "Staging app is not healthy; stopping before a clean restart."
    stop
  fi
  echo "Building EduOS and migrating private demo database EduOS_Demo_Codespaces..."
  dotnet restore EduOS.slnx
  dotnet build EduOS.slnx -c Release --no-restore
  dotnet ef database update --project EduOS.Persistence --startup-project EduOS.App \
    --context EduOSDbContext --configuration Release --no-build
  export ASPNETCORE_ENVIRONMENT=Staging
  export EduOSQA__EnableProgrammeTrace=true
  export ASPNETCORE_URLS="https://0.0.0.0:7243"
  export ASPNETCORE_Kestrel__Certificates__Default__Path="$qa_home/qa.pfx"
  export ASPNETCORE_Kestrel__Certificates__Default__Password="$QA_CERT_PASSWORD"
  export AllowedHosts="$allowed_hosts"
  export Cors__AllowedOrigins__0="$origin"
  export Cors__AllowedOrigins__1="$preview_origin"
  export DataProtection__KeysPath="$qa_home/keys"
  export FileStorage__PrivateBasePath="$qa_home/private"
  export DemoData__Enabled=true
  export DemoData__SharedPassword="$QA_DEMO_PASSWORD"
  export DemoData__PlatformQaPassword="$QA_PLATFORM_PASSWORD"
  export JwtSettings__Secret="$QA_JWT_SECRET"
  export Payments__AamarPay__IsSandbox=true
  export SSLCommerz__IsSandbox=true
  export ManualPayment__Enabled=false
  export SmsSettings__ApiKey=""
  export SmsSettings__ApiSecret=""
  export EmailSettings__Password=""
  : > "$app_log"
  (
    cd "$repo/EduOS.App"
    nohup dotnet "$repo/EduOS.App/bin/Release/net10.0/EduOS.App.dll" \
      >> "$app_log" 2>&1 </dev/null &
    echo "$!" > "$pid_file"
  )
  for i in $(seq 1 90); do
    if ready; then
      printf '%s\n' "$head" > "$sha_file"
      chmod 600 "$sha_file"
      echo "EduOS private Staging QA READY: $origin (commit $head)"
      echo "Demo login: demo.tenantadmin@eduos.example.invalid"
      echo "Demo credential generated privately at $secret_file (not printed)."
      if [ -n "$codespace" ]; then
        echo "Codespaces private Ports panel (7243): $preview_origin"
      fi
      return
    fi
    if ! app_pid >/dev/null; then
      echo "EduOS startup failed. Inspect local private log: $app_log" >&2
      exit 1
    fi
    sleep 2
  done
  echo "EduOS app readiness timeout. Inspect local private log: $app_log" >&2
  exit 1
}
status() {
  bash .devcontainer/sqlserver.sh status
  if app_pid >/dev/null && ready; then
    echo "EduOS Codespaces Staging QA ready at $origin"
  else
    echo "EduOS Codespaces Staging QA not running."
  fi
}
browser() {
  bin="$qa_home/node/node_modules/.bin/playwright"
  if [ ! -x "$bin" ]; then
    npm install --prefix "$qa_home/node" --no-save --package-lock=false \
      --no-audit --no-fund playwright@1.56.0
  fi
  if [ ! -f "$qa_home/browser-ready" ]; then
    "$bin" install --with-deps chromium
    : > "$qa_home/browser-ready"
  fi
  export NODE_PATH="$qa_home/node/node_modules"
}
run_tests() {
  local scope="${1:-all}"
  if [ "$scope" != "all" ] && [ "$scope" != "academic" ]; then
    echo "Unsupported QA scope." >&2
    exit 2
  fi
  guard_branch
  if [ -n "$(git status --porcelain --untracked-files=no)" ]; then
    echo "Refusing exact-SHA QA with uncommitted tracked changes; commit or stash them first." >&2
    exit 1
  fi
  # start is idempotent and must initialize local SQL/credentials BEFORE
  # any connection validation; first-run Codespaces has no SQL password file.
  start
  connection
  browser
  suffix="$(date -u +%Y%m%dT%H%M%S)-$(openssl rand -hex 3)"
  export GITHUB_SHA="$(git rev-parse HEAD)"
  export EDUOS_QA_ORIGIN="$origin"
  export QA_YEAR_NAME="QA Academic Year $suffix"
  export QA_TERM_NAME="QA First Term $suffix"
  export QA_PROGRAM_NAME="QA Programme $suffix"
  export QA_LEVEL_NAME="QA Class One $suffix"
  export QA_SUBJECT_NAME="QA Subject $suffix"
  export QA_SECTION_NAME="QA Section A $suffix"
  export QA_CURRICULUM_NAME="QA Curriculum $suffix"
  export QA_BATCH_NAME="QA Batch $suffix"
  export QA_STUDENT_NAME="QA Student $suffix"
  export QA_GUARDIAN_NAME="QA Guardian $suffix"
  export QA_STUDENT_ROLL="QA-Roll-$suffix"
  export EDUOS_SQL_REAL_QA_CONNECTION="$ConnectionStrings__DefaultConnection"
  mkdir -p qa-artifacts
  echo "Executing real Codespaces browser/SQL checks at commit $GITHUB_SHA..."
  if [ "$scope" = "all" ]; then
    node tests/browser/real-data-entry.cjs
  fi
  node tests/browser/real-academic-foundation.cjs
  if [ "$scope" = "all" ]; then
    # Dependency order matches .github/workflows/real-data-entry-qa.yml.
    node tests/browser/real-student-enrollment.cjs
    node tests/browser/real-menu-route-sweep.cjs
    node tests/browser/real-tenantadmin-page-render.cjs
    node tests/browser/real-roleexclusive-page-render.cjs
    node tests/browser/real-privileged-route-coverage.cjs
    node scripts/qa/compare-real-route-coverage.cjs
    node scripts/qa/page-action-coverage.cjs
  fi
  local filter="FullyQualifiedName~SqlServerRealAcademicFoundationEvidenceTests"
  if [ "$scope" = "all" ]; then
    filter="FullyQualifiedName~SqlServerRealDataEntryEvidenceTests|$filter|FullyQualifiedName~SqlServerRealStudentEnrollmentEvidenceTests"
  fi
  dotnet test EduOS.Tests/EduOS.Tests.csproj -c Release --no-build \
    --filter "$filter" --logger "trx;LogFileName=codespaces-$scope.trx" \
    --results-directory qa-artifacts/test-results
  node scripts/qa/release-readiness.cjs
  # Fail closed if the checked-out source changed or a browser suite
  # accidentally reused stale evidence from a different commit.
  test "$(git rev-parse HEAD)" = "$GITHUB_SHA"
  node - "$GITHUB_SHA" "$scope" <<'QA_EVIDENCE'
const assert=require('node:assert/strict');
const fs=require('node:fs');
const sha=process.argv[2],scope=process.argv[3];
const files=scope==='all'?
  ['real-data-entry','real-academic-foundation','real-student-enrollment',
    'real-menu-route-sweep','real-tenantadmin-page-render','real-roleexclusive-page-render',
    'real-privileged-route-coverage','real-route-coverage-gap']:
  ['real-academic-foundation'];
for(const name of files){
  const data=JSON.parse(fs.readFileSync('qa-artifacts/'+name+'.json','utf8'));
  assert.equal(data.sha,sha,'Evidence belongs to different commit: '+name);
  if(name==='real-menu-route-sweep')
    assert.equal(data.results.length,13,'Missing role menu checks');
  if(name==='real-tenantadmin-page-render')
    assert(data.checked>=100&&data.failed===0,'TenantAdmin page render incomplete');
  if(name==='real-roleexclusive-page-render')
    assert(data.totalDistinctPages>=100&&data.failed===0,'Role pages incomplete');
}
console.log('PASS exact-commit browser evidence:',files.length,'artifact groups');
QA_EVIDENCE
  echo "Codespaces $scope QA PASS for tested scenarios; staging/production UAT sign-off still required."
}
stop() {
  if pid="$(app_pid)"; then
    kill "$pid"
    echo "Private demo app stopped, SQL data preserved."
  else
    echo "Private demo app is already stopped."
  fi
  rm -f "$pid_file" "$sha_file"
}
case "${1:-}" in
  start) start ;;
  status) status ;;
  test) run_tests all ;;
  test-academic) run_tests academic ;;
  stop) stop ;;
  stop-all) stop; bash .devcontainer/sqlserver.sh stop ;;
  *) usage; exit 2 ;;
esac
