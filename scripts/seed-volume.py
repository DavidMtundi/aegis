#!/usr/bin/env python3
"""Fill a local Development tenant with realistic volume for demos and manual testing.

Everything goes through the public API, so alerts come from the real rules:
team users, customers and accounts, ~60 days of background transactions with
structuring, rapid-movement and high-risk-geography patterns mixed in, then
analyst work on the resulting alerts (assign, dismiss, resolve, escalate,
cases with notes, links and closures).

--backdate (local Docker Postgres only) moves alert, case, audit and ingest
timestamps back to when the underlying activity happened, so the dashboard's
age, SLA and trend charts look like a team that has been working for weeks.

Usage:
  python3 scripts/seed-volume.py --slug smoke-6121 --email admin@smoke-6121.test --backdate
"""
import argparse
import json
import random
import subprocess
import sys
import time
import urllib.error
import urllib.request
from datetime import datetime, timedelta, timezone

FIRST = ["Amina", "Brian", "Cynthia", "Daniel", "Esther", "Felix", "Grace", "Hassan", "Irene", "James",
         "Kevin", "Lilian", "Mercy", "Nelson", "Faith", "Peter", "Rose", "Samuel", "Tabitha", "Victor",
         "Wanjiru", "Yusuf", "Zawadi", "Achieng", "Baraka", "Chebet", "Dennis", "Halima", "Juma", "Kamau",
         "Mwangi", "Njeri", "Omondi", "Wafula", "Akinyi", "Kipchoge", "Nafula", "Otieno", "Mutua", "Auma"]
LAST = ["Otieno", "Kamau", "Wanjiku", "Mwangi", "Ochieng", "Njoroge", "Kiprotich", "Achieng", "Mutua", "Kariuki",
        "Omondi", "Chebet", "Wambui", "Kiplagat", "Nyambura", "Odhiambo", "Gitau", "Korir", "Musyoka", "Ndungu",
        "Hassan", "Abdi", "Mohamed", "Okello", "Nakato", "Mugisha", "Uwase", "Mrisho", "Juma", "Wekesa"]
BUSINESS = ["Savanna Agro Traders", "Lakeview Hardware", "Kilimani Pharmacy", "Rift Valley Dairies", "Mombasa Freight Co",
            "Nyeri Coffee Cooperative", "Eastlands Electronics", "Highway Petrol Station", "Jua Kali Metalworks",
            "Coastline Fisheries", "Tusker Logistics", "Baraka Wholesalers", "Umoja Supermarket", "Kisumu Boda Sacco",
            "Green Valley Horticulture", "Machakos Builders", "Pwani Tours & Travel", "Nairobi Gadget Hub",
            "Sunrise Microfinance Agents", "Kericho Tea Brokers", "Thika Textiles", "Mama Mboga Collective",
            "Lamu Dhow Exports", "Naivasha Flower Farm", "Westlands Forex Bureau", "Eldoret Grain Millers",
            "Garissa Livestock Traders", "Malindi Beach Resort", "Kitale Seed Suppliers", "Embu Quarry Works"]
