const $ = s => document.querySelector(s);
let lookups = {};
let selectedCall = null;

async function api(url, method = "GET", body) {
  const opt = { method, headers: {} };
  if (body !== undefined) { opt.headers["Content-Type"] = "application/json"; opt.body = JSON.stringify(body); }
  const res = await fetch("/api/" + url, opt);
  const txt = await res.text();
  let data;
  try { data = JSON.parse(txt); } catch { data = { error: `HTTP ${res.status}: ${txt.slice(0, 300) || res.statusText}` }; }
  if (!res.ok) throw data;
  return data;
}

function toast(text, err, small) {
  const t = document.createElement("div");
  t.className = "toast" + (err ? " err" : "");
  t.textContent = text;
  if (small) { const s = document.createElement("small"); s.textContent = small; t.appendChild(s); }
  $("#toast").appendChild(t);
  setTimeout(() => t.remove(), err ? 7000 : 3500);
}

function fail(e) {
  toast(e.code ? `Помилка MySQL ${e.code}` : "Помилка", true, e.error || String(e));
}

function esc(v) {
  return String(v).replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
}

const badgeCols = ["Пріоритет", "Статус", "priority", "status"];
const badgeVals = ["високий", "середній", "низький", "зареєстровано", "в дорозі", "виконано", "скасовано"];

function cell(col, v) {
  if (v === null || v === undefined) return '<td class="null">NULL</td>';
  if (badgeCols.includes(col) && badgeVals.includes(v)) return `<td><span class="badge b-${v.replace(/ /g, "-")}">${esc(v)}</span></td>`;
  return `<td>${esc(v)}</td>`;
}

function renderTable(el, t, opt = {}) {
  if (!t.rows.length) { el.innerHTML = '<div class="tablewrap"><div class="empty">Немає записів</div></div>'; return; }
  let h = '<div class="tablewrap"><table><thead><tr>' + (opt.actions ? "<th>Дії</th>" : "") + t.columns.map(c => `<th>${esc(c)}</th>`).join("");
  h += "</tr></thead><tbody>";
  t.rows.forEach((r, i) => {
    const cls = opt.onRow ? ' class="clickable' + (opt.selected === r[0] ? " selected" : "") + '"' : "";
    h += `<tr data-i="${i}"${cls}>` + (opt.actions ? `<td class="actions">${opt.actions(r)}</td>` : "") +
      r.map((v, j) => cell(t.columns[j], v)).join("") + "</tr>";
  });
  el.innerHTML = h + "</tbody></table></div>";
  if (opt.onRow) el.querySelectorAll("tbody tr").forEach(tr => tr.addEventListener("click", e => {
    if (e.target.closest("button,select,input")) return;
    opt.onRow(t.rows[+tr.dataset.i]);
  }));
}

function fill(sel, items, placeholder) {
  const el = typeof sel === "string" ? $(sel) : sel;
  const keep = el.value;
  el.innerHTML = (placeholder ? `<option value="">${placeholder}</option>` : "") +
    items.map(([id, name]) => `<option value="${id}">${esc(name)}</option>`).join("");
  if (keep && [...el.options].some(o => o.value === keep)) el.value = keep;
}

function formData(form) {
  const o = {};
  new FormData(form).forEach((v, k) => o[k] = v);
  return o;
}

function nowLocal() {
  const d = new Date();
  d.setMinutes(d.getMinutes() - d.getTimezoneOffset());
  return d.toISOString().slice(0, 16);
}

async function loadStatus() {
  const chip = $("#dbStatus");
  try {
    const s = await api("status");
    if (!s.ok) throw s;
    chip.className = "chip ok";
    chip.textContent = "MySQL підключено · " + s.counts.map(([n, c]) => `${n}: ${c}`).join(" · ");
    return true;
  } catch (e) {
    chip.className = "chip bad";
    chip.textContent = "Немає з'єднання з MySQL";
    toast("Немає з'єднання з базою даних", true, (e.error || "") + " Перевірте пароль у appsettings.json і чи запущено MySQL.");
    return false;
  }
}

async function loadLookups() {
  lookups = await api("lookups");
  fill("#selPatient", lookups.patients);
  fill("#selAddress", lookups.addresses);
  fill("#selDispatcher", lookups.dispatchers);
  fill("#selDiagnosis", lookups.diagnoses);
  fill("#selHospital", lookups.hospitals);
  fill("#selVehicle", lookups.ambulances);
  fill("#pStaff", lookups.staff);
  document.querySelectorAll(".memberStaff").forEach(s => fill(s, lookups.staff, "— не обрано —"));
}

