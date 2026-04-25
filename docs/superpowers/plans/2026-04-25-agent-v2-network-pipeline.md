# Agent v2.0 — Service Mode + Full Network Pipeline

**Fecha:** 2026-04-25
**Objetivo:** Convertir agent a Windows Service (full) + trial mode (one-shot), implementar pipeline completo de network discovery.
**Reglas de negocio:** Ver `memory/project_network_discovery_rules.md`

---

## Orden de ejecución

Cada tarea es un bloque para un subagente. No se superponen.
🔍 = checkpoint de review antes de continuar.

---

### BLOQUE 1: Agent como Windows Service
**Archivos:** `Program.cs`, nuevo `ServiceWorker.cs`, `AgentConfig.cs`
**Dependencias:** Ninguna — es la base para todo lo demás

1. **T1.1** — Crear `ServiceWorker.cs` (BackgroundService de .NET)
   - Worker loop: idle → check schedule → run scan cycle → sleep
   - Intervals configurables desde registry: `ScanIntervalMinutes` (default 240 = 4h para SNMP), `ComplianceIntervalHours` (default 24)
   - Graceful shutdown en `StopAsync`

2. **T1.2** — Modificar `Program.cs` para dual-mode
   - `--install`: instalar Windows Service (`sc.exe create` o `ServiceInstaller`)
   - `--uninstall`: parar + eliminar servicio
   - `--service`: ejecutar como service (BackgroundService host)
   - Sin flags: one-shot mode actual (trial/legacy)
   - `--trial`: one-shot + auto-genera reporte Preventa Detailed + limpia todo

3. **T1.3** — Health heartbeat
   - `POST /v1/heartbeat` cada 15 min (nuevo endpoint)
   - Body: `{ agentId, version, uptime, lastScanAt, mode: "service"|"trial" }`
   - API: actualiza `machines.last_heartbeat_at` + `agent_mode`
   - SQL migration: `ALTER TABLE machines ADD last_heartbeat_at datetime2, agent_mode varchar(10)`

4. **T1.4** — Actualizar deploy scripts (NinjaOne/GPO/Intune)
   - `Scripts/Deploy/Install-KryossService.ps1`: download + install service + start
   - `Scripts/Deploy/Uninstall-KryossService.ps1`: stop + uninstall + cleanup
   - Mantener backward-compat con script actual para transición

🔍 **REVIEW 1:** Service arranca, corre scans en loop, heartbeat funciona, one-shot sigue funcionando.

---

### BLOQUE 2: Trial Mode + Auto-Report
**Archivos:** `Program.cs`, `ApiClient.cs`, `EnrollFunction.cs`, `EnrollmentService.cs`
**Dependencias:** T1.2 (dual-mode Program.cs)

5. **T2.1** — Enrollment codes con flag trial
   - SQL: `ALTER TABLE enrollment_codes ADD is_trial bit DEFAULT 0, trial_expires_at datetime2`
   - `EnrollmentService`: si code es trial → `trial_expires_at = NOW + 30 days`
   - `machines` table: `is_trial bit, trial_expires_at datetime2`
   - API rechaza scans de trials expirados (401 + mensaje claro)

6. **T2.2** — Agent trial flow
   - `--trial` flag: scan completo → upload → pedir reporte → descargar HTML → abrir en browser → limpiar registry
   - Nuevo endpoint `GET /v2/reports/org/{orgId}?type=preventas&tone=detailed` ya existe
   - Agent llama al endpoint después del upload, guarda HTML en Desktop del usuario
   - Zero residuo: wipe `HKLM\SOFTWARE\Kryoss\Agent` al finalizar

7. **T2.3** — Portal: badge trial en machines list
   - Machines con `is_trial=true` muestran badge "Trial" + días restantes
   - Filter por trial/full en machines list

🔍 **REVIEW 2:** Trial funciona end-to-end: enroll → scan → report en desktop → cleanup.

---

### BLOQUE 3: Banner Grab en Port Scanner
**Archivos:** `PortScanner.cs`, `MachinePort.cs` entity, migration SQL
**Dependencias:** Ninguna (independiente)

