# Vorfall 23.08.2026: Teilnehmer verliert seine Identität (Dave)

Dave konnte das Teilnehmerverzeichnis (BDRS) nicht mehr aufrufen. Ursache war nicht das
Verzeichnis, sondern ein **Totalverlust der IdentityHub-Datenbank** — ausgelöst durch einen
Node-Wechsel bei einem Postgres ohne persistentes Volume.

Dieses Dokument beschreibt den Fall, die Diagnosekette und die Reparatur. Die Diagnosekette ist
so aufgebaut, dass sie sich auf jeden Teilnehmer anwenden lässt; Abkürzungen sind in
[dataspace-betrieb-und-aufbau.md](dataspace-betrieb-und-aufbau.md) Abschnitt 1 erklärt.

---

## 1. Kurzfassung

| | |
|---|---|
| **Symptom** | Dave erreicht das Teilnehmerverzeichnis nicht; Controlplane-Log voll mit `StateMachineManager … error caught during processor` |
| **Eigentliche Ursache** | `postgres-dave.yaml` deklarierte `emptyDir`. Der Node-Wechsel am 23.08.2026 löschte die Datenbanken `edc` und `ih`. |
| **Folge** | Participant Context, Keypair, DID-Dokument und Credentials waren weg. Ohne DID kein STS-Token, ohne Token kein Verzeichniszugriff. |
| **Warum reparierbar** | Der Vault-Pod war seit 31 Tagen nicht neu gestartet und hielt den **originalen privaten Signing-Key** noch im Speicher. |
| **Reparatur** | Participant Context mit dem **vorhandenen** Schlüssel neu angelegt → DID unverändert → kein Re-Onboarding nötig. |
| **Dauer** | Verlust am 23.08., bemerkt und behoben am 24.08.2026. |

---

## 2. Was man sah — und was es wirklich war

Der sichtbare Einstieg war ein leeres DID-Dokument:

```bash
curl -s -o /dev/null -w '%{http_code}\n' https://dave-windx.cluster.swms-cloud.com/.well-known/did.json
# 204
```

**HTTP 204 ist hier kein Ingress- oder Zertifikatsproblem.** Es ist die Antwort des
IdentityHub selbst: „für diese Identität habe ich kein DID-Dokument". Der Ingress liefert
korrekt aus, der IdentityHub hat nur nichts zu liefern. Der Direktaufruf im Cluster auf dem
DID-Port bestätigt das:

```bash
kubectl -n windx-dave run t --rm -i --restart=Never --image=curlimages/curl -- \
  curl -si http://dave-ih:8084/.well-known/did.json
```

Ebenso irreführend war der zweite Befund:

```
ERROR  HTTP client exception caught for request [POST, http://dave-ih:8087/api/sts/token]
```

Das liest sich wie ein Netzwerk- oder Lastproblem, ist aber ein **401 `invalid_client`**: Zum
Client `did:web:dave-windx…` existierte im IdentityHub kein STS-Account mehr. Die Flut an
`StateMachineManager … error caught during processor` ist reine Folgewirkung — jede State
Machine scheitert daran, dass kein Token zu bekommen ist.

> Der Connector zeigt standardmäßig keine Stacktraces (siehe Betriebsdoku Abschnitt 6). Die
> Meldungen nennen deshalb nie die eigentliche Ursache. Nicht dem Wortlaut folgen, sondern die
> Prüfkette in Abschnitt 4 abarbeiten.

---

## 3. Ursachenkette

```
postgres-dave.yaml: volume "data" -> emptyDir
        │
        ▼  Node-Wechsel 23.08.2026 (Cluster unter Speicherdruck, Evictions)
Postgres-Pod startet auf neuem Node -- Datenbanken "edc" und "ih" leer
        │
        ▼  IdentityHub bootet gegen leere DB
Flyway legt nur das leere Schema an; Super-User wird neu erzeugt.
Participant Context, Keypair, DID-Ressource, Credentials: weg.
        │
        ├──▶ kein DID-Dokument      -> did.json = HTTP 204
        ├──▶ kein STS-Client        -> /api/sts/token = 401 invalid_client
        └──▶ keine MembershipCredential -> BDRS = 401
```

Der **Speicherdruck im Cluster war nur der Auslöser**, nicht die Ursache. Ursache ist das
fehlende PVC: Jeder Pod-Neustart hätte denselben Effekt gehabt — Update, OOM, Eviction,
Node-Wartung.

### Warum der Ausfall still blieb

