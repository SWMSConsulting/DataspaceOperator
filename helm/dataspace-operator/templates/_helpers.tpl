{{- define "dataspace-operator.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "dataspace-operator.fullname" -}}
{{- if .Values.fullnameOverride -}}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- printf "%s-%s" .Release.Name (include "dataspace-operator.name" .) | trunc 63 | trimSuffix "-" -}}
{{- end -}}
{{- end -}}

{{- define "dataspace-operator.labels" -}}
app.kubernetes.io/name: {{ include "dataspace-operator.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version }}
{{- end -}}

{{- define "dataspace-operator.selectorLabels" -}}
app.kubernetes.io/name: {{ include "dataspace-operator.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}

{{- define "dataspace-operator.serviceAccountName" -}}
{{- if .Values.serviceAccount.create -}}
{{- default (include "dataspace-operator.fullname" .) .Values.serviceAccount.name -}}
{{- else -}}
{{- default "default" .Values.serviceAccount.name -}}
{{- end -}}
{{- end -}}

{{/* Name of the secret holding the issuer seed (existing or chart-managed). */}}
{{- define "dataspace-operator.issuerSecretName" -}}
{{- if .Values.issuer.existingSecret -}}
{{- .Values.issuer.existingSecret -}}
{{- else -}}
{{- include "dataspace-operator.fullname" . -}}
{{- end -}}
{{- end -}}

{{/* Resolved connection string with {dataDir} substituted. */}}
{{- define "dataspace-operator.connectionString" -}}
{{- .Values.database.connectionString | replace "{dataDir}" .Values.persistence.mountPath -}}
{{- end -}}

{{/* Vault seed read (KV). */}}
{{- define "dataspace-operator.vaultApp" -}}
{{- and .Values.vault.enabled .Values.vault.app.enabled -}}
{{- end -}}

{{/* Vault Transit signing (key never leaves Vault). */}}
{{- define "dataspace-operator.vaultTransit" -}}
{{- and .Values.vault.enabled .Values.vault.transit.enabled -}}
{{- end -}}

{{/* True when Vault supplies the issuer key (KV or Transit) — then no K8s Secret seed is mounted. */}}
{{- define "dataspace-operator.vaultManaged" -}}
{{- or (eq (include "dataspace-operator.vaultApp" .) "true") (eq (include "dataspace-operator.vaultTransit" .) "true") -}}
{{- end -}}

{{/* Secret-backed + Vault env vars for the app + migration init container. */}}
{{- define "dataspace-operator.appSecretsEnv" -}}
{{- if ne (include "dataspace-operator.vaultManaged" .) "true" }}
- name: Issuer__PrivateSeedBase64
  valueFrom:
    secretKeyRef:
      name: {{ include "dataspace-operator.issuerSecretName" . }}
      key: {{ .Values.issuer.existingSecretSeedKey }}
{{- end }}
- name: DevExpress__ExpressApp__Security__UrlSigningKey
  valueFrom:
    secretKeyRef:
      name: {{ include "dataspace-operator.fullname" . }}
      key: url-signing-key
{{- if eq (include "dataspace-operator.vaultApp" .) "true" }}
- name: Vault__Enabled
  value: "true"
- name: Vault__Address
  value: {{ .Values.vault.app.address | quote }}
{{- with .Values.vault.app.token }}
- name: Vault__Token
  value: {{ . | quote }}
{{- end }}
{{- with .Values.vault.app.kubernetesRole }}
- name: Vault__KubernetesRole
  value: {{ . | quote }}
{{- end }}
- name: Vault__KubernetesAuthMount
  value: {{ .Values.vault.app.kubernetesAuthMount | quote }}
- name: Vault__KvMount
  value: {{ .Values.vault.app.kvMount | quote }}
- name: Vault__SecretPath
  value: {{ .Values.vault.app.secretPath | quote }}
- name: Vault__SeedField
  value: {{ .Values.vault.app.seedField | quote }}
{{- end }}
{{- if eq (include "dataspace-operator.vaultTransit" .) "true" }}
- name: Vault__Transit__Enabled
  value: "true"
- name: Vault__Transit__Address
  value: {{ .Values.vault.transit.address | quote }}
{{- with .Values.vault.transit.token }}
- name: Vault__Transit__Token
  value: {{ . | quote }}
{{- end }}
{{- with .Values.vault.transit.kubernetesRole }}
- name: Vault__Transit__KubernetesRole
  value: {{ . | quote }}
{{- end }}
- name: Vault__Transit__KubernetesAuthMount
  value: {{ .Values.vault.transit.kubernetesAuthMount | quote }}
- name: Vault__Transit__Mount
  value: {{ .Values.vault.transit.mount | quote }}
- name: Vault__Transit__KeyName
  value: {{ .Values.vault.transit.keyName | quote }}
{{- end }}
{{- end -}}