8. **T3.1** — Agregar banner grab a `PortScanner.cs`
   - Después de detectar puerto abierto TCP: conectar, leer primeros 512 bytes (timeout 3s)
   - Probes específicos: HTTP (`HEAD / HTTP/1.0\r\n\r\n`), SMTP/FTP/SSH (leer banner directo)
   - Parsear: service name, version, server header
   - Modelo: `PortResult` gana campos `banner`, `service`, `serviceVersion`

9. **T3.2** — Persistir banner en API
   - SQL: `ALTER TABLE machine_ports ADD banner varchar(512), service_name varchar(100), service_version varchar(100)`
   - `ResultsFunction` / port upload: guardar campos nuevos
   - Portal: mostrar service+version en ports tab

🔍 **REVIEW 3:** Port scan muestra "Apache 2.4.49" en vez de solo "443 open".

---

### BLOQUE 4: Reverse DNS + Large-Packet Ping
**Archivos:** `NetworkScanner.cs` o nuevo `NetworkProbe.cs`, `SnmpFunction.cs`
**Dependencias:** Ninguna (independiente)

10. **T4.1** — Reverse DNS
    - `Dns.GetHostEntryAsync(ip)` para cada device descubierto
    - Timeout 2s por lookup (no bloquear scan)
    - Resultado → `snmp_devices.reverse_dns` (nuevo campo)
    - Si no tiene sysName, usar reverse DNS como label
    - SQL: `ALTER TABLE snmp_devices ADD reverse_dns varchar(255)`

11. **T4.2** — Large-packet ping (latencia + packet loss)
    - `Ping.SendAsync(ip, timeout: 2000, buffer: 1472 bytes)` (MTU test)
    - 5 pings, calcular: avg latency, min, max, jitter, packet loss %
    - Resultado → campos en `snmp_devices`: `ping_latency_ms`, `ping_loss_pct`, `ping_jitter_ms`
    - SQL: `ALTER TABLE snmp_devices ADD ping_latency_ms float, ping_loss_pct float, ping_jitter_ms float`

🔍 **REVIEW 4:** Devices muestran hostname resuelto + latencia + packet loss en portal.

---

### BLOQUE 5: WMI Probe para Unenrolled Windows
**Archivos:** Nuevo `Services/WmiProbe.cs`, `SnmpFunction.cs` o `NetworkScanner.cs`
**Dependencias:** Ninguna (independiente)

12. **T5.1** — `WmiProbe.cs`
    - Solo para IPs que: (a) no tienen machineId, (b) SNMP sysDescr contiene "Windows" o no tiene SNMP
    - Conectar via `ManagementScope(@"\\{ip}\root\cimv2")` con credenciales del dominio
    - Recolectar: OS version, hostname, CPU, RAM, disk, services, shares, logged-on users, installed software
    - Timeout 10s por máquina, parallel con throttle

13. **T5.2** — Persistir WMI data
    - Mapear a campos existentes de `snmp_devices` (CPU, memory, disk) o tabla separada `wmi_devices`
    - Decisión: ¿misma tabla `snmp_devices` renombrada a `network_devices`? ¿O tabla aparte?
    - Marcar `scan_source = "wmi"` para diferenciar

🔍 **REVIEW 5:** Windows machines sin agente muestran OS, CPU, RAM, disco, servicios.

---

### BLOQUE 6: Passive Discovery (broadcasts)
**Archivos:** Nuevo `Services/PassiveListener.cs`
**Dependencias:** T1.1 (requiere service mode para correr continuo)

14. **T6.1** — `PassiveListener.cs`
    - UDP listener en puertos: 137 (NetBIOS), 5353 (mDNS), 1900 (SSDP/UPnP)
    - Parsear: hostname, device type, manufacturer (de mDNS TXT records, SSDP headers)
    - Mantener in-memory set de IPs descubiertas, merge con próximo scan activo
    - Solo en service mode (no en trial)

15. **T6.2** — Integrar con scan pipeline
    - Passive discoveries se agregan al pool de targets del próximo scan cycle
    - Marcar `scan_source = "passive"` en devices descubiertos así

🔍 **REVIEW 6:** Dispositivos IoT/cámaras aparecen sin necesidad de ping/SNMP.

---