Das ist der gefährlichste Teil des Vorfalls: **Kubernetes meldete alle Pods `Ready`.**

Flyway legt das Datenbankschema nur **beim Boot** an. Ein bereits laufender Connector bemerkt
den Verlust nie — er hat seine Verbindung, die Tabellen existieren (leer), die Liveness-Probe
antwortet. Der Ausfall wird erst sichtbar, wenn jemand einen Datenaustausch versucht. Bei Dave
lag über einen Tag zwischen Verlust und Entdeckung; laut Commit `e3d19aa` traf dasselbe zuvor
Charlie am 01.08.2026.

---

## 4. Diagnosekette

Von außen nach innen. Der erste Schritt, der ein unerwartetes Ergebnis liefert, benennt die
Ebene des Problems.

| # | Prüfung | Gesund | Bei Dave |
|---|---|---|---|
| 1 | `did.json` öffentlich abrufbar | `200` + Dokument | **`204`** |
| 2 | `did.json` direkt am IH (Port 8084) | `200` | `204` → IH, nicht Ingress |
| 3 | `participant_context` in DB `ih` | Zeile je Teilnehmer, `state 200` | **nur `super-user`** |
| 4 | `did_resources` | DID des Teilnehmers, `state 300` | **nur `did:web:super-user`** |
| 5 | `keypair_resource` | `key_id = <did>#signing-key-1`, `state 200` | **nur super-user-key** |
| 6 | `edc_sts_client` | Client-Eintrag je Teilnehmer | **nur `super-user`** |
| 7 | `credential_resource` | ≥ 1 Zeile, `vc_state 500` | **0 Zeilen** |
| 8 | STS-Token beziehbar | `200` + `access_token` | **`401 invalid_client`** |
| 9 | Vault-Inhalt | Signing-Key + STS-Secret vorhanden | **vorhanden** ← Rettung |

Datenbank prüfen:

```bash
kubectl -n windx-dave exec deploy/dave-postgres -- sh -c \
  'PGPASSWORD=$POSTGRES_PASSWORD psql -U edc -d ih \
     -c "select participant_context_id, state from participant_context;" \
     -c "select did, state from did_resources;" \
     -c "select key_id, state from keypair_resource;" \
     -c "select count(*) from credential_resource;"'
```

Zustandscodes: Participant Context `200` = ACTIVATED · DID-Ressource `300` = PUBLISHED ·
Keypair `200` = ACTIVE · Credential `500` = ISSUED.

Vault prüfen — **der entscheidende Schritt**, er entscheidet über den Reparaturweg:

```bash
kubectl -n windx-dave exec deploy/dave-vault -- sh -c \
  'VAULT_ADDR=http://127.0.0.1:8200 VAULT_TOKEN=$VAULT_DEV_ROOT_TOKEN_ID vault kv list secret'
```

Erwartet werden unter anderem (Schlüsselnamen liegen prozentkodiert im KV-Store):

```
did%3Aweb%3Adave-windx.cluster.swms-cloud.com%23signing-key-1
did%3Aweb%3Adave-windx.cluster.swms-cloud.com-sts-client-secret
```

**Ist der Signing-Key noch da → Weg A (Abschnitt 5). Ist er weg → Weg B (Abschnitt 7).**

---

## 5. Reparatur mit vorhandenem Schlüssel (Weg A)

### Schritt 1 — Vault sofort sichern

Zuerst, nicht zuletzt: Der Vault läuft im Dev-Modus (In-Memory). Solange er nicht neu
gestartet ist, hält er den einzigen Beweis der Identität. Bei einem Cluster unter
Speicherdruck ist das eine Frage von Minuten.

```bash
sed -e 's/__NS__/windx-dave/' -e 's/__PARTICIPANT__/dave/' \
  deploy/participants/vault-seeder.yaml | kubectl apply -f -
kubectl -n windx-dave wait --for=condition=complete job/vault-seeder --timeout=120s
kubectl -n windx-dave logs job/vault-seeder

sed -e 's/__NS__/windx-dave/' -e 's/__PARTICIPANT__/dave/' \
  deploy/participants/vault-keeper.yaml | kubectl apply -f -
```

[vault-seeder.yaml](../deploy/participants/vault-seeder.yaml) erzeugt das Secret
`vault-backup`, [vault-keeper.yaml](../deploy/participants/vault-keeper.yaml) spielt daraus
nach einem Neustart automatisch zurück. Beide arbeiten vollständig im Cluster — privates
Schlüsselmaterial landet nicht auf einem Arbeitsplatz.

