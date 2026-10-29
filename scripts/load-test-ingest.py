#!/usr/bin/env python3
"""Burst ingest+evaluate load test against a local Aegis API (§43 baseline).

Prerequisites:
  - API running (Development) with AllowDevBootstrap
  - Raise the API rate limit for the run, e.g.:
      RateLimiting__Api__PermitLimit=100000

Usage:
  python3 scripts/load-test-ingest.py [--count 200] [--concurrency 20] [--api http://127.0.0.1:5092]
"""

from __future__ import annotations

import argparse
import json
import sys
import time
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor, as_completed
from datetime import datetime, timedelta, timezone
from uuid import uuid4


def http_json(method: str, url: str, body: dict | None = None, token: str | None = None) -> tuple[int, dict | None]:
    data = None if body is None else json.dumps(body).encode()
    headers = {"Content-Type": "application/json", "Accept": "application/json"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    def parse(raw: str) -> dict | None:
        raw = raw.strip()
        if not raw:
            return None
        try:
            return json.loads(raw)
        except json.JSONDecodeError:
            return {"raw": raw}

    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            return resp.status, parse(resp.read().decode())
    except urllib.error.HTTPError as e:
        return e.code, parse(e.read().decode())


def main() -> int:
    p = argparse.ArgumentParser(description="Aegis ingest+evaluate load baseline")
    p.add_argument("--api", default="http://127.0.0.1:5092")
    p.add_argument("--count", type=int, default=200)
    p.add_argument("--concurrency", type=int, default=20)
    args = p.parse_args()
    api = args.api.rstrip("/")

    code, _ = http_json("GET", f"{api}/health")
    if code != 200:
        print(f"Health check failed ({code}). Start the API first.", file=sys.stderr)
        return 1

    slug = f"load-{uuid4().hex[:10]}"
    email = f"admin@{slug}.test"
    password = "Passw0rd!"
    code, boot = http_json(
        "POST",
        f"{api}/api/v1/tenants",
        {
            "name": "Load Test Tenant",
            "slug": slug,
            "adminEmail": email,
            "adminName": "Load Admin",
            "adminPassword": password,
        },
    )
    if code not in (200, 201):
        print(f"Bootstrap failed: {code} {boot}", file=sys.stderr)
        return 1

    code, login = http_json(
        "POST",
        f"{api}/api/v1/auth/login",
        {"email": email, "password": password, "tenantSlug": slug},
    )
    if code != 200 or not login or "accessToken" not in login:
        print(f"Login failed: {code} {login}", file=sys.stderr)
        return 1
    token = login["accessToken"]

    code, customer = http_json(
        "POST",
        f"{api}/api/v1/customers",
        {"type": "INDIVIDUAL", "country": "KE", "firstName": "Load", "lastName": "Test"},
        token,
    )
    if code not in (200, 201) or not customer:
        print(f"Customer create failed: {code} {customer}", file=sys.stderr)
        return 1
    customer_id = customer["id"]

    code, account = http_json(
        "POST",
        f"{api}/api/v1/customers/{customer_id}/accounts",
        {"accountType": "MOBILE_WALLET", "currency": "KES"},
        token,
    )
    if code not in (200, 201) or not account:
        print(f"Account create failed: {code} {account}", file=sys.stderr)
        return 1
    account_id = account["id"]

    base_ts = datetime.now(timezone.utc) - timedelta(hours=2)

    def one(i: int) -> tuple[int, float]:
        payload = {
            "externalReference": f"LOAD-{slug}-{i}",
            "accountId": account_id,
            "customerId": customer_id,
            "amount": 1000 + (i % 50),
            "currency": "KES",
            "direction": "CREDIT",
            "transactionType": "TRANSFER",
            "channel": "MOBILE",
            "timestamp": (base_ts + timedelta(seconds=i)).strftime("%Y-%m-%dT%H:%M:%SZ"),
        }
        t0 = time.perf_counter()
        status, _ = http_json("POST", f"{api}/api/v1/transactions", payload, token)
        return status, time.perf_counter() - t0

    print(f"Burst: count={args.count} concurrency={args.concurrency} api={api} tenant={slug}")
    started = time.perf_counter()
    ok = 0
    fail = 0
    latencies: list[float] = []
    with ThreadPoolExecutor(max_workers=args.concurrency) as pool:
        futures = [pool.submit(one, i) for i in range(args.count)]
        for fut in as_completed(futures):
            status, elapsed = fut.result()
            latencies.append(elapsed)
            if status in (200, 201):
                ok += 1
            else:
                fail += 1
    wall = time.perf_counter() - started
    latencies.sort()
    p50 = latencies[len(latencies) // 2] if latencies else 0
    p95 = latencies[int(len(latencies) * 0.95)] if latencies else 0
    tps = ok / wall if wall > 0 else 0

    print("---")
    print(f"ok={ok} fail={fail} wall_sec={wall:.3f}")
    print(f"txs_per_sec={tps:.2f}")
    print(f"latency_p50_ms={p50 * 1000:.1f} latency_p95_ms={p95 * 1000:.1f}")
    if fail:
        print("Load test completed with failures (often rate limiting — raise RateLimiting__Api__PermitLimit).", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
