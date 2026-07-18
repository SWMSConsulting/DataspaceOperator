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

{{/* Secret-backed env vars for the app + migration init container. */}}
{{- define "dataspace-operator.appSecretsEnv" -}}
{{- if not (and .Values.vault.enabled .Values.vault.injector.enabled) }}
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
{{- end -}}
