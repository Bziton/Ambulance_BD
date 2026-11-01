using System.Text.Encodings.Web;
using System.Text.Unicode;
using MySqlConnector;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All));
var app = builder.Build();

string cs = builder.Configuration.GetConnectionString("Db");
string sqlDir = Path.Combine(builder.Environment.ContentRootPath, "sql");
string dbError = null;

try { InitDb(false); }
catch (Exception e) { dbError = e.Message; }

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/status", () =>
{
    try
    {
        var t = Query(@"SELECT 'Виклики' AS name, COUNT(*) AS n FROM calls
                        UNION ALL SELECT 'Пацієнти', COUNT(*) FROM patients
                        UNION ALL SELECT 'Персонал', COUNT(*) FROM staff
                        UNION ALL SELECT 'Зміни', COUNT(*) FROM shifts
                        UNION ALL SELECT 'Госпіталізації', COUNT(*) FROM hospitalizations");
        dbError = null;
        return Results.Ok(new { ok = true, counts = t.rows });
    }
    catch (Exception e) { return Results.Ok(new { ok = false, error = dbError ?? e.Message }); }
});

app.MapPost("/api/reset", () => Safe(() => { InitDb(true); dbError = null; return new { ok = true }; }));

app.MapGet("/api/lookups", () => Safe(() => new
{
    patients = Query("SELECT patient_id AS id, full_name AS name FROM patients ORDER BY full_name").rows,
    addresses = Query("SELECT address_id AS id, CONCAT(city, ', ', street, ', ', building) AS name FROM addresses ORDER BY address_id").rows,
    dispatchers = Query("SELECT staff_id AS id, full_name AS name FROM staff WHERE position = 'диспетчер' ORDER BY full_name").rows,
    staff = Query("SELECT staff_id AS id, CONCAT(full_name, ' (', position, ')') AS name FROM staff WHERE position <> 'диспетчер' ORDER BY full_name").rows,
    shifts = Query(@"SELECT s.shift_id AS id, CONCAT('#', s.shift_id, ' ', DATE_FORMAT(s.shift_date, '%d.%m'), ' ', s.crew_type, ', ', a.plate_number, ' (', s.status, ')') AS name
                     FROM shifts s JOIN ambulances a ON s.vehicle_id = a.vehicle_id ORDER BY s.shift_date DESC, s.shift_id DESC").rows,
    ambulances = Query("SELECT vehicle_id AS id, CONCAT(plate_number, ' ', model) AS name FROM ambulances").rows,
    hospitals = Query("SELECT hospital_id AS id, name FROM hospitals ORDER BY name").rows,
    diagnoses = Query("SELECT diagnosis_id AS id, CONCAT(icd_code, ' ', name) AS name FROM diagnoses ORDER BY icd_code").rows
}));

app.MapGet("/api/calls", (string filter) => Safe(() =>
{
    bool active = filter == "active";
    string where = active ? "WHERE c.status IN ('зареєстровано', 'в дорозі')" : "";
    string arrival = active ? "" : "c.arrival_time AS `Прибуття`,";
    return Query($@"SELECT c.call_id AS `№`, c.call_time AS `Час виклику`, p.full_name AS `Пацієнт`,
                           CONCAT(a.street, ', ', a.building) AS `Адреса`, c.symptoms AS `Симптоми`,
                           c.priority AS `Пріоритет`, c.status AS `Статус`, {arrival}
                           d.full_name AS `Диспетчер`,
                           IF(s.shift_id IS NULL, NULL, CONCAT('#', s.shift_id, ' ', s.crew_type)) AS `Зміна`
                    FROM calls c
                    JOIN patients p ON c.patient_id = p.patient_id
                    JOIN addresses a ON c.address_id = a.address_id
                    JOIN staff d ON c.dispatcher_id = d.staff_id
                    LEFT JOIN shifts s ON c.shift_id = s.shift_id
                    {where}
                    ORDER BY c.priority, c.call_time");
}));

app.MapGet("/api/calls/{id:int}/details", (int id) => Safe(() => new
{
    diagnoses = Query(@"SELECT d.icd_code AS `Код МКХ`, d.name AS `Діагноз`, cd.diagnosis_type AS `Тип`
                        FROM call_diagnoses cd JOIN diagnoses d ON cd.diagnosis_id = d.diagnosis_id
                        WHERE cd.call_id = @id", P("@id", id)),
    hospitalizations = Query(@"SELECT hz.hospitalization_id AS `№`, hz.delivery_time AS `Час доставки`, h.name AS `Лікарня`,
                                      hz.department AS `Відділення`, hz.admission_status AS `Статус`,
                                      hz.previous_hospitalization_id AS `Переведено з №`
                               FROM hospitalizations hz JOIN hospitals h ON hz.hospital_id = h.hospital_id
                               WHERE hz.call_id = @id ORDER BY hz.delivery_time", P("@id", id))
}));

app.MapPost("/api/calls", (NewCall c) => Safe(() =>
{
    string sql = @"INSERT INTO calls (call_time, symptoms, priority, status, patient_id, address_id, dispatcher_id)
                   VALUES (NOW(), @symptoms, @priority, 'зареєстровано', @patientId, @addressId, @dispatcherId)";
    Exec(sql, P("@symptoms", c.Symptoms, "@priority", c.Priority, "@patientId", c.PatientId,
                "@addressId", c.AddressId, "@dispatcherId", c.DispatcherId));
    return new { ok = true, sql };
}));

app.MapPost("/api/calls/{id:int}/assign", (int id, AssignShift a) => Safe(() =>
{
    string sql = "UPDATE calls SET shift_id = @shiftId, status = 'в дорозі' WHERE call_id = @id";
    InTransaction((conn, tr) =>
    {
        Exec(sql, P("@shiftId", a.ShiftId, "@id", id), conn, tr);
        Exec("UPDATE shifts SET status = 'на виклику' WHERE shift_id = @shiftId", P("@shiftId", a.ShiftId), conn, tr);
    });
    return new { ok = true, sql };
}));

app.MapPost("/api/calls/{id:int}/complete", (int id, Complete c) => Safe(() =>
{
    string sql = "UPDATE calls SET status = 'виконано', arrival_time = @arrival WHERE call_id = @id";
    InTransaction((conn, tr) =>
    {
        Exec(sql, P("@arrival", c.ArrivalTime ?? DateTime.Now, "@id", id), conn, tr);
        Exec(@"UPDATE shifts SET status = 'на базі'
               WHERE shift_id = (SELECT shift_id FROM calls WHERE call_id = @id)", P("@id", id), conn, tr);
    });
    return new { ok = true, sql };
}));

app.MapPost("/api/calls/{id:int}/cancel", (int id) => Safe(() =>
{
    string sql = "UPDATE calls SET status = 'скасовано' WHERE call_id = @id";
    Exec(sql, P("@id", id));
    return new { ok = true, sql };
}));

app.MapPost("/api/calls/{id:int}/diagnoses", (int id, NewDiagnosis d) => Safe(() =>
{
    string sql = "INSERT INTO call_diagnoses (call_id, diagnosis_id, diagnosis_type) VALUES (@id, @diagnosisId, @type)";
    Exec(sql, P("@id", id, "@diagnosisId", d.DiagnosisId, "@type", d.Type));
    return new { ok = true, sql };
}));

app.MapPost("/api/calls/{id:int}/hospitalizations", (int id, NewHospitalization h) => Safe(() =>
{
    string sql = @"INSERT INTO hospitalizations (delivery_time, department, admission_status, call_id, hospital_id, previous_hospitalization_id)
                   VALUES (NOW(), @department, @status, @id, @hospitalId, @previousId)";
    Exec(sql, P("@department", h.Department, "@status", h.AdmissionStatus, "@id", id,
                "@hospitalId", h.HospitalId, "@previousId", h.PreviousId));
    return new { ok = true, sql };
}));

app.MapGet("/api/patients", () => Safe(() => Query(
    @"SELECT p.patient_id AS `№`, p.full_name AS `ПІБ`, p.birth_date AS `Дата народження`, p.phone AS `Телефон`,
             p.blood_type AS `Група крові`, CONCAT(a.city, ', ', a.street, ', ', a.building) AS `Адреса`,
             COUNT(c.call_id) AS `Викликів`
      FROM patients p
      JOIN addresses a ON p.address_id = a.address_id
      LEFT JOIN calls c ON c.patient_id = p.patient_id
      GROUP BY p.patient_id, p.full_name, p.birth_date, p.phone, p.blood_type, a.city, a.street, a.building
      ORDER BY p.patient_id")));

app.MapPost("/api/patients", (NewPatient p) => Safe(() =>
{
    InTransaction((conn, tr) =>
    {
        Exec("INSERT INTO addresses (city, street, building) VALUES (@city, @street, @building)",
             P("@city", p.City, "@street", p.Street, "@building", p.Building), conn, tr);
        Exec(@"INSERT INTO patients (full_name, birth_date, phone, blood_type, address_id)
               VALUES (@name, @birth, @phone, @blood, LAST_INSERT_ID())",
             P("@name", p.FullName, "@birth", p.BirthDate, "@phone", Empty(p.Phone), "@blood", p.BloodType), conn, tr);
    });
    return new { ok = true };
}));

app.MapDelete("/api/patients/{id:int}", (int id) => Safe(() =>
{
    string sql = $"DELETE FROM patients WHERE patient_id = {id};";
    Exec("DELETE FROM patients WHERE patient_id = @id", P("@id", id));
    return new { ok = true, sql };
}, $"DELETE FROM patients WHERE patient_id = {id};"));

app.MapGet("/api/shifts", () => Safe(() => Query(
    @"SELECT s.shift_id AS `№`, s.shift_date AS `Дата`, CONCAT(DATE_FORMAT(s.start_time, '%H:%i'), '–', IFNULL(DATE_FORMAT(s.end_time, '%H:%i'), '...')) AS `Час`,
             s.crew_type AS `Тип бригади`, s.status AS `Статус`, CONCAT(a.plate_number, ' ', a.model) AS `Автомобіль`,
             GROUP_CONCAT(CONCAT(st.full_name, ' — ', ss.role_in_shift) ORDER BY ss.role_in_shift SEPARATOR '; ') AS `Склад бригади`
      FROM shifts s
      JOIN ambulances a ON s.vehicle_id = a.vehicle_id
      LEFT JOIN shift_staff ss ON ss.shift_id = s.shift_id
      LEFT JOIN staff st ON ss.staff_id = st.staff_id
      GROUP BY s.shift_id, s.shift_date, s.start_time, s.end_time, s.crew_type, s.status, a.plate_number, a.model
      ORDER BY s.shift_date DESC, s.shift_id DESC")));

app.MapPost("/api/shifts", (NewShift s) => Safe(() =>
{
    InTransaction((conn, tr) =>
    {
        Exec(@"INSERT INTO shifts (shift_date, start_time, end_time, crew_type, status, vehicle_id)
               VALUES (DATE(@start), @start, @end, @crewType, 'на базі', @vehicleId)",
             P("@start", s.Start, "@end", s.End, "@crewType", s.CrewType, "@vehicleId", s.VehicleId), conn, tr);
        long shiftId = Convert.ToInt64(new MySqlCommand("SELECT LAST_INSERT_ID()", conn, tr).ExecuteScalar());
        foreach (var m in s.Members)
            Exec("INSERT INTO shift_staff (shift_id, staff_id, role_in_shift) VALUES (@shiftId, @staffId, @role)",
                 P("@shiftId", shiftId, "@staffId", m.StaffId, "@role", m.Role), conn, tr);
    });
    return new { ok = true };
}));

var dirs = new Dictionary<string, (string table, string key, string list)>
{
    ["staff"] = ("staff", "staff_id", @"SELECT staff_id AS `№`, full_name AS `ПІБ`, position AS `Посада`, qualification AS `Кваліфікація`, phone AS `Телефон`
                                         FROM staff ORDER BY staff_id"),
    ["ambulances"] = ("ambulances", "vehicle_id", @"SELECT vehicle_id AS `№`, plate_number AS `Номер`, model AS `Модель`, vehicle_type AS `Тип`, year AS `Рік`
                                                    FROM ambulances ORDER BY vehicle_id"),
    ["hospitals"] = ("hospitals", "hospital_id", @"SELECT h.hospital_id AS `№`, h.name AS `Назва`, h.specialization AS `Спеціалізація`, h.phone AS `Телефон`,
                                                          CONCAT(a.city, ', ', a.street, ', ', a.building) AS `Адреса`
                                                   FROM hospitals h JOIN addresses a ON h.address_id = a.address_id ORDER BY h.hospital_id"),
    ["diagnoses"] = ("diagnoses", "diagnosis_id", @"SELECT diagnosis_id AS `№`, icd_code AS `Код МКХ`, name AS `Назва` FROM diagnoses ORDER BY icd_code"),
    ["addresses"] = ("addresses", "address_id", @"SELECT address_id AS `№`, city AS `Місто`, street AS `Вулиця`, building AS `Будинок` FROM addresses ORDER BY address_id")
};

app.MapGet("/api/dir/{name}", (string name) => Safe(() => Query(dirs[name].list)));

app.MapDelete("/api/dir/{name}/{id:int}", (string name, int id) =>
{
    var d = dirs[name];
    string sql = $"DELETE FROM {d.table} WHERE {d.key} = {id};";
    return Safe(() => { Exec($"DELETE FROM {d.table} WHERE {d.key} = @id", P("@id", id)); return new { ok = true, sql }; }, sql);
});

app.MapPost("/api/dir/staff", (NewStaff s) => Safe(() =>
{
    string sql = "INSERT INTO staff (full_name, position, qualification, phone) VALUES (@name, @position, @qualification, @phone)";
    Exec(sql, P("@name", s.FullName, "@position", s.Position, "@qualification", Empty(s.Qualification), "@phone", Empty(s.Phone)));
    return new { ok = true, sql };
}));

app.MapPost("/api/dir/ambulances", (NewAmbulance a) => Safe(() =>
{
    string sql = "INSERT INTO ambulances (plate_number, model, vehicle_type, year) VALUES (@plate, @model, @type, @year)";
    Exec(sql, P("@plate", a.PlateNumber, "@model", a.Model, "@type", a.VehicleType, "@year", a.Year));
    return new { ok = true, sql };
}));

app.MapPost("/api/dir/hospitals", (NewHospital h) => Safe(() =>
{
    InTransaction((conn, tr) =>
    {
        Exec("INSERT INTO addresses (city, street, building) VALUES (@city, @street, @building)",
             P("@city", h.City, "@street", h.Street, "@building", h.Building), conn, tr);
        Exec("INSERT INTO hospitals (name, specialization, phone, address_id) VALUES (@name, @spec, @phone, LAST_INSERT_ID())",
             P("@name", h.Name, "@spec", Empty(h.Specialization), "@phone", Empty(h.Phone)), conn, tr);
    });
    return new { ok = true };
}));

app.MapPost("/api/dir/diagnoses", (NewDiagnosisDir d) => Safe(() =>
{
    string sql = "INSERT INTO diagnoses (icd_code, name) VALUES (@code, @name)";
    Exec(sql, P("@code", d.IcdCode, "@name", d.Name));
    return new { ok = true, sql };
}));

app.MapPost("/api/dir/addresses", (NewAddress a) => Safe(() =>
{
    string sql = "INSERT INTO addresses (city, street, building) VALUES (@city, @street, @building)";
    Exec(sql, P("@city", a.City, "@street", a.Street, "@building", a.Building));
    return new { ok = true, sql };
}));

var reports = new Dictionary<int, string>
{
    [1] = @"SELECT c.call_id, p.full_name, a.city, a.street, a.building,
       c.priority, c.status, c.call_time
FROM calls c
JOIN patients p ON c.patient_id = p.patient_id
JOIN addresses a ON c.address_id = a.address_id
WHERE c.status IN ('зареєстровано', 'в дорозі')
ORDER BY c.priority, c.call_time;",
    [2] = @"SELECT s.shift_id, s.shift_date, s.crew_type, COUNT(c.call_id) AS calls_count
FROM shifts s
LEFT JOIN calls c ON c.shift_id = s.shift_id
GROUP BY s.shift_id, s.shift_date, s.crew_type
ORDER BY calls_count DESC;",
    [3] = @"SELECT priority,
       AVG(TIMESTAMPDIFF(MINUTE, call_time, arrival_time)) AS avg_response_min
FROM calls
WHERE arrival_time IS NOT NULL
GROUP BY priority
ORDER BY avg_response_min;",
    [4] = @"SELECT d.name, COUNT(*) AS cases_count
FROM call_diagnoses cd
JOIN diagnoses d ON cd.diagnosis_id = d.diagnosis_id
GROUP BY d.name
ORDER BY cases_count DESC
LIMIT 10;",
    [5] = @"SELECT h.name, COUNT(hz.hospitalization_id) AS admissions_count
FROM hospitals h
LEFT JOIN hospitalizations hz ON hz.hospital_id = h.hospital_id
GROUP BY h.name
ORDER BY admissions_count DESC;",
    [6] = @"SELECT h1.hospitalization_id, hos1.name AS from_hospital,
       h1.previous_hospitalization_id, hos2.name AS to_hospital
FROM hospitalizations h1
LEFT JOIN hospitalizations h2 ON h2.previous_hospitalization_id = h1.hospitalization_id
JOIN hospitals hos1 ON h1.hospital_id = hos1.hospital_id
LEFT JOIN hospitals hos2 ON h2.hospital_id = hos2.hospital_id
WHERE h1.call_id = @callId;",
    [7] = @"SELECT s.shift_date, s.start_time, s.end_time, ss.role_in_shift
FROM shift_staff ss
JOIN shifts s ON ss.shift_id = s.shift_id
WHERE ss.staff_id = @staffId
  AND s.shift_date BETWEEN @from AND @to
ORDER BY s.shift_date;"
};

app.MapGet("/api/reports/{n:int}", (int n, int? callId, int? staffId, string from, string to) => Safe(() =>
{
    string sql = reports[n];
    var r = Query(sql, P("@callId", callId ?? 2, "@staffId", staffId ?? 1, "@from", from ?? "2026-10-01", "@to", to ?? "2026-10-31"));
    string shown = sql.Replace("@callId", (callId ?? 2).ToString()).Replace("@staffId", (staffId ?? 1).ToString())
                      .Replace("@from", $"'{from ?? "2026-10-01"}'").Replace("@to", $"'{to ?? "2026-10-31"}'");
    return new { r.columns, r.rows, sql = shown };
}));

var tables = new[] { "addresses", "patients", "staff", "ambulances", "shifts", "shift_staff", "calls", "hospitals", "hospitalizations", "diagnoses", "call_diagnoses" };

app.MapGet("/api/tables/{name}", (string name) => Safe(() =>
{
    if (!tables.Contains(name)) throw new ArgumentException("Невідома таблиця");
    string sql = $"SELECT * FROM {name};";
    var r = Query(sql);
    return new { r.columns, r.rows, sql };
}));

var integrity = new Dictionary<string, string>
{
    ["fk-delete"] = "DELETE FROM patients WHERE patient_id = 1;",
    ["fk-insert"] = "INSERT INTO calls (call_time, symptoms, priority, patient_id, address_id, dispatcher_id)\nVALUES (NOW(), 'тест', 'високий', 999, 1, 5);",
    ["check"] = "UPDATE calls SET arrival_time = '2026-10-04 09:00:00'\nWHERE call_id = 1;",
    ["enum"] = "INSERT INTO calls (call_time, symptoms, priority, patient_id, address_id, dispatcher_id)\nVALUES (NOW(), 'тест', 'терміновий', 1, 1, 5);",
    ["unique"] = "INSERT INTO ambulances (plate_number, model, vehicle_type, year)\nVALUES ('АА1234ВВ', 'Renault Master', 'транспортний', 2020);",
    ["notnull"] = "INSERT INTO patients (full_name, birth_date, address_id)\nVALUES (NULL, '1990-01-01', 1);"
};

app.MapPost("/api/integrity/{test}", (string test) =>
{
    string sql = integrity[test];
    try
    {
        using var conn = Open();
        using var tr = conn.BeginTransaction();
        int n = new MySqlCommand(sql, conn, tr).ExecuteNonQuery();
        tr.Rollback();
        return Results.Ok(new { ok = true, sql, message = $"Запит виконано без помилки (змінено рядків: {n}), зміни відкочено." });
    }
    catch (MySqlException e) { return Results.Ok(new { ok = false, sql, code = e.Number, error = e.Message }); }
});

app.Run();

MySqlConnection Open(string connStr = null)
{
    var conn = new MySqlConnection(connStr ?? cs);
    conn.Open();
    return conn;
}

void InitDb(bool force)
{
    var csb = new MySqlConnectionStringBuilder(cs) { Database = "" };
    using var conn = Open(csb.ConnectionString);
    var exists = new MySqlCommand("SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name = 'ambulance_service'", conn).ExecuteScalar();
    if (!force && Convert.ToInt32(exists) > 0) return;
    foreach (var f in new[] { "01_schema.sql", "02_data.sql" })
        new MySqlCommand(File.ReadAllText(Path.Combine(sqlDir, f)), conn).ExecuteNonQuery();
}

Dictionary<string, object> P(params object[] kv)
{
    var d = new Dictionary<string, object>();
    for (int i = 0; i < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1] ?? DBNull.Value;
    return d;
}

int Exec(string sql, Dictionary<string, object> ps, MySqlConnection conn = null, MySqlTransaction tr = null)
{
    var own = conn == null ? Open() : null;
    try
    {
        var cmd = new MySqlCommand(sql, conn ?? own, tr);
        foreach (var p in ps) cmd.Parameters.AddWithValue(p.Key, p.Value);
        return cmd.ExecuteNonQuery();
    }
    finally { own?.Dispose(); }
}

void InTransaction(Action<MySqlConnection, MySqlTransaction> work)
{
    using var conn = Open();
    using var tr = conn.BeginTransaction();
    work(conn, tr);
    tr.Commit();
}

Table Query(string sql, Dictionary<string, object> ps = null)
{
    using var conn = Open();
    var cmd = new MySqlCommand(sql, conn);
    if (ps != null) foreach (var p in ps) cmd.Parameters.AddWithValue(p.Key, p.Value);
    using var r = cmd.ExecuteReader();
    var cols = Enumerable.Range(0, r.FieldCount).Select(r.GetName).ToList();
    var rows = new List<object[]>();
    while (r.Read())
    {
        var row = new object[r.FieldCount];
        for (int i = 0; i < r.FieldCount; i++)
        {
            object v = r.IsDBNull(i) ? null : r.GetValue(i);
            if (v is DateTime dt) v = r.GetDataTypeName(i) == "DATE" ? dt.ToString("yyyy-MM-dd") : dt.ToString("yyyy-MM-dd HH:mm");
            else if (v is decimal m) v = Math.Round(m, 1);
            row[i] = v;
        }
        rows.Add(row);
    }
    return new Table(cols, rows);
}

IResult Safe(Func<object> f, string sql = null)
{
    try { return Results.Ok(f()); }
    catch (MySqlException e) { return Results.BadRequest(new { error = e.Message, code = e.Number, sql }); }
    catch (Exception e) { return Results.BadRequest(new { error = e.Message, sql }); }
}

object Empty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

record Table(List<string> columns, List<object[]> rows);
record NewCall(int PatientId, int AddressId, int DispatcherId, string Symptoms, string Priority);
record AssignShift(int ShiftId);
record Complete(DateTime? ArrivalTime);
record NewDiagnosis(int DiagnosisId, string Type);
record NewHospitalization(int HospitalId, string Department, string AdmissionStatus, int? PreviousId);
record NewPatient(string FullName, DateTime BirthDate, string Phone, string BloodType, string City, string Street, string Building);
record Member(int StaffId, string Role);
record NewStaff(string FullName, string Position, string Qualification, string Phone);
record NewAmbulance(string PlateNumber, string Model, string VehicleType, int Year);
record NewHospital(string Name, string Specialization, string Phone, string City, string Street, string Building);
record NewDiagnosisDir(string IcdCode, string Name);
record NewAddress(string City, string Street, string Building);
record NewShift(DateTime Start, DateTime? End, string CrewType, int VehicleId, List<Member> Members);
