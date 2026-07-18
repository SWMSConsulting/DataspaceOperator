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

{{/* True when the app should read the issuer seed natively from Vault. */}}
{{- define "dataspace-operator.vaultApp" -}}
{{- and .Values.vault.enabled .Values.vault.app.enabled -}}
{{- end -}}

{{/* Secret-backed + Vault env vars for the app + migration init container. */}}
{{- define "dataspace-operator.appSecretsEnv" -}}
{{- if not (eq (include "dataspace-operator.vaultApp" .) "true") }}
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
{{- end -}}
