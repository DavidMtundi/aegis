#!/usr/bin/env bash
# Seed a local Development tenant for the Aegis supervisor demo.
# Prerequisites: API on AEGIS_API_BASE_URL (default http://127.0.0.1:5092), AllowDevBootstrap on.
set -euo pipefail

API="${AEGIS_API_BASE_URL:-http://127.0.0.1:5092}"
SLUG="${DEMO_TENANT_SLUG:-demo-$(date +%s | tail -c 7)}"
EMAIL="${DEMO_ADMIN_EMAIL:-admin@${SLUG}.test}"
PASSWORD="${DEMO_ADMIN_PASSWORD:-Passw0rd!}"
NAME="${DEMO_ADMIN_NAME:-Demo Admin}"

json() {
  python3 -c 'import json,sys; print(json.dumps(json.load(sys.stdin), indent=2))' 2>/dev/null || cat
}

py_get() {
  python3 -c "import json,sys; d=json.load(sys.stdin); print($1)"
}

echo "==> API: $API"
echo "==> Tenant slug: $SLUG"

if ! curl -sf "$API/health" >/dev/null; then
  echo "API health check failed. Start: ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Aegis.Api" >&2
  exit 1
fi

BOOT=$(curl -sf -X POST "$API/api/v1/tenants" \
  -H 'Content-Type: application/json' \
  -d "$(python3 - <<PY
import json
print(json.dumps({
  "name": "Demo Tenant",
  "slug": "$SLUG",
  "adminEmail": "$EMAIL",
  "adminName": "$NAME",
  "adminPassword": "$PASSWORD",
}))
PY
)") || {
  echo "Bootstrap failed (is Development bootstrap enabled?). Response may be 404 outside Development." >&2
  exit 1
}

TOKEN=$(curl -sf -X POST "$API/api/v1/auth/login" \
  -H 'Content-Type: application/json' \
  -d "$(python3 - <<PY
import json
print(json.dumps({
  "email": "$EMAIL",
  "password": "$PASSWORD",
  "tenantSlug": "$SLUG",
}))
PY
)" | py_get 'd["accessToken"]')

AUTH=( -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' )

CUSTOMER=$(curl -sf -X POST "$API/api/v1/customers" "${AUTH[@]}" \
  -d '{"type":"INDIVIDUAL","country":"KE","firstName":"Amina","lastName":"Otieno"}')
CUSTOMER_ID=$(printf '%s' "$CUSTOMER" | py_get 'd["id"]')

ACCOUNT=$(curl -sf -X POST "$API/api/v1/customers/${CUSTOMER_ID}/accounts" "${AUTH[@]}" \
  -d '{"accountType":"MOBILE_WALLET","currency":"KES"}')
ACCOUNT_ID=$(printf '%s' "$ACCOUNT" | py_get 'd["id"]')

BASE_TS=$(python3 - <<'PY'
from datetime import datetime, timezone, timedelta
print((datetime.now(timezone.utc) - timedelta(hours=1)).strftime("%Y-%m-%dT%H:%M:%SZ"))
PY
)

ALERT_IDS=()
echo "==> Ingesting structuring pattern (5 × 95_000 KES)…"
for i in 0 1 2 3 4; do
  REF="TX-DEMO-S-${SLUG}-$i"
  TS=$(python3 - <<PY
from datetime import datetime, timedelta
base = datetime.fromisoformat("${BASE_TS}".replace("Z", "+00:00"))
print((base + timedelta(minutes=$i)).strftime("%Y-%m-%dT%H:%M:%SZ"))
PY
)
  BODY=$(python3 - <<PY
import json
print(json.dumps({
  "externalReference": "$REF",
  "accountId": "$ACCOUNT_ID",
  "customerId": "$CUSTOMER_ID",
  "amount": 95000,
  "currency": "KES",
  "direction": "CREDIT",
  "transactionType": "TRANSFER",
  "channel": "MOBILE",
  "timestamp": "$TS",
}))
PY
)
  RESP=$(curl -sf -X POST "$API/api/v1/transactions" "${AUTH[@]}" -d "$BODY")
  IDS=$(printf '%s' "$RESP" | py_get '",".join(d.get("alertIds") or [])')
  if [[ -n "$IDS" ]]; then
    IFS=',' read -ra PARTS <<< "$IDS"
    for a in "${PARTS[@]}"; do
      [[ -n "$a" ]] && ALERT_IDS+=("$a")
    done
  fi
done

echo "==> Optional high-risk geography transaction…"
GEO_REF="TX-DEMO-G-${SLUG}"
GEO_RESP=$(curl -sf -X POST "$API/api/v1/transactions" "${AUTH[@]}" \
  -d "$(python3 - <<PY
import json
from datetime import datetime, timezone, timedelta
ts = (datetime.now(timezone.utc) - timedelta(minutes=2)).strftime("%Y-%m-%dT%H:%M:%SZ")
print(json.dumps({
  "externalReference": "$GEO_REF",
  "accountId": "$ACCOUNT_ID",
  "customerId": "$CUSTOMER_ID",
  "amount": 15000,
  "currency": "KES",
  "direction": "CREDIT",
  "transactionType": "TRANSFER",
  "channel": "MOBILE",
  "timestamp": ts,
  "counterpartyCountry": "KP",
}))
PY
)")
GEO_ALERT=$(printf '%s' "$GEO_RESP" | py_get '",".join(d.get("alertIds") or [])')
if [[ -n "$GEO_ALERT" ]]; then
  IFS=',' read -ra PARTS <<< "$GEO_ALERT"
  for a in "${PARTS[@]}"; do
    [[ -n "$a" ]] && ALERT_IDS+=("$a")
  done
fi

# unique alert ids
UNIQUE_ALERTS=$(printf '%s\n' "${ALERT_IDS[@]:-}" | awk 'NF' | sort -u)

cat <<EOF

========================================
 Aegis demo seed complete
========================================
API:           $API
Tenant slug:   $SLUG
Email:         $EMAIL
Password:      $PASSWORD
Customer id:   $CUSTOMER_ID
Account id:    $ACCOUNT_ID

Console:       http://localhost:3000/login  (or :3010 if 3000 is taken)
  slug / email / password as above
  → Alerts → open structuring alert → Create case → Close case
  → Audit for CASE_* events

Alert ids:
$(printf '%s\n' "$UNIQUE_ALERTS" | sed 's/^/  /')

Walkthrough: docs/runbooks/demo-walkthrough.md
========================================
EOF