### BLOQUE 7: Self-Updater
**Archivos:** Nuevo `Services/SelfUpdater.cs`, `Program.cs`
**Dependencias:** T1.1 (requiere service mode)

16. **T7.1** — `SelfUpdater.cs`
    - Cada 6h: `GET /v2/agent/latest-version` → comparar con assembly version
    - Si hay update: download a temp, verificar hash, parar service, reemplazar exe, reiniciar service
    - Rollback: si nuevo exe no arranca en 60s, restaurar backup
    - Log update history en `C:\ProgramData\Kryoss\update-log.txt`

17. **T7.2** — API endpoint version check
    - `GET /v2/agent/latest-version` → `{ version, hash, url, releaseNotes }`
    - Blob storage: `stkryossagent/kryoss-agent-templates/latest/`

🔍 **REVIEW 7:** Agent se actualiza solo sin intervención.

---

### BLOQUE 8: External Exposure (API-side)
**Archivos:** Nuevo `Services/ExternalScanner.cs` (API), `ExternalExposureFunction.cs`, migration SQL
**Dependencias:** Consent model (T8.1)

18. **T8.1** — Consent model
    - SQL: `ALTER TABLE organizations ADD external_scan_consent bit DEFAULT 0, consent_granted_at datetime2, consent_granted_by uniqueidentifier`
    - Portal: botón "Enable External Scan" con disclaimer legal + confirmación
    - API: `PATCH /v2/organizations/{id}/external-scan` toggle

19. **T8.2** — External port scanner (API-side)
    - `ExternalScanner.cs`: desde el servidor, TCP connect a la IP pública del org
    - Top 100 ports + banner grab
    - Triggered: después de cada scan del agent que reporta public IP
    - Guardar en nueva tabla `external_scan_results` (org_id, public_ip, port, service, banner, scanned_at)

20. **T8.3** — Basic pentest findings
    - Reglas sobre resultados del external scan:
      - RDP (3389) expuesto → Critical
      - SMB (445) expuesto → Critical
      - Telnet (23) expuesto → Critical
      - FTP (21) expuesto → High
      - HTTP sin redirect a HTTPS → Medium
      - SSH con banner viejo → Medium
      - Cualquier puerto >1024 con servicio desconocido → Low
    - Guardar como `external_scan_findings` (severity, title, description, remediation)

21. **T8.4** — Portal: External Exposure tab
    - Tabla de puertos expuestos + findings con severity badges
    - Timeline de scans
    - Consent status banner

🔍 **REVIEW 8:** External scan muestra puertos expuestos + findings de seguridad.

---

### BLOQUE 9: Closed-Set Remediation (último — requiere todo lo anterior estable)
**Archivos:** Nuevo `Services/RemediationExecutor.cs` (agent), nuevo `RemediationFunction.cs` (API), migration SQL
**Dependencias:** T1.1 (service mode) + T1.3 (heartbeat como canal de pull)

22. **T9.1** — Remediation catalog (API-side)
    - Tabla `remediation_actions`: catálogo cerrado de acciones posibles
    - Cada acción mapea a un control_def: `control_id → action_type + params_template`
    - Action types (whitelist): `set_registry`, `enable_service`, `disable_service`, `set_audit_policy`, `enable_firewall_rule`, `set_account_policy`
    - Seed con las remediaciones más comunes (~50 controles que se pueden auto-fix)

23. **T9.2** — Remediation tasks (API-side)
    - Tabla `remediation_tasks`: id, org_id, machine_id, control_id, action_type, params (JSON), status (pending/running/completed/failed/rolled_back), approved_by, approved_at, executed_at, result, previous_value (JSON)
    - `RemediationFunction.cs`: 
      - `POST /v2/remediation/tasks` — crear task (requiere admin:write, validar que action_type esté en catálogo)
      - `GET /v2/remediation/tasks?machineId=X` — listar tasks por máquina
      - `POST /v2/remediation/tasks/{id}/rollback` — crear reverse task
      - `GET /v2/remediation/history?orgId=X` — audit trail
    - Heartbeat response incluye `pendingTasks[]` para la máquina