async function loadActive() {
  const t = await api("calls?filter=active");
  const shiftOpts = lookups.shifts.map(([id, n]) => `<option value="${id}">${esc(n)}</option>`).join("");
  renderTable($("#activeCalls"), t, {
    actions: r => {
      const id = r[0];
      let a = "";
      if (r[6] === "зареєстровано")
        a += `<select id="sh${id}">${shiftOpts}</select> <button class="sm" onclick="assign(${id})">Призначити зміну</button> `;
      if (r[6] === "в дорозі")
        a += `<input type="datetime-local" id="arr${id}" value="${nowLocal()}"> <button class="sm ok" onclick="complete(${id})">Виконано</button> `;
      return a + `<button class="sm no" onclick="cancelCall(${id})">Скасувати</button>`;
    }
  });
}

async function loadAllCalls() {
  const t = await api("calls?filter=all");
  renderTable($("#allCalls"), t, { onRow: r => openCall(r[0], r[2]), selected: selectedCall });
}

async function openCall(id, patient) {
  selectedCall = id;
  $("#callDetails").hidden = false;
  $("#detTitle").textContent = `Виклик №${id} — ${patient || ""}`;
  const d = await api(`calls/${id}/details`);
  renderTable($("#detDiag"), d.diagnoses);
  renderTable($("#detHosp"), d.hospitalizations);
  fill("#selPrevHosp", d.hospitalizations.rows.map(r => [r[0], `№${r[0]} ${r[2]}`]), "— перша госпіталізація —");
  loadAllCalls();
}

async function loadPatients() {
  const t = await api("patients");
  renderTable($("#patientList"), t, { actions: r => `<button class="sm no" onclick="deletePatient(${r[0]})">Видалити</button>` });
}

async function loadShifts() {
  renderTable($("#shiftList"), await api("shifts"));
}

async function refreshAll() {
  if (!(await loadStatus())) return;
  try {
    await loadLookups();
    await Promise.all([loadActive(), loadAllCalls(), loadPatients(), loadShifts(), loadDir()]);
    if (selectedCall) await openCall(selectedCall, $("#detTitle").textContent.split("— ")[1]);
  } catch (e) { fail(e); }
}

function formMsg(form, ok, text, small) {
  if (!form) return;
  let m = form.querySelector(".msg");
  if (!m) { m = document.createElement("div"); form.appendChild(m); }
  m.className = "msg " + (ok ? "ok" : "bad");
  m.innerHTML = esc(text) + (small ? `<small>${esc(small)}</small>` : "");
}

async function run(promise, okText, form) {
  try {
    const r = await promise;
    const sql = r && r.sql ? r.sql.replace(/\s+/g, " ").trim() : undefined;
    toast(okText, false, sql);
    formMsg(form, true, okText, sql);
    await refreshAll();
    return true;
  } catch (e) {
    fail(e);
    formMsg(form, false, e.code ? `Помилка MySQL ${e.code}` : "Помилка", e.error || String(e));
    return false;
  }
}

document.addEventListener("invalid", e => {
  const label = e.target.closest("label");
  const name = label ? label.childNodes[0].textContent.trim() : (e.target.placeholder || e.target.name);
  formMsg(e.target.form, false, `Заповніть поле «${name}»`);
}, true);

window.assign = id => run(api(`calls/${id}/assign`, "POST", { shiftId: +$("#sh" + id).value }), `Виклик №${id}: призначено зміну, статус «в дорозі»`);
window.complete = id => run(api(`calls/${id}/complete`, "POST", { arrivalTime: $("#arr" + id).value }), `Виклик №${id} виконано`);
window.cancelCall = id => run(api(`calls/${id}/cancel`, "POST"), `Виклик №${id} скасовано`);
window.deletePatient = id => run(api(`patients/${id}`, "DELETE"), `Пацієнта №${id} видалено`);

$("#callForm").addEventListener("submit", e => {
  e.preventDefault();
  const f = formData(e.target);
  run(api("calls", "POST", { patientId: +f.patientId, addressId: +f.addressId, dispatcherId: +f.dispatcherId, symptoms: f.symptoms, priority: f.priority }),
      "Виклик зареєстровано", e.target).then(ok => ok && (e.target.symptoms.value = ""));
});

$("#selPatient").addEventListener("change", async () => {
  const p = await api("patients");
  const row = p.rows.find(r => r[0] === +$("#selPatient").value);
  if (!row) return;
  const addr = lookups.addresses.find(([, n]) => n === row[5]);
  if (addr) $("#selAddress").value = addr[0];
});

$("#diagForm").addEventListener("submit", e => {
  e.preventDefault();
  const f = formData(e.target);
  run(api(`calls/${selectedCall}/diagnoses`, "POST", { diagnosisId: +f.diagnosisId, type: f.type }), "Діагноз додано", e.target);
});