> Der Seeder bricht ab, wenn der Vault leer ist. Er darf **nur gegen einen gesunden Vault**
> laufen, sonst überschreibt er ein gutes Backup mit einem leeren.

### Schritt 2 — Participant Context wiederherstellen

```bash
sed -e 's/__NS__/windx-dave/' -e 's/__PARTICIPANT__/dave/' \
    -e 's/__DID_HOST__/dave-windx.cluster.swms-cloud.com/' \
    -e 's/__EDC_HOST__/dave-edc-windx.cluster.swms-cloud.com/' \
    deploy/participants/ih-provision.yaml | kubectl apply -f -
kubectl -n windx-dave wait --for=condition=complete job/ih-provision --timeout=180s
kubectl -n windx-dave logs job/ih-provision
```

[ih-provision.yaml](../deploy/participants/ih-provision.yaml) liest den vorhandenen Schlüssel
aus dem Vault und übergibt nur dessen **öffentlichen** Teil als `publicKeyJwk`.

**Das ist der Unterschied zu [DEPLOY.md](DEPLOY.md) Schritt 5**, der mit `keyGeneratorParams`
arbeitet und den IdentityHub einen **neuen** Schlüssel erzeugen lässt. Für einen Neuaufbau ist
das richtig, für eine Wiederherstellung falsch — es überschriebe den alten privaten Key im
Vault, und ein zuvor gezogenes Backup zeigte dann auf einen Schlüssel, den DID-Dokument und
Datenbank nicht mehr kennen. Der `vault-keeper` würde im Ernstfall eine kaputte Identität
zurückspielen.

### Schritt 3 — Backup nachziehen und Connector neu starten

Die Provisionierung erzeugt ein **neues STS-Client-Secret** und legt es im Vault ab. Damit ist
das Backup aus Schritt 1 veraltet:

```bash
kubectl -n windx-dave delete job vault-seeder
sed -e 's/__NS__/windx-dave/' -e 's/__PARTICIPANT__/dave/' \
  deploy/participants/vault-seeder.yaml | kubectl apply -f -

kubectl -n windx-dave rollout restart deploy/vault-keeper
kubectl -n windx-dave rollout restart deploy/dave-edc-controlplane
```

### Schritt 4 — EdcAdmin nachziehen

EdcAdmin hält das STS-Client-Secret als eigene Kopie. Wird es nicht aktualisiert, meldet die
Oberfläche Authentifizierungsfehler, obwohl der Connector längst gesund ist:

```bash
kubectl -n windx-dave patch secret edcadmin-dave \
  -p '{"stringData":{"sts-client-secret":"<neues Secret aus ih-provision-Log>"}}'
kubectl -n windx-dave rollout restart deploy/edcadmin-dave
```

### Schritt 5 — Prüfen

```bash
# DID wieder öffentlich, mit unverändertem Public Key
curl -s https://dave-windx.cluster.swms-cloud.com/.well-known/did.json | jq .

# Controlplane fehlerfrei -- beide Zahlen müssen 0 sein
CP=$(kubectl -n windx-dave get pods --no-headers | grep controlplane | awk '{print $1}')
kubectl -n windx-dave logs $CP | grep -c 'error caught during processor'
kubectl -n windx-dave logs $CP | grep -ciE 'invalid_client|HTTP client exception'

# Ende-zu-Ende: Katalog über die BPN abrufen (erzwingt die BDRS-Auflösung)
curl -s -X POST http://dave-edc-controlplane:8081/management/v3/catalog/request \
  -H "X-Api-Key: $APIKEY" -H 'Content-Type: application/json' \
  -d '{"@context":{"@vocab":"https://w3id.org/edc/v0.0.1/ns/"},
       "counterPartyAddress":"https://alice-edc-windx.cluster.swms-cloud.com/api/v1/dsp",
       "counterPartyId":"BPNL00000000WA01",
       "protocol":"dataspace-protocol-http"}'
```

Der Katalogabruf ist der eigentliche Beweis: Er läuft über die **BPN**, nicht über die DID, und
setzt damit die gesamte Kette voraus — BPN im Verzeichnis auflösen, Token prägen, Credential
vorzeigen, Gegenseite validiert.

---

## 6. Was danach von selbst passierte