COUNTRIES = [("KE", "KES")] * 16 + [("UG", "UGX"), ("TZ", "TZS"), ("RW", "RWF"), ("NG", "NGN")]
BENIGN_FOREIGN = ["UG", "TZ", "AE", "CN", "GB", "US", "IN", "ZA", "RW"]
HIGH_RISK = ["KP", "IR", "SY"]
DISMISS_REASONS = [
    "Salary advance repayments from employer; matches payroll pattern on file.",
    "Chama contributions collected by group treasurer; group registration verified.",
    "Customer confirmed school fees split across siblings; receipts provided.",
    "Seasonal harvest proceeds from cooperative; consistent with prior years.",
    "Known supplier payments; invoices reviewed and match amounts.",
    "Counterparty is a licensed remittance partner; purpose documented.",
    "Duplicate of an alert already worked in a case.",
]
NOTES = [
    "Requested source-of-funds documentation from the customer via branch.",
    "Reviewed the last 90 days of activity; pattern started after account upgrade.",
    "Customer called back: says deposits are from a family business. Asked for business permit.",
    "Counterparty accounts appear in two other open cases. Linking for context.",
    "KYC refreshed; ID and KRA PIN verified. Occupation listed as trader.",
    "Funds moved out to three wallets within 40 minutes. Typical layering signature.",
    "Escalating for MLRO review given the amounts and the jurisdiction involved.",
    "No adverse media found. Waiting on bank statement from the customer.",
]
CONCLUSIONS = {
    "FALSE_POSITIVE": "Activity explained by documented business income. No further action.",
    "NO_SUSPICIOUS_ACTIVITY": "Reviewed transactions and KYC; activity consistent with customer profile.",
    "SUSPICIOUS_ACTIVITY": "Structured deposits with rapid onward transfers and no plausible source of funds.",
    "REPORTED": "Suspicious transaction report filed with the FRC. Account placed under enhanced monitoring.",
}
TEAM = [
    ("Grace Wanjiku", "grace", ["Analyst"]),
    ("Brian Otieno", "brian", ["Analyst"]),
    ("Halima Abdi", "halima", ["Analyst"]),
    ("Samuel Kiprotich", "samuel", ["Reviewer"]),
    ("Esther Nyambura", "esther", ["Reviewer"]),
    ("Daniel Mutua", "daniel", ["Viewer"]),
]


class Api:
    def __init__(self, base):
        self.base = base.rstrip("/")
        self.token = None

    def call(self, method, path, body=None, token=None, ok=(200, 201)):
        data = json.dumps(body).encode() if body is not None else None
        req = urllib.request.Request(self.base + path, data=data, method=method)
        req.add_header("Content-Type", "application/json")
        if token or self.token:
            req.add_header("Authorization", f"Bearer {token or self.token}")
        try:
            with urllib.request.urlopen(req, timeout=300) as resp:
                raw = resp.read()
                return resp.status, (json.loads(raw) if raw else None)
        except urllib.error.HTTPError as e:
            text = e.read().decode(errors="replace")
            if e.code in ok:
                return e.code, None
            return e.code, text

    def login(self, slug, email, password):
        for _ in range(12):
            status, body = self.call("POST", "/api/v1/auth/login",
                                     {"tenantSlug": slug, "email": email, "password": password})
            if status != 429:
                break
            time.sleep(10)
        if status != 200:
            raise SystemExit(f"Login failed for {email}: {status} {body}")
        return body["accessToken"]