24. **T9.3** — `RemediationExecutor.cs` (agent-side)
    - Whitelist hardcodeado de action types (mismo set que T9.1, compilado en el agent)
    - Agent NUNCA ejecuta algo fuera del whitelist — ignora y reporta error
    - Para cada task:
      1. Leer valor actual → guardar como `previousValue`
      2. Ejecutar acción (registry write, service config, etc.)
      3. Verificar que el cambio aplicó (re-read)
      4. Reportar resultado: `POST /v1/task-result { taskId, status, previousValue, newValue }`
    - Rollback: recibe reverse task, restaura `previousValue`
    - Log local: `C:\ProgramData\Kryoss\Remediation\{taskId}.json`

25. **T9.4** — Portal: Remediation UI
    - En machine detail, cada control FAIL muestra botón "Remediate" (si existe en catálogo)
    - Confirmación: "This will change {setting} from {current} to {expected}. Approve?"
    - Task status tracker: pending → running → completed/failed
    - Botón "Undo" en tasks completados
    - History tab con audit trail (quién aprobó, cuándo, qué cambió)
    - Bulk remediate: seleccionar múltiples controles → aprobar batch

26. **T9.5** — Auto-remediate (opcional, per-control)
    - Organizations config: `auto_remediate_controls` (lista de control IDs que se auto-fix sin aprobación manual)
    - Solo controles de bajo riesgo (ej: audit policy, password length) — nunca servicios o firewall
    - Portal: toggle per-control en catalog view
    - Tabla `org_auto_remediate`: org_id, control_id, enabled_by, enabled_at

**Seguridad — lo que el agent NUNCA hace:**
- Ejecutar scripts/comandos arbitrarios del server
- Descargar y correr binarios
- Modificar algo fuera del whitelist hardcodeado
- Auto-remediate sin que el MSP lo haya habilitado explícitamente

🔍 **REVIEW 9:** Remediation end-to-end: portal approve → heartbeat pull → agent fix → verify → audit trail. Rollback funciona.

---

## Resumen de orden

```
T1.1 → T1.2 → T1.3 → T1.4 → 🔍 REVIEW 1
T2.1 → T2.2 → T2.3 → 🔍 REVIEW 2
T3.1 → T3.2 → 🔍 REVIEW 3
T4.1 → T4.2 → 🔍 REVIEW 4
T5.1 → T5.2 → 🔍 REVIEW 5
T6.1 → T6.2 → 🔍 REVIEW 6
T7.1 → T7.2 → 🔍 REVIEW 7
T8.1 → T8.2 → T8.3 → T8.4 → 🔍 REVIEW 8
T9.1 → T9.2 → T9.3 → T9.4 → T9.5 → 🔍 REVIEW 9
```

Bloques 3, 4, 5 son independientes y pueden correr en paralelo.
Bloque 6 y 7 requieren Bloque 1 (service mode).
Bloque 8 es independiente (API-side).
Bloque 9 va último — requiere service mode + heartbeat estables.

---

## Migrations SQL necesarias

| Migration | Tabla | Campos |
|-----------|-------|--------|
| 061 | machines | last_heartbeat_at, agent_mode, is_trial, trial_expires_at |
| 062 | enrollment_codes | is_trial, trial_expires_at |
| 063 | machine_ports | banner, service_name, service_version |
| 064 | snmp_devices | reverse_dns, ping_latency_ms, ping_loss_pct, ping_jitter_ms |
| 065 | organizations | external_scan_consent, consent_granted_at, consent_granted_by |
| 066 | external_scan_results | NEW TABLE |
| 067 | external_scan_findings | NEW TABLE |
| 068 | remediation_actions | NEW TABLE (catálogo cerrado de acciones) |
| 069 | remediation_tasks | NEW TABLE (tasks pendientes/ejecutados/rollback) |
| 070 | org_auto_remediate | NEW TABLE (auto-fix opt-in per control) |

---

## Version bumps esperados

| Componente | De → A | Trigger |
|------------|--------|---------|
| Agent | 1.7.4 → 2.0.0 | Service mode = breaking change |
| API | 1.17.5 → 1.18.0+ | New endpoints + migrations |
| Portal | 1.10.0 → 1.11.0+ | Trial badges + external exposure tab |
