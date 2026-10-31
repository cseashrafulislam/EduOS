const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const bootstrap = read('EduOS.Persistence/Seed/DemoInstitutionSeeder.cs');
const program = read('EduOS.App/Program.cs');
const login = read('EduOS.App/Views/Account/Login.cshtml');
const account = read('EduOS.App/Controllers/AccountController.cs');
const loginJs = read('EduOS.App/wwwroot/js/account/login.js');
const sql = read('scripts/demo/01-seed-academic-demo.sql');

test('public demo login requires Staging and explicit opt-in, never platform admin', () => {
    assert.match(program, /DemoData:Enabled/);
    assert.match(program, /!app\.Environment\.IsStaging\(\)/);
    assert.match(program, /var requireDeploymentSafety = builder\.Environment\.IsProduction\(\) \|\|/);
    assert.match(program, /requireDeploymentSafety &&/);
    assert.match(login, /DemoHost\.EnvironmentName.*"Staging"/);
    assert.match(login, /DemoData:Enabled/);
    assert.match(login, /DemoInstitutionSeeder\.Roles/);
    assert.match(account, /\[ResponseCache\(NoStore = true, Location = ResponseCacheLocation.None\)\]\s*public IActionResult Login\(\)/);
    assert.doesNotMatch(bootstrap.split('public static readonly IReadOnlyList<string> Roles =')[1].split('];')[0], /SuperAdmin/);
    assert.match(bootstrap, /role\.IsSystemRole/);
    assert.match(loginJs, /form\.requestSubmit\(submitButton\)/);
    assert.match(loginJs, /demoPanel\.dataset\.demoPassword/);
    assert.match(loginJs, /fetch\('\/api\/auth\/login'/);
});

test('demo identities use Identity hashing, live-database guard and real tenant memberships', () => {
    assert.match(bootstrap, /UserManager<ApplicationUser>/);
    assert.match(bootstrap, /userManager\.CreateAsync\(user, password\)/);
    assert.match(bootstrap, /EduOS_Demo_/);
    assert.match(bootstrap, /x\.Code != TenantCode && x\.Code != "EDUOS-SYSTEM"/);
    assert.match(bootstrap, /TenantMemberships\.Add\(new TenantMembership/);
    assert.match(bootstrap, /UserCampusAccesses\.Add\(new UserCampusAccess/);
    assert.match(bootstrap, /IsOwner = roleName == "TenantAdmin"/);
    assert.doesNotMatch(bootstrap, /PasswordHash\s*=/);
});

test('SQL fixture is isolated, transactional, idempotent and refuses financial/audit fake rows', () => {
    assert.match(sql, /LEFT\(DB_NAME\(\), 11\) <> N'EduOS_Demo_'/);
    assert.match(sql, /__EFMigrationsHistory/);
    assert.match(sql, /EDUOS-DEMO/);
    assert.match(sql, /BEGIN TRANSACTION/);
    assert.match(sql, /ROLLBACK TRANSACTION/);
    assert.match(sql, /IF NOT EXISTS/);
    assert.match(sql, /WHILE @i <= 12/g);
    const insertedTables = [...sql.matchAll(/\bINSERT\s+dbo\.([A-Za-z][A-Za-z0-9]*)/g)].map(m => m[1]);
    const safe = new Set([
        'Campuses', 'AcademicYears', 'AcademicTerms', 'AcademicDepartments',
        'AcademicPrograms', 'ProgramCampuses', 'AcademicLevels', 'AcademicTracks',
        'Mediums', 'Shifts', 'Rooms', 'Subjects', 'AcademicCurriculums',
        'CurriculumSubjects', 'RoutineTimeSlots', 'AcademicBatches',
        'SubjectOfferings', 'Persons', 'Students', 'StudentPersonLinks',
        'Guardians', 'StudentGuardians', 'StudentEnrollments', 'StudentSubjectRegistrations'
    ]);
    assert.ok(insertedTables.length >= 20);
    for (const table of insertedTables) assert.ok(safe.has(table), `Unsafe demo INSERT: ${table}`);
    assert.doesNotMatch(sql, /\b(DELETE\s+FROM|TRUNCATE|DROP\s+TABLE|UPDATE\s+dbo\.)/i);
    const snapshot = read('EduOS.Persistence/Migrations/EduOSDbContextModelSnapshot.cs');
    for (const table of safe) assert.ok(snapshot.includes(`ToTable("${table}"`), `No EF-mapped table: ${table}`);
});

test('every SQL INSERT column matches the current EF Core model snapshot', () => {
    const snapshot = read('EduOS.Persistence/Migrations/EduOSDbContextModelSnapshot.cs');
    const entities = [...snapshot.matchAll(/modelBuilder\.Entity\("[^"]+", b =>/g)];
    const mapped = new Map();
    for (let i = 0; i < entities.length; i++) {
        const block = snapshot.slice(entities[i].index,
            i + 1 < entities.length ? entities[i + 1].index : snapshot.length);
        const tableName = block.match(/\bb\.ToTable\("([^"]+)"/);
        if (!tableName) continue;
        mapped.set(tableName[1], new Set([...block.matchAll(/\bb\.Property<[^\n]+?>\("([^"]+)"\)/g)]
            .map(match => match[1])));
    }
    const inserts = [...sql.matchAll(/INSERT\s+dbo\.([A-Za-z][A-Za-z0-9]*)\s*\(([^)]+)\)/g)];
    assert.ok(inserts.length >= 20);
    for (const [, name, columns] of inserts) {
        const properties = mapped.get(name);
        assert.ok(properties?.size, `No EF metadata for ${name}`);
        for (const column of columns.split(',').map(value => value.trim())) {
            assert.ok(properties.has(column), `Unknown ${name}.${column} column`);
        }
    }
});