def iso(ts):
    return ts.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--api", default="http://127.0.0.1:5092")
    p.add_argument("--slug", required=True)
    p.add_argument("--email", required=True)
    p.add_argument("--password", default="Passw0rd!")
    p.add_argument("--customers", type=int, default=300)
    p.add_argument("--days", type=int, default=60)
    p.add_argument("--seed", type=int, default=42)
    p.add_argument("--tag", default=None, help="Reference prefix; defaults to a timestamp so reruns add data")
    p.add_argument("--backdate", action="store_true")
    p.add_argument("--pg-container", default="aegis-postgres-1")
    args = p.parse_args()

    rnd = random.Random(args.seed)
    tag = args.tag or "SEED" + datetime.now().strftime("%m%d%H%M")
    now = datetime.now(timezone.utc).replace(microsecond=0)
    start = now - timedelta(days=args.days)
    api = Api(args.api)
    api.token = api.login(args.slug, args.email, args.password)
    print(f"Seeding tenant {args.slug} with tag {tag}")

    # Team
    users = {}
    status, existing = api.call("GET", "/api/v1/users")
    by_email = {u["email"].lower(): u for u in existing} if status == 200 else {}
    for name, handle, roles in TEAM:
        email = f"{handle}@{args.slug}.test"
        user = by_email.get(email)
        if not user:
            status, user = api.call("POST", "/api/v1/users",
                                    {"email": email, "name": name, "password": args.password, "roles": roles})
            if status not in (200, 201):
                raise SystemExit(f"Create user {email}: {status} {user}")
        users[handle] = {"id": user["id"], "email": email, "roles": roles,
                         "token": api.login(args.slug, email, args.password)}
    analysts = [u for u in users.values() if "Analyst" in u["roles"]]
    reviewers = [u for u in users.values() if "Reviewer" in u["roles"]]
    print(f"  team: {len(users)} users")

    # Customers and accounts
    customers = []
    n_business = args.customers // 5
    for i in range(args.customers):
        business = i < n_business
        country, local_ccy = rnd.choice(COUNTRIES)
        body = {"type": "BUSINESS" if business else "INDIVIDUAL", "country": country,
                "externalReference": f"{tag}-C{i:04d}"}
        if business:
            body["legalName"] = f"{rnd.choice(BUSINESS)} {'Ltd' if rnd.random() < 0.6 else 'Enterprises'}"
        else:
            body["firstName"], body["lastName"] = rnd.choice(FIRST), rnd.choice(LAST)
        status, c = api.call("POST", "/api/v1/customers", body)
        if status not in (200, 201):
            raise SystemExit(f"Create customer: {status} {c}")
        types = (["BANK_ACCOUNT", "MERCHANT_ACCOUNT"] if business else ["MOBILE_WALLET", "BANK_ACCOUNT"]) + \
                rnd.sample(["SACCO_ACCOUNT", "LOAN_ACCOUNT", "MOBILE_WALLET", "OTHER"], rnd.randint(0, 1))
        accounts = []
        for j, acct_type in enumerate(types[: rnd.randint(1, 3)]):
            ccy = local_ccy if j == 0 or rnd.random() < 0.8 else rnd.choice(["USD", "KES"])
            status, a = api.call("POST", f"/api/v1/customers/{c['id']}/accounts",
                                 {"accountType": acct_type, "currency": ccy,
                                  "externalReference": f"{tag}-A{i:04d}-{j}"})
            if status not in (200, 201):
                raise SystemExit(f"Create account: {status} {a}")
            accounts.append({"id": a["id"], "currency": ccy, "type": acct_type})
        customers.append({"id": c["id"], "business": business, "accounts": accounts})
        if (i + 1) % 50 == 0:
            print(f"  customers: {i + 1}/{args.customers}")

    # Transactions
    txs = []

    def tx(cust, acct, amount, direction, ts, ttype="TRANSFER", channel="MOBILE", cp=None):
        if ts > now - timedelta(minutes=1):
            return
        txs.append({"externalReference": f"{tag}-T{len(txs):06d}", "accountId": acct["id"], "customerId": cust["id"],
                    "amount": round(amount, 2), "currency": acct["currency"], "direction": direction,
                    "transactionType": ttype, "channel": channel, "timestamp": iso(ts),
                    **({"counterpartyCountry": cp} if cp else {})})

    def at(day, hour_lo=7, hour_hi=21):
        return start + timedelta(days=day, hours=rnd.randint(hour_lo, hour_hi), minutes=rnd.randint(0, 59),
                                 seconds=rnd.randint(0, 59))

    for cust in customers:
        main_acct = cust["accounts"][0]
        for day in range(args.days):
            if cust["business"]:
                for _ in range(rnd.choice([0, 1, 2, 2, 3, 3, 4])):
                    tx(cust, main_acct, rnd.uniform(500, 35000), "CREDIT", at(day), "MOBILE",
                       rnd.choice(["MOBILE", "MOBILE", "ONLINE", "BRANCH"]))
                if rnd.random() < 0.35:
                    tx(cust, main_acct, rnd.uniform(5000, 80000), "DEBIT", at(day, 9, 17), "EFT",
                       "ONLINE", rnd.choice(BENIGN_FOREIGN) if rnd.random() < 0.15 else None)
            else:
                if day % 30 == 27:
                    tx(cust, main_acct, rnd.uniform(25000, 180000), "CREDIT", at(day, 6, 8), "EFT", "BULK")
                for _ in range(rnd.choice([0, 0, 1, 1, 1, 2])):
                    tx(cust, rnd.choice(cust["accounts"]), rnd.uniform(50, 6000), rnd.choice(["DEBIT", "DEBIT", "CREDIT"]),
                       at(day), rnd.choice(["TRANSFER", "MOBILE", "CASH_WITHDRAWAL"]),
                       rnd.choice(["MOBILE", "MOBILE", "BRANCH", "ONLINE", "ATM"]))
                if rnd.random() < 0.02:
                    tx(cust, main_acct, rnd.uniform(2000, 40000), "CREDIT", at(day), "WIRE", "ONLINE",
                       rnd.choice(BENIGN_FOREIGN))

    kes_customers = [c for c in customers if c["accounts"][0]["currency"] == "KES"]
    suspects = rnd.sample(kes_customers, min(len(kes_customers), 60))
    repeaters = suspects[:12]

    def pattern_day():
        if rnd.random() < 0.35:
            return rnd.randint(max(0, args.days - 7), args.days - 1)
        return rnd.randint(0, args.days - 1)

    def structuring(cust, near_miss=False):
        acct, day = cust["accounts"][0], pattern_day()
        t = at(day, 8, 12)
        for _ in range(4 if near_miss else rnd.randint(5, 7)):
            tx(cust, acct, rnd.choice([95000, 98000, 99000, 97500, rnd.uniform(90000, 99500)]), "CREDIT", t,
               "CASH_DEPOSIT", rnd.choice(["BRANCH", "MOBILE"]))
            t += timedelta(minutes=rnd.randint(10, 70))

    def rapid(cust):
        acct, day = cust["accounts"][0], pattern_day()
        t, amount = at(day, 9, 18), rnd.uniform(60000, 450000)
        tx(cust, acct, amount, "CREDIT", t, "TRANSFER", "ONLINE")
        out, parts = amount * rnd.uniform(0.92, 0.99), rnd.randint(1, 3)
        for _ in range(parts):
            t += timedelta(minutes=rnd.randint(5, 18))
            tx(cust, acct, out / parts, "DEBIT", t, "TRANSFER", "MOBILE")

    def geography(cust):
        acct, day = cust["accounts"][0], pattern_day()
        tx(cust, acct, rnd.uniform(12000, 600000), rnd.choice(["CREDIT", "DEBIT"]), at(day), "WIRE",
           "ONLINE", rnd.choice(HIGH_RISK))

    for i, cust in enumerate(suspects):
        [structuring, rapid, geography][i % 3](cust)
    for cust in repeaters:
        for fn in rnd.sample([structuring, rapid, geography], 2):
            fn(cust)
    for cust in rnd.sample(kes_customers, 10):
        structuring(cust, near_miss=True)

    txs.sort(key=lambda t: t["timestamp"])
    print(f"  ingesting {len(txs)} transactions")
    alert_ts = {}
    for i in range(0, len(txs), 1000):
        chunk = txs[i:i + 1000]
        status, res = api.call("POST", "/api/v1/transactions/batch", {"transactions": chunk})
        if status != 200:
            raise SystemExit(f"Batch failed: {status} {res}")
        for row in res["results"]:
            for aid in row.get("alertIds") or []:
                alert_ts.setdefault(aid, chunk[row["row"] - 1]["timestamp"])
        print(f"    {min(i + 1000, len(txs))}/{len(txs)}  failed={res['failed']}  alerts so far={len(alert_ts)}")

    # Analyst work, oldest first
    def act(user, method, path, body=None):
        status, res = api.call(method, path, body, token=user["token"])
        if status == 403:
            status, res = api.call(method, path, body)
        return status, res

    alerts = []
    for aid, ts in alert_ts.items():
        status, a = api.call("GET", f"/api/v1/alerts/{aid}")
        if status == 200:
            alerts.append({"id": aid, "ts": ts, "customer": a.get("focusEntityId"), "status": a.get("status")})
    alerts.sort(key=lambda a: a["ts"])
    handled, cases, closed_alerts = set(), [], []
    for a in alerts:
        if a["id"] in handled or a["status"] not in ("OPEN", "IN_REVIEW", "ASSIGNED"):
            continue
        age = (now - datetime.fromisoformat(a["ts"].replace("Z", "+00:00"))).days
        analyst = rnd.choice(analysts)
        roll = rnd.random()
        if age < 3:
            plan = "case" if roll < 0.15 else "assign" if roll < 0.6 else "open"
        elif age <= 14:
            plan = ("dismiss" if roll < 0.3 else "resolve" if roll < 0.4 else "escalate" if roll < 0.5
                    else "case" if roll < 0.8 else "assign")
        else:
            plan = ("dismiss" if roll < 0.45 else "resolve" if roll < 0.6 else "case" if roll < 0.92 else "open")
        handled.add(a["id"])
        if plan in ("assign", "dismiss", "resolve", "escalate", "case"):
            act(analyst, "POST", f"/api/v1/alerts/{a['id']}/assign", {"assignedTo": analyst["id"]})
        if plan == "dismiss":
            act(analyst, "POST", f"/api/v1/alerts/{a['id']}/dismiss", {"reason": rnd.choice(DISMISS_REASONS)})
            closed_alerts.append(a["id"])
        elif plan == "resolve":
            act(analyst, "POST", f"/api/v1/alerts/{a['id']}/resolve")
            closed_alerts.append(a["id"])
        elif plan == "escalate":
            act(analyst, "POST", f"/api/v1/alerts/{a['id']}/escalate",
                {"reason": "Amounts and counterparties need reviewer sign-off."})
        elif plan == "case":
            status, c = act(analyst, "POST", f"/api/v1/alerts/{a['id']}/create-case")
            if status not in (200, 201) or not isinstance(c, dict):
                continue
            case = {"id": c["id"], "ts": a["ts"], "age": age, "alerts": [a["id"]]}
            for other in alerts:
                if other["customer"] == a["customer"] and other["id"] not in handled:
                    if act(analyst, "POST", f"/api/v1/cases/{c['id']}/alerts", {"alertId": other["id"]})[0] in (200, 201):
                        handled.add(other["id"])
                        case["alerts"].append(other["id"])
            act(analyst, "POST", f"/api/v1/cases/{c['id']}/assign", {"assignedTo": analyst["id"]})
            for note in rnd.sample(NOTES, rnd.randint(1, 3)):
                act(analyst, "POST", f"/api/v1/cases/{c['id']}/notes", {"text": note})
            reviewer = rnd.choice(reviewers)
            if age > 14 and rnd.random() < 0.85:
                disposition = rnd.choices(list(CONCLUSIONS), weights=[35, 30, 20, 15])[0]
                if disposition in ("SUSPICIOUS_ACTIVITY", "REPORTED"):
                    act(analyst, "POST", f"/api/v1/cases/{c['id']}/escalate", {"reason": "Needs MLRO decision."})
                act(reviewer, "POST", f"/api/v1/cases/{c['id']}/close",
                    {"disposition": disposition, "conclusion": CONCLUSIONS[disposition]})
                case["closed"] = True
                for aid in case["alerts"]:
                    act(analyst, "POST", f"/api/v1/alerts/{aid}/resolve")
            elif rnd.random() < 0.3:
                act(analyst, "POST", f"/api/v1/cases/{c['id']}/escalate", {"reason": "Pattern spans several accounts."})
            cases.append(case)
    print(f"  alerts: {len(alerts)}  cases: {len(cases)}")

    api.call("POST", "/api/v1/risk/recalculate-all")

    if args.backdate:
        backdate(args, tag, alerts, cases, rnd, now)
    print(f"Done. Sign in at http://localhost:3000/login with slug {args.slug}.")
    print(f"Team logins use the same password: " + ", ".join(u["email"] for u in users.values()))