$("#hospForm").addEventListener("submit", e => {
  e.preventDefault();
  const f = formData(e.target);
  run(api(`calls/${selectedCall}/hospitalizations`, "POST", {
    hospitalId: +f.hospitalId, department: f.department, admissionStatus: f.admissionStatus,
    previousId: f.previousId ? +f.previousId : null
  }), "Госпіталізацію додано", e.target).then(ok => ok && (e.target.department.value = ""));
});

$("#patientForm").addEventListener("submit", e => {
  e.preventDefault();
  run(api("patients", "POST", formData(e.target)), "Пацієнта додано", e.target).then(ok => ok && e.target.reset());
});

const roles = ["старший бригади", "медична сестра", "водій"];
$("#members").innerHTML = roles.map(r => `<div class="row2"><label>Працівник<select class="memberStaff"></select></label><label>Роль<input class="memberRole" value="${r}"></label></div>`).join("");

$("#shiftForm").addEventListener("submit", e => {
  e.preventDefault();
  const f = formData(e.target);
  const staff = [...document.querySelectorAll(".memberStaff")];
  const role = [...document.querySelectorAll(".memberRole")];
  const members = staff.map((s, i) => ({ staffId: +s.value, role: role[i].value })).filter(m => m.staffId);
  run(api("shifts", "POST", { start: f.start, end: f.end || null, crewType: f.crewType, vehicleId: +f.vehicleId, members }), "Зміну створено", e.target);
});
$("#shiftForm").start.value = nowLocal().slice(0, 11) + "08:00";
$("#shiftForm").end.value = nowLocal().slice(0, 11) + "20:00";

const positions = ["лікар", "фельдшер", "парамедик", "медична сестра", "водій", "диспетчер"];
const dirCfg = {
  staff: { title: "Персонал", one: "працівника", fields: [["fullName", "ПІБ", 1], ["position", "Посада", 1, positions], ["qualification", "Кваліфікація"], ["phone", "Телефон"]] },
  ambulances: { title: "Автомобілі", one: "автомобіль", fields: [["plateNumber", "Номерний знак", 1], ["model", "Модель", 1], ["vehicleType", "Тип", 1, ["реанімобіль", "транспортний"]], ["year", "Рік випуску", 1, null, "number"]] },
  hospitals: { title: "Лікарні", one: "лікарню", fields: [["name", "Назва", 1], ["specialization", "Спеціалізація"], ["phone", "Телефон"], ["city", "Місто", 1], ["street", "Вулиця", 1], ["building", "Будинок", 1]] },
  diagnoses: { title: "Діагнози (МКХ)", one: "діагноз", fields: [["icdCode", "Код МКХ", 1], ["name", "Назва", 1]] },
  addresses: { title: "Адреси", one: "адресу", fields: [["city", "Місто", 1], ["street", "Вулиця", 1], ["building", "Будинок", 1]] }
};
let currentDir = "staff";

function showDir(name) {
  currentDir = name;
  const c = dirCfg[name];
  document.querySelectorAll("#dirButtons button").forEach(b => b.classList.toggle("active", b.dataset.dir === name));
  $("#dirFormTitle").textContent = "Додати " + c.one;
  $("#dirListTitle").textContent = c.title;
  $("#dirForm").innerHTML = c.fields.map(([k, label, req, opts, type]) => opts
    ? `<label>${label}<select name="${k}">${opts.map(o => `<option>${o}</option>`).join("")}</select></label>`
    : `<label>${label}<input name="${k}" type="${type || "text"}"${req ? " required" : ""}${name === "addresses" && k === "city" || name === "hospitals" && k === "city" ? ' value="Рівне"' : ""}></label>`
  ).join("") + '<button class="primary">Додати</button>';
  loadDir();
}

async function loadDir() {
  const t = await api("dir/" + currentDir);
  renderTable($("#dirList"), t, { actions: r => `<button class="sm no" onclick="deleteDir(${r[0]})">Видалити</button>` });
}

$("#dirButtons").innerHTML = Object.entries(dirCfg).map(([k, c]) => `<button data-dir="${k}" onclick="showDir('${k}')">${c.title}</button>`).join("");

$("#dirForm").addEventListener("submit", e => {
  e.preventDefault();
  const f = formData(e.target);
  if (f.year) f.year = +f.year;
  run(api("dir/" + currentDir, "POST", f), "Запис додано", e.target).then(ok => ok && showDirKeepMsg());
});

function showDirKeepMsg() {
  const m = $("#dirForm .msg");
  showDir(currentDir);
  if (m) $("#dirForm").appendChild(m);
}

window.deleteDir = id => run(api(`dir/${currentDir}/${id}`, "DELETE"), `Запис №${id} видалено`);
window.showDir = showDir;

