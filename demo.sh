#!/usr/bin/env bash
#
# Tour guiado de la API de revisión de documentos ecert.
#
# Uso:
#   docker compose up --build -d
#   ./demo.sh
#
# Recorre el ciclo de vida completo de un documento contra la API en vivo:
# datos sembrados, registro, revisión, observaciones, rechazo, nueva versión,
# aprobación y trazabilidad. Requiere curl y (jq o python3) para formatear.
set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost:8080}"
cd "$(dirname "$0")"

RESPONSE=""
STEP=0

step() {
    STEP=$((STEP + 1))
    echo
    echo "────────────────────────────────────────────────────────────────"
    echo "PASO $STEP: $1"
    echo "────────────────────────────────────────────────────────────────"
}

pretty() {
    if command -v jq >/dev/null 2>&1; then
        jq . | sed 's/^/    /'
    elif command -v python3 >/dev/null 2>&1; then
        python3 -m json.tool | sed 's/^/    /'
    else
        sed 's/^/    /'
    fi
}

# extract <expresión jq> <expresión python sobre `d`>
extract() {
    if command -v jq >/dev/null 2>&1; then
        jq -r "$1" <<<"$RESPONSE"
    else
        python3 -c "import json,sys; d=json.load(sys.stdin); print($2)" <<<"$RESPONSE"
    fi
}

# call <código esperado> <método> <ruta> [opciones curl...]
call() {
    local expected=$1 method=$2 path=$3
    shift 3
    echo
    echo "  \$ curl -X $method $BASE_URL$path" "$@"
    local out code
    out=$(curl -s -w $'\n%{http_code}' -X "$method" "$BASE_URL$path" "$@")
    code=${out##*$'\n'}
    RESPONSE=${out%$'\n'*}
    echo "  → HTTP $code"
    if [ -n "$RESPONSE" ]; then
        pretty <<<"$RESPONSE"
    fi
    if [ "$code" != "$expected" ]; then
        echo
        echo "ERROR: se esperaba HTTP $expected pero la API respondió HTTP $code." >&2
        exit 1
    fi
}

echo "Tour guiado — ecert Document Review API ($BASE_URL)"
echo
echo -n "Esperando a que la API esté disponible"
for i in $(seq 1 30); do
    if curl -fsS "$BASE_URL/health" >/dev/null 2>&1; then
        echo " ✓"
        break
    fi
    if [ "$i" -eq 30 ]; then
        echo
        echo "ERROR: la API no responde en $BASE_URL/health." >&2
        echo "¿Está corriendo? Pruebe: docker compose up --build -d" >&2
        exit 1
    fi
    echo -n "."
    sleep 2
done

# ─── Parte 1: datos sembrados ───────────────────────────────────────────────

step "Documentos sembrados: el seeder deja documentos en distintas etapas del ciclo de vida"
call 200 GET "/api/documents"

step "Trazabilidad de un documento sembrado: el informe rechazado dos veces (2 versiones, 2 rechazos)"
REPORT_ID=$(extract '[.[] | select(.title=="Quarterly Report Q1")][0].id' \
    '[x for x in d if x["title"]=="Quarterly Report Q1"][0]["id"]')
if [ -n "$REPORT_ID" ] && [ "$REPORT_ID" != "null" ]; then
    call 200 GET "/api/documents/$REPORT_ID/history"
else
    echo "  (documento sembrado no encontrado; se omite este paso)"
fi

# ─── Parte 2: ciclo de vida completo en vivo ────────────────────────────────

step "Registro de un documento nuevo con su primera versión PDF (Created)"
TITLE="Contrato Demo $(date +%H%M%S)"
call 201 POST "/api/documents" \
    -F "Title=$TITLE" \
    -F "Type=Contract" \
    -F "UploadedBy=juan.author" \
    -F "File=@samples/contrato-v1.pdf;type=application/pdf"
DOC_ID=$(extract '.id' 'd["id"]')
PAGES=$(extract '.versions[0].pageCount' 'd["versions"][0]["pageCount"]')
echo
echo "  ★ Integración externa (PdfPig): la API validó el PDF y contó sus páginas → pageCount=$PAGES"

step "El autor envía el documento a revisión (Created → PendingReview)"
call 200 POST "/api/documents/$DOC_ID/status" \
    -H "Content-Type: application/json" \
    -d '{"targetStatus":"PendingReview","performedBy":"juan.author"}'

step "Una revisora toma el documento (PendingReview → UnderReview)"
call 200 POST "/api/documents/$DOC_ID/status" \
    -H "Content-Type: application/json" \
    -d '{"targetStatus":"UnderReview","performedBy":"maria.reviewer"}'

step "La revisora registra una observación (solicitud de corrección)"
call 201 POST "/api/documents/$DOC_ID/observations" \
    -H "Content-Type: application/json" \
    -d '{"type":"CorrectionRequest","content":"El plazo de la cláusula 2 no coincide con lo cotizado.","createdBy":"maria.reviewer"}'

step "La revisora rechaza el documento; el motivo es obligatorio y queda como observación (UnderReview → Rejected)"
call 200 POST "/api/documents/$DOC_ID/status" \
    -H "Content-Type: application/json" \
    -d '{"targetStatus":"Rejected","performedBy":"maria.reviewer","reason":"Corregir plazo y precio antes de reenviar."}'

step "El autor sube una versión corregida: el documento vuelve solo a la cola de revisión (Rejected → PendingReview)"
call 201 POST "/api/documents/$DOC_ID/versions" \
    -F "UploadedBy=juan.author" \
    -F "File=@samples/contrato-v2.pdf;type=application/pdf"

step "Segunda revisión (PendingReview → UnderReview)"
call 200 POST "/api/documents/$DOC_ID/status" \
    -H "Content-Type: application/json" \
    -d '{"targetStatus":"UnderReview","performedBy":"maria.reviewer"}'

step "La versión 2 se aprueba (UnderReview → Approved)"
call 200 POST "/api/documents/$DOC_ID/status" \
    -H "Content-Type: application/json" \
    -d '{"targetStatus":"Approved","performedBy":"maria.reviewer"}'

# ─── Parte 3: validaciones y trazabilidad ───────────────────────────────────

step "La máquina de estados protege el ciclo de vida: rechazar un documento aprobado devuelve 409"
call 409 POST "/api/documents/$DOC_ID/status" \
    -H "Content-Type: application/json" \
    -d '{"targetStatus":"Rejected","performedBy":"maria.reviewer","reason":"intento inválido"}'

step "Todas las observaciones del documento, en todas sus versiones"
call 200 GET "/api/documents/$DOC_ID/observations"

step "Trazabilidad completa: la auditoría cuenta la historia de principio a fin"
call 200 GET "/api/documents/$DOC_ID/history"

step "Estado final del documento con su historial de versiones"
call 200 GET "/api/documents/$DOC_ID"

echo
echo "════════════════════════════════════════════════════════════════"
echo "Tour completo ✓  ($STEP pasos)"
echo
echo "Para seguir explorando:"
echo "  • Docs interactivas (Scalar): $BASE_URL/scalar"
echo "  • Descargar el PDF vigente:   curl -OJ $BASE_URL/api/documents/$DOC_ID/file"
echo "  • Colección Postman:          Ecert.DocsReview.postman_collection.json"
echo "════════════════════════════════════════════════════════════════"