Nach der Wiederherstellung stand die MembershipCredential ohne weiteres Zutun wieder im
IdentityHub — ausgestellt vom Issuer `did:web:auth-windx…`, `FullMember`, `holderIdentifier`
gleich der BPN, ein Jahr gültig.

**Der DCP-Issuance-Flow heilt sich selbst, sobald der DID des Inhabers wieder auflösbar ist.**
Ein manueller Onboarding-Schritt beim Operator war nicht nötig. Bei einem Verlust wie diesem
gilt also: erst Identität wiederherstellen, dann prüfen — das Credential kommt in der Regel
von allein nach.

Das gilt ausdrücklich **nur bei unverändertem DID**. Wurde ein neuer Schlüssel erzeugt oder
ein neuer DID vergeben, ist der Operator im Spiel (Weg B).

---

## 7. Wenn auch der Vault leer ist (Weg B)

Dann ist die Identität nicht wiederherstellbar. Der private Schlüssel existierte nur dort.

1. [DEPLOY.md](DEPLOY.md) Schritt 5 fahren, also mit `keyGeneratorParams` einen **neuen**
   Schlüssel erzeugen.
2. Neuen DID beim Operator onboarden: BPN→DID im BDRS aktualisieren, MembershipCredential neu
   ausstellen lassen.
3. Danach `vault-seeder` und `vault-keeper` einrichten, damit sich der Fall nicht wiederholt.

Der DID selbst (`did:web:<host>`) bleibt dabei textlich gleich — aber der hinterlegte
öffentliche Schlüssel ist ein anderer. Alles, was den alten Schlüssel zwischengespeichert hat,
muss ihn neu laden.

---

## 8. Stolperfallen (in dieser IdentityHub-Version, Chart 0.3.2)

| Fallstrick | Detail |
|---|---|
| Feldname im Manifest | Die Identity API erwartet **`participantContextId`**, nicht `participantId`. Sonst: `400 ValidationFailure: participantContextId cannot be null or empty`. |
| DID veröffentlichen | `POST .../participants/<b64did>/did/publish` liefert **404**. Nicht nötig: `"active": true` im Manifest veröffentlicht den DID direkt (`did_resources.state = 300`). |
| STS-Secret | Wird bei der Provisionierung **neu erzeugt**, nicht wiederverwendet. Vault-Backup und EdcAdmin danach nachziehen. |
| Vault-Schlüsselnamen | Liegen prozentkodiert im KV-Store (`did%3Aweb%3A…%23signing-key-1`). Im HTTP-Pfad muss `%` zusätzlich zu `%25` werden. |
| `configurable.enable` | In `dave-ih-config` steht `edc.tractusx.ih.participant.configurable.enable=false`, der zugehörige `…configurable.secret` ist noch der Platzhalter `changeme-…`. Der Context wurde per Identity API angelegt — die Chart-Variante (DEPLOY.md, Abschnitt „Alternative provisioning") ist **nicht** aktiv. Beide Wege gleichzeitig zu nutzen, erzeugt einen Konflikt. |

---

## 9. Vorbeugung

**Erledigt:**

- `postgres-{alice,bob,dave}.yaml` auf PVC umgestellt (Commit `e3d19aa`).
- `vault-keeper` läuft jetzt auch in `windx-dave` (vorher nur Alice und Bob).
- `vault-seeder.yaml` und `ih-provision.yaml` versioniert — die Wiederherstellung ist kein
  Handbetrieb mehr und läuft ohne Schlüsselmaterial auf einem Arbeitsplatz.

**Offen:**

- **`charlie-postgres` steht weiterhin auf `emptyDir`.** Commit `e3d19aa` hat Charlie nicht
  erfasst. Charlie hat zudem keinen `vault-keeper` — der nächste Node-Wechsel trifft ihn wie
  Dave, nur ohne Sicherheitsnetz.
- **Vault im Dev-Modus.** Der Keeper ist ein Pflaster. Dauerhaft: File- oder Raft-Backend auf
  einem PVC mit regulärem Unseal.
- **Stiller Ausfall.** Bereitschaftsprüfungen melden `Ready`, während die Identität fehlt.
  Sinnvoll wäre ein Health-Check, der `did.json` und einen STS-Token-Abruf prüft, statt nur den
  HTTP-Port.
- **Speicherdruck im Cluster.** Am 24.08.2026 standen 5 von 10 Nodes auf
  `MemoryPressure=True`, während der Arbeit wurden mehrfach Pods evakuiert. Solange das
  anhält, sind Pod-Umzüge Normalfall, nicht Ausnahme.