const reportNames = [
  "Активні виклики з даними пацієнта та адреси",
  "Завантаженість змін (кількість викликів)",
  "Середній час реагування за пріоритетом",
  "Найпоширеніші діагнози",
  "Лікарні за кількістю госпіталізацій",
  "Ланцюжок переведень пацієнта між лікарнями",
  "Історія змін працівника за період"
];
$("#reportButtons").innerHTML = reportNames.map((n, i) => `<button onclick="runReport(${i + 1})"><b>Запит ${i + 1}.</b>${n}</button>`).join("");

window.runReport = async n => {
  try {
    const q = `reports/${n}?callId=${$("#pCall").value}&staffId=${$("#pStaff").value}&from=${$("#pFrom").value}&to=${$("#pTo").value}`;
    const r = await api(q);
    $("#reportOut").hidden = false;
    $("#reportTitle").textContent = `Запит ${n}. ${reportNames[n - 1]}`;
    $("#reportSql").textContent = r.sql;
    renderTable($("#reportTable"), r);
  } catch (e) { fail(e); }
};

const tableNames = ["addresses", "patients", "staff", "ambulances", "shifts", "shift_staff", "calls", "hospitals", "hospitalizations", "diagnoses", "call_diagnoses"];
$("#tableButtons").innerHTML = tableNames.map(t => `<button onclick="showTable('${t}', this)">${t.toUpperCase()}</button>`).join("");

window.showTable = async (name, btn) => {
  try {
    const r = await api("tables/" + name);
    document.querySelectorAll("#tableButtons button").forEach(b => b.classList.toggle("active", b === btn));
    $("#tableOut").hidden = false;
    $("#tableSql").textContent = r.sql;
    renderTable($("#tableData"), r);
  } catch (e) { fail(e); }
};

const tests = [
  ["fk-delete", "Посилальна цілісність: видалення", "Спроба видалити пацієнта, за яким зареєстровано виклик.",
   "DELETE FROM patients WHERE patient_id = 1;"],
  ["fk-insert", "Посилальна цілісність: вставлення", "Реєстрація виклику для пацієнта, якого не існує.",
   "INSERT INTO calls (call_time, symptoms, priority, patient_id, address_id, dispatcher_id)\nVALUES (NOW(), 'тест', 'високий', 999, 1, 5);"],
  ["check", "Бізнес-правило (CHECK)", "Час прибуття бригади раніший за час реєстрації виклику.",
   "UPDATE calls SET arrival_time = '2026-10-04 09:00:00'\nWHERE call_id = 1;"],
  ["enum", "Цілісність домену (ENUM)", "Пріоритет, якого немає в переліку допустимих значень.",
   "INSERT INTO calls (call_time, symptoms, priority, patient_id, address_id, dispatcher_id)\nVALUES (NOW(), 'тест', 'терміновий', 1, 1, 5);"],
  ["unique", "Унікальність (UNIQUE)", "Додавання автомобіля з номерним знаком, що вже існує.",
   "INSERT INTO ambulances (plate_number, model, vehicle_type, year)\nVALUES ('АА1234ВВ', 'Renault Master', 'транспортний', 2020);"],
  ["notnull", "Обов'язкове поле (NOT NULL)", "Додавання пацієнта без ПІБ.",
   "INSERT INTO patients (full_name, birth_date, address_id)\nVALUES (NULL, '1990-01-01', 1);"]
];
$("#integrityList").innerHTML = tests.map(([k, t, d, sql]) =>
  `<div class="test"><h3>${t}</h3><p>${d}</p><pre class="sql">${esc(sql)}</pre><button onclick="runTest('${k}')">Виконати запит</button><div id="res-${k}"></div></div>`).join("");

window.runTest = async k => {
  const r = await api("integrity/" + k, "POST");
  $("#res-" + k).innerHTML = r.ok
    ? `<div class="result okr"><b>Запит виконано</b>${esc(r.message)}</div>`
    : `<div class="result err"><b>Помилка MySQL ${r.code} — запит відхилено</b>${esc(r.error)}</div>`;
};

$("#resetBtn").addEventListener("click", async e => {
  const b = e.target;
  if (!b.classList.contains("armed")) {
    b.classList.add("armed");
    b.textContent = "Натисніть ще раз для підтвердження";
    setTimeout(() => { b.classList.remove("armed"); b.textContent = "Перестворити базу даних"; }, 4000);
    return;
  }
  b.classList.remove("armed");
  b.textContent = "Перестворити базу даних";
  selectedCall = null;
  $("#callDetails").hidden = true;
  run(api("reset", "POST"), "Базу даних перестворено з тестовими даними");
});

document.querySelectorAll("#tabs button").forEach(b => b.addEventListener("click", () => {
  document.querySelectorAll("#tabs button").forEach(x => x.classList.toggle("active", x === b));
  document.querySelectorAll(".tab").forEach(t => t.classList.toggle("active", t.id === b.dataset.tab));
}));

showDir("staff");
refreshAll();
