# Dataspace: Was läuft, wie es funktioniert, wie man es neu aufsetzt

Diese Doku beschreibt den kompletten Aufbau im Cluster: **was installiert ist**, **was dabei
genau passiert** und **wie man alles von Null neu aufsetzt**. Sprache bewusst einfach; alle
Abkürzungen werden bei der ersten Nennung erklärt.

---

## 1. Abkürzungen (einmal in Ruhe)

| Kürzel | Bedeutung | In einem Satz |
|---|---|---|
| **DID** | Decentralized Identifier | Eine Ausweisnummer, die man selbst besitzt, z. B. `did:web:alice-windx.cluster.swms-cloud.com`. Bei `did:web` liegt der zugehörige öffentliche Schlüssel einfach als Datei auf genau diesem Webserver. |
| **DID-Dokument** | – | Die Datei hinter der DID (`/.well-known/did.json`). Enthält den **öffentlichen** Schlüssel und wo man den Teilnehmer erreicht. |
| **VC** | Verifiable Credential | Ein digital unterschriebener Nachweis, z. B. „Alice ist Mitglied im Dataspace". |
| **VP** | Verifiable Presentation | Ein VC, das der Inhaber **vorzeigt** und dabei selbst mitunterschreibt („das gehört wirklich mir"). |
| **Wallet** | – | Der Ort, an dem ein Teilnehmer seine VCs aufbewahrt. Hier: der IdentityHub. |
| **IH** | IdentityHub | Die Wallet-Software eines Teilnehmers. Hält Schlüssel + VCs und stellt VPs aus. |
| **STS** | Secure Token Service | Teil des IH. Stellt kurzlebige Tokens aus, weil **nur der IH** den privaten Schlüssel hat. |
| **EDC** | Eclipse Dataspace Connector | Die Software, die Daten anbietet und abruft („Connector"). |
| **DSP** | Dataspace Protocol | Die Sprache, die zwei Connectoren miteinander sprechen (Katalog, Vertrag, Transfer). |
| **DCP** | Decentralized Claims Protocol | Die Sprache für **Credentials**: ausstellen und vorzeigen. |
| **BPN** | Business Partner Number | Die Firmennummer, z. B. `BPNL00000000WA01`. |
| **BDRS** | BPN-DID Resolution Service | Ein Telefonbuch: „zu welcher DID gehört diese BPN?" |
| **Issuer** | Aussteller | Die zentrale Stelle, die VCs ausstellt und unterschreibt. |
| **Vault** | – | Tresor für Geheimnisse (private Schlüssel, Passwörter). |

---

## 2. Das Grundprinzip in fünf Sätzen

1. Jeder Teilnehmer (Alice, Bob) hat eine **eigene DID** und eine **eigene Wallet**.
2. Eine **zentrale Stelle** stellt „Mitgliedsausweise" (**MembershipCredential**) aus und betreibt
   das Telefonbuch (**BDRS**). Mehr macht sie nicht.
3. Wollen zwei Teilnehmer Daten tauschen, **zeigen sie sich gegenseitig ihren Ausweis** — nicht die
   Zentrale entscheidet, sondern die beiden prüfen selbst.
4. Geprüft wird **kryptografisch**: die Unterschrift des Ausstellers wird gegen dessen öffentlichen
   Schlüssel geprüft, den man über die DID im Internet nachschlägt.
5. Deshalb ist das Ganze **dezentral**: die Zentrale kennt **kein einziges Geheimnis** der Teilnehmer.

---

## 3. Was im Cluster läuft

Drei Namespaces (`kubectl get ns | grep windx`). Jeder Teilnehmer ist vollständig autark —
theoretisch könnte er bei einer ganz anderen Firma in einem anderen Rechenzentrum stehen.

### 3.1 Zentral: Namespace `windx-auth`

| Was | Wozu |
|---|---|
| `dataspace-operator` (unsere .NET/XAF-Anwendung) | Issuer + BDRS + Admin-Oberfläche |

Öffentlich erreichbar unter **`https://auth-windx.cluster.swms-cloud.com`**:

| Adresse | Wozu |
|---|---|
| `/.well-known/did.json` | Unser DID-Dokument: unser **öffentlicher** Schlüssel + Hinweis, wo man Credentials anfragt |
| `/api/issuance/...` | Credentials ausstellen (DCP) |
| `/api/directory/bpn-directory` | Das Telefonbuch (BDRS). Nur mit gültigem Mitglieds-VP lesbar |
| `/status-lists/revocation` | Sperrliste (Widerruf) — siehe Einschränkung in Abschnitt 7 |

Die Zentrale speichert: Teilnehmer (Name, BPN, DID), ausgestellte Credentials, vertrauenswürdige
Aussteller und den **Audit-Trail**. Sie speichert **keine** privaten Schlüssel der Teilnehmer.

### 3.2 Pro Teilnehmer: `windx-alice` bzw. `windx-bob`

Jeder Namespace enthält **vier** Bausteine, die zusammengehören:

| Baustein | Beispiel Alice | Wozu |
|---|---|---|
| **Vault** | `alice-vault` | Tresor. Hier liegen Alices **privater** Schlüssel und ihr STS-Passwort |
| **Postgres** | `alice-postgres` | Datenbank für IH und Connector |
| **IdentityHub** | `alice-ih` | Alices Wallet: verwahrt VCs, stellt VPs aus, betreibt die STS |
| **Connector** | `alice-edc-controlplane` + `alice-edc-dataplane` | Bietet Daten an bzw. ruft sie ab |

Öffentlich erreichbar:

| Adresse | Wer | Wozu |
|---|---|---|
| `https://alice-windx.cluster.swms-cloud.com` | IdentityHub | DID-Dokument + Credential-Annahme/-Vorzeigen |
| `https://alice-edc-windx.cluster.swms-cloud.com` | Connector | DSP (`/api/v1/dsp`) + Datenabruf (`/api/public`) |

(Für Bob identisch mit `bob-…`.)

> **Wichtig:** Vault + IH + Connector eines Teilnehmers teilen sich **einen** Vault. Das ist
> *innerhalb* eines Teilnehmers — nichts wird über Teilnehmergrenzen hinweg geteilt. Genau das
> macht den Aufbau dezentral-tauglich (siehe Abschnitt 7, Punkt „Warum ein geteilter Vault").

### 3.3 Beispiel-Datenquelle

`bob-backend` (nginx) in `windx-bob` liefert unter `/asset.json` eine kleine Datei. Das ist der
„interne REST-Dienst", den Bob über seinen Connector anbietet.

---

## 4. Was genau passiert — die drei Abläufe

### 4.1 Ablauf A: Teilnehmer bekommt seinen Mitgliedsausweis (DCP)

Ausgelöst im Admin-UI mit **„Issue Membership Credential"**.

1. **Angebot.** Die Zentrale schickt an Alices Wallet: „Ich hätte hier ein MembershipCredential
   für dich." (`CredentialOfferMessage`)
2. **Alice fragt selbst an.** Alices Wallet schlägt in unserem DID-Dokument nach, *wo* man
   Credentials anfragt, und schickt eine Anfrage dorthin (`CredentialRequestMessage`). Sie legt
   ein selbst unterschriebenes Token bei.
3. **Wir prüfen Alice.** Wir holen Alices DID-Dokument, nehmen ihren öffentlichen Schlüssel und
   prüfen die Unterschrift. Passt sie, antworten wir mit „angenommen" + Vorgangsnummer.
4. **Wir stellen aus und liefern.** Wir unterschreiben das VC mit **unserem** privaten Schlüssel und
   schicken es an Alices Wallet (`CredentialMessage`), passend zur Vorgangsnummer.
5. **Alice legt es ab.** Ihre Wallet ordnet es der Anfrage zu und speichert es.

> Entscheidend: **Alice fragt an, wir liefern.** Wir können nichts „hineindrücken" — das ist die
> vorgesehene Richtung im DCP.

### 4.2 Ablauf B: Zwei Teilnehmer lernen sich kennen (Vertrauen)

Alice will Bobs Katalog sehen und kennt nur seine **BPN**.

1. **Token holen.** Alices Connector hat den privaten Schlüssel *nicht* — den hat nur ihr IH.
   Also lässt er sich von der STS ein Token ausstellen (dafür braucht er das STS-Passwort aus
   dem gemeinsamen Vault).
2. **Telefonbuch fragen.** Alices Connector fragt bei uns (BDRS): „Welche DID hat `BPNL...WB02`?"
   Um überhaupt fragen zu dürfen, **zeigt er Alices Mitglieds-VP vor**. Wir prüfen: Unterschrift
   des Inhabers ✓, Unterschrift des Ausstellers ✓, Aussteller vertrauenswürdig ✓ → wir antworten.
3. **Anfragen.** Alices Connector schickt die Katalogfrage an Bobs Connector (DSP) und legt ein
   selbst unterschriebenes Token bei.
4. **Bob prüft Alice.** Bobs Connector holt sich Alices Mitglieds-VP, prüft es genauso — und
   antwortet erst dann mit dem Katalog.

### 4.3 Ablauf C: Datei abholen (DSP)

1. **Katalog** — Alice sieht Bobs Angebot (`bob-asset-1`).
2. **Vertrag** — Alice nimmt das Angebot an; beide Seiten prüfen die Regeln, es entsteht ein
   **Agreement**.
3. **Transfer** — Alice startet den Transfer. Bob liefert eine **EDR** zurück: eine Adresse plus
   ein kurzlebiges Zugriffstoken.
4. **Abholen** — Alice ruft die Adresse mit dem Token ab. Bobs Dataplane holt die Datei aus
   `bob-backend` und reicht sie durch.

Ergebnis (echter Lauf):

```
{"message":"Hello Alice, this is Bob's shared file via the dataspace!","secret":"windx-42",...}
```

### 4.4 Audit-Trail

Jeder Aufruf gegen die Protokoll-Endpunkte der Zentrale wird protokolliert: Zeitpunkt, Art,
Methode, Pfad, Statuscode, Dauer, Request-Body und Ergebnis. Wenn der Aufruf einem Teilnehmer
zugeordnet werden kann (über die DID), erscheint er im Admin-UI direkt **beim Teilnehmer** unter
`AuditEntries`. Das Protokollieren kann einen Protokollaufruf nie zum Scheitern bringen.

---

## 5. Frisch aufsetzen (von Null)

Voraussetzungen: Kubernetes mit **nginx-Ingress** und **cert-manager** (ClusterIssuer
`letsencrypt-prod`), DNS zeigt auf den Ingress, Helm installiert.

```bash
helm repo add tractusx-edc https://eclipse-tractusx.github.io/charts/dev
helm repo update
kubectl create ns windx-auth; kubectl create ns windx-alice; kubectl create ns windx-bob
```

### Schritt 1 — Zentrale

```bash
helm upgrade --install dataspace-operator ./helm/dataspace-operator \
  -n windx-auth -f windx-values.yaml
```

Wichtige Werte in `windx-values.yaml`:

```yaml
image: { repository: ghcr.io/swmsconsulting/dataspaceoperator, tag: sha-XXXXXXX }
issuer:
  did: did:web:auth-windx.cluster.swms-cloud.com
  privateSeedBase64: "<32-Byte-Ed25519-Seed, base64>"   # unser privater Schlüssel
  includeCredentialStatus: false                        # siehe Abschnitt 7
admin: { username: "Admin", password: "<Passwort>" }
```

Prüfen: `curl https://auth-windx.cluster.swms-cloud.com/.well-known/did.json` muss das
DID-Dokument liefern (inkl. `IssuerService`-Eintrag).

### Schritt 2 — Pro Teilnehmer: Vault + Postgres

```bash
kubectl -n windx-alice apply -f vault-alice.yaml      # dev-mode, Root-Token "root"
kubectl -n windx-alice apply -f postgres-alice.yaml   # DBs: ih + edc
```

Super-User-Schlüssel für die Verwaltungs-API des IH in den Vault legen:

```bash
kubectl -n windx-alice exec deploy/alice-vault -- sh -c \
  'VAULT_ADDR=http://127.0.0.1:8200 VAULT_TOKEN=root \
   vault kv put secret/sup3r\$3cr3t content="c3VwZXItdXNlcg==.c3VwZXItc2VjcmV0LWtleQo="'
```

### Schritt 3 — IdentityHub (Wallet)

```bash
helm upgrade --install alice-ih tractusx-edc/tractusx-identityhub \
  --version 0.3.2 -n windx-alice -f ih-full-alice.yaml
```

### Schritt 4 — Teilnehmer im IdentityHub anlegen

Erzeugt Alices Schlüsselpaar, ihr STS-Konto und ihr DID-Dokument. **Die Antwort enthält das
`clientSecret` — es landet automatisch im gemeinsamen Vault**, der Connector liest es von dort.

```bash
kubectl -n windx-alice port-forward svc/alice-ih 8082:8082 &
curl -X POST http://localhost:8082/api/identity/v1alpha/participants \
 -H 'Content-Type: application/json' \
 -H 'X-Api-Key: c3VwZXItdXNlcg==.c3VwZXItc2VjcmV0LWtleQo=' \
 -d '{
  "active": true,
  "did": "did:web:alice-windx.cluster.swms-cloud.com",
  "participantContextId": "did:web:alice-windx.cluster.swms-cloud.com",
  "key": { "keyGeneratorParams": {"algorithm":"EdDSA","curve":"Ed25519"},
           "keyId": "did:web:alice-windx.cluster.swms-cloud.com#signing-key-1",
           "privateKeyAlias": "did:web:alice-windx.cluster.swms-cloud.com#signing-key-1" },
  "serviceEndpoints": [
    {"type":"CredentialService","id":"credentialservice-1",
     "serviceEndpoint":"https://alice-windx.cluster.swms-cloud.com/api/credentials/v1/participants/<BASE64-DER-DID>"},
    {"type":"ProtocolEndpoint","id":"dsp-url",
     "serviceEndpoint":"https://alice-edc-windx.cluster.swms-cloud.com/api/v1/dsp"}
  ]}'
```

`<BASE64-DER-DID>`: `echo -n "did:web:alice-windx.cluster.swms-cloud.com" | base64`

### Schritt 5 — Connector

```bash
helm upgrade --install alice-edc <gepatchtes-chart>/tractusx-connector \
  -n windx-alice -f conn-full-alice.yaml
```

> **Achtung, Chart-Fehler:** `tractusx-connector` 0.12.1 erzeugt doppelte `WEB_HTTP_CATALOG_*`-
> Einträge. Bei Server-Side-Apply bricht das Deployment ab. Abhilfe: Chart lokal ziehen
> (`helm pull … --untar`) und in `templates/deployment-controlplane.yaml` den **doppelten**
> Catalog-/Federated-Catalog-Block entfernen.

> **Reihenfolge:** Die Dataplane meldet sich beim Controlplane an. Startet sie zuerst, geht sie
> in CrashLoop — nach dem Hochlaufen des Controlplane fängt sie sich von selbst.

### Schritt 6 — Teilnehmer zentral registrieren

Im Admin-UI (`https://auth-windx.cluster.swms-cloud.com`) je einen Participant anlegen:

| Feld | Alice | Bob |
|---|---|---|
| Bpn | `BPNL00000000WA01` | `BPNL00000000WB02` |
| Did | `did:web:alice-windx.cluster.swms-cloud.com` | `did:web:bob-windx.cluster.swms-cloud.com` |
| CredentialServiceUrl | `https://alice-windx…/api/credentials/v1/participants/<BASE64>` | analog |

> Die **BPN muss überall exakt gleich** sein: hier, in `participant.id` des Connectors und im
> Katalog-Aufruf. Ein Tippfehler führt zu „Empty optional" bei der Auflösung.

### Schritt 7 — Credentials ausstellen

Im Admin-UI beim Teilnehmer **„Issue Membership Credential"**. Kontrolle im IH-Log:
`HolderCredentialRequest … is now in state ISSUED`.

### Schritt 8 — Anbieter einrichten und testen

Auf Bobs Connector (Management-API, Standard-Key `password`) Asset, Policy und
Contract-Definition anlegen; dann von Alice aus: Katalog → Negotiation → Transfer → Abruf.
Die konkreten Aufrufe stehen in `DEPLOY.md`.

---

## 6. Wo man nachschaut, wenn etwas klemmt

| Symptom | Wo nachsehen |
|---|---|
| Credential kommt nicht an | IH-Log des Empfängers: `state ISSUED` oder `state ERROR` mit Grund |
| `401` beim BDRS | Zentrale-Log: `BDRS read rejected: …` nennt den genauen Grund |
| `Empty optional` | BPN stimmt nicht überein (Zentrale ⇄ Connector ⇄ Aufruf) |
| Connector-Fehler ohne Details | Log4j2-Template des Connectors zeigt standardmäßig **keine** Stacktraces — im ConfigMap `…-log4j2` einen `exception`-Resolver ergänzen |
| Katalog `500` bei der Gegenseite | Meist die Credential-Prüfung; Stacktrace im Controlplane-Log der Gegenseite |

Audit-Trail im Admin-UI: jeder Aufruf mit Zeit, Pfad, Status und Ergebnis — beim jeweiligen
Teilnehmer.

---

## 7. Bekannte Einschränkungen und bewusste Entscheidungen

**Widerruf (Revocation) ist derzeit ausgeschaltet.** Grund: Der IdentityHub lädt die Sperrliste und
erwartet ein **signiertes JWT**; der EDC-Connector lädt **dieselbe URL** und erwartet **JSON**.
Beide fragen identisch an (gleicher HTTP-Client, `Accept: */*`) — eine Unterscheidung ist nicht
möglich. Damit Vorzeigen *und* Prüfen funktionieren, wird `credentialStatus` derzeit weggelassen:

```yaml
issuer:
  includeCredentialStatus: false
```

Auf `true` stellen, sobald beide Seiten dasselbe Format akzeptieren. Ohne `credentialStatus`
prüft keine Seite die Sperrliste — ausgestellte Credentials gelten bis zum Ablaufdatum.

**Warum ein geteilter Vault kein Widerspruch zur Dezentralität ist.** Der Vault wird nur
*innerhalb eines Teilnehmers* geteilt (Alices IH + Alices Connector). Er enthält Alices eigene
Geheimnisse in Alices eigener Infrastruktur. Die Zentrale hat darauf keinen Zugriff und speichert
selbst kein einziges Teilnehmer-Geheimnis. Genau so ist der tractusx-Standardaufbau gedacht.

**Dev-Einstellungen, die vor Produktivbetrieb zu ändern sind:**
- Vault läuft im `-dev`-Modus mit Root-Token `root` (nicht persistent).
- Connector-Management-API nutzt den Standardschlüssel `password`.
- Der Super-User-Schlüssel des IH ist der bekannte MXD-Standardwert.
- `POST /api/issuance/offer` an der Zentrale ist derzeit **nicht** authentifiziert (Test-Trigger).

**Versionen:** IdentityHub-Chart `0.3.2`, Connector-Chart `0.12.1` (lokal gepatcht) — zwei
verschiedene Release-Stränge. Beim Aktualisieren beide zusammen prüfen.