def backdate(args, tag, alerts, cases, rnd, now):
    stmts = []
    q = lambda s: "'" + s.replace("'", "''") + "'"
    cap = now - timedelta(minutes=5)
    stmts.append(f"""UPDATE transactions.transactions SET "CreatedAt" = "Timestamp" + interval '20 seconds',
        "UpdatedAt" = "Timestamp" + interval '20 seconds' WHERE "ExternalReference" LIKE {q(tag + '-T%')};""")
    stmts.append(f"""UPDATE audit.audit_events e SET "OccurredAt" = t."CreatedAt" FROM transactions.transactions t
        WHERE t."ExternalReference" LIKE {q(tag + '-T%')} AND e."EntityId" = t."Id"::text;""")
    stmts.append(f"""UPDATE customers.customers SET "CreatedAt" = now() - (random() * interval '700 days' + interval '{args.days} days')
        WHERE "ExternalReference" LIKE {q(tag + '-C%')};""")
    stmts.append(f"""UPDATE customers.accounts a SET "OpenedAt" = c."CreatedAt", "CreatedAt" = c."CreatedAt"
        FROM customers.customers c WHERE a.customer_id = c."Id" AND c."ExternalReference" LIKE {q(tag + '-C%')};""")

    def shift_audit(entity_id, base, step_hours):
        return (f"""UPDATE audit.audit_events e SET "OccurredAt" = LEAST({q(iso(cap))}::timestamptz,
            {q(iso(base))}::timestamptz + (r.n - 1) * interval '{step_hours} hours')
            FROM (SELECT "Id", row_number() OVER (ORDER BY "OccurredAt") n FROM audit.audit_events
                  WHERE "EntityId" = {q(entity_id)}) r WHERE e."Id" = r."Id";""")

    for a in alerts:
        ts = datetime.fromisoformat(a["ts"].replace("Z", "+00:00")) + timedelta(seconds=30)
        resolved = min(cap, ts + timedelta(hours=rnd.randint(3, 96)))
        stmts.append(f"""UPDATE alerts.alerts SET "TriggeredAt" = {q(iso(ts))}, "CreatedAt" = {q(iso(ts))},
            "ResolvedAt" = CASE WHEN "ResolvedAt" IS NULL THEN NULL ELSE {q(iso(resolved))}::timestamptz END,
            "UpdatedAt" = COALESCE(CASE WHEN "ResolvedAt" IS NULL THEN NULL ELSE {q(iso(resolved))}::timestamptz END,
                                    {q(iso(ts))}::timestamptz) WHERE "Id" = {q(a['id'])};""")
        stmts.append(shift_audit(a["id"], ts, max(1, int((resolved - ts).total_seconds() // 3600 // 3))))
    for c in cases:
        opened = min(cap, datetime.fromisoformat(c["ts"].replace("Z", "+00:00")) + timedelta(hours=rnd.randint(1, 10)))
        closed = min(cap, opened + timedelta(days=rnd.randint(2, 12), hours=rnd.randint(0, 8)))
        stmts.append(f"""UPDATE cases.cases SET "OpenedAt" = {q(iso(opened))}, "CreatedAt" = {q(iso(opened))},
            "ClosedAt" = CASE WHEN "ClosedAt" IS NULL THEN NULL ELSE {q(iso(closed))}::timestamptz END,
            "UpdatedAt" = COALESCE(CASE WHEN "ClosedAt" IS NULL THEN NULL ELSE {q(iso(closed))}::timestamptz END,
                                    {q(iso(opened))}::timestamptz) WHERE "Id" = {q(c['id'])};""")
        end = closed if c.get("closed") else min(cap, opened + timedelta(days=max(1, min(c["age"], 10))))
        stmts.append(shift_audit(c["id"], opened, max(1, int((end - opened).total_seconds() // 3600 // 6))))

    sql = "BEGIN;\n" + "\n".join(stmts) + "\nCOMMIT;\n"
    result = subprocess.run(["docker", "exec", "-i", args.pg_container, "psql", "-U", "aegis", "-d", "aegis",
                             "-v", "ON_ERROR_STOP=1", "-q"], input=sql, text=True, capture_output=True)
    if result.returncode != 0:
        sys.exit(f"Backdate failed: {result.stderr[-2000:]}")
    print(f"  backdated {len(alerts)} alerts, {len(cases)} cases, transactions and customers")


if __name__ == "__main__":
    main()
