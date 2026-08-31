#!/usr/bin/env python3
"""Chroma Drop — 랭킹판에 테스트용 더미 기록을 넣는다 (개발 전용).

  python3 Tools/seed-dummy-scores.py <서비스계정키.json> --count 100
  python3 Tools/seed-dummy-scores.py <서비스계정키.json> --delete       # 더미만 삭제

서비스 계정 키는 보안 규칙을 우회하는 관리자 권한이다. 커밋하지 말고 클라이언트에 넣지 말 것.
더미 문서 ID 는 전부 'dummy_' 로 시작하므로 --delete 로 안전하게 걷어낼 수 있다.
HTTPS 는 curl 로 보낸다 (사내 TLS 프록시 때문에 python ssl 이 막힘).
"""
import argparse, base64, json, os, random, subprocess, sys, tempfile, time, urllib.parse

BOARDS = ["score_easy", "score_normal", "score_hard", "ta"]
COUNTRIES = ["KR", "JP", "US", "CN", "TW", "DE", "FR", "GB", "BR", "IN",
             "VN", "TH", "ID", "CA", "AU", "ES", "IT", "MX", "PL", "SE"]
NAMES = ["Chroma", "Drop", "Neon", "Pixel", "Blitz", "Echo", "Vortex", "Prism",
         "Quartz", "Nova", "Zephyr", "Onyx", "Flux", "Rune", "Halo", "Cobalt"]

def b64(b): return base64.urlsafe_b64encode(b).rstrip(b"=")

def curl(args, data=None, cfg=None):
    cmd = ["curl", "-s", "--max-time", "25"] + (["-K", cfg] if cfg else []) + args
    return subprocess.run(cmd, input=data, capture_output=True).stdout.decode(errors="replace")

def get_token(key_path):
    sa = json.load(open(key_path))
    now = int(time.time())
    claim = {"iss": sa["client_email"], "scope": "https://www.googleapis.com/auth/datastore",
             "aud": sa["token_uri"], "iat": now, "exp": now + 3600}
    si = b64(json.dumps({"alg": "RS256", "typ": "JWT"}).encode()) + b"." + b64(json.dumps(claim).encode())
    fd, pem = tempfile.mkstemp(suffix=".pem"); os.close(fd)
    open(pem, "w").write(sa["private_key"])
    try:
        sig = subprocess.run(["openssl", "dgst", "-sha256", "-sign", pem],
                             input=si, capture_output=True, check=True).stdout
    finally:
        os.unlink(pem)
    body = urllib.parse.urlencode({"grant_type": "urn:ietf:params:oauth:grant-type:jwt-bearer",
                                   "assertion": (si + b"." + b64(sig)).decode()})
    d = json.loads(curl([sa["token_uri"], "-X", "POST",
                         "-H", "Content-Type: application/x-www-form-urlencoded",
                         "--data-binary", "@-"], data=body.encode()))
    if "access_token" not in d:
        sys.exit("토큰 발급 실패")
    return d["access_token"], sa["project_id"]

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("key"); ap.add_argument("--count", type=int, default=100)
    ap.add_argument("--delete", action="store_true")
    ap.add_argument("--seed", type=int, default=7)
    a = ap.parse_args()

    tok, proj = get_token(a.key)
    fd, cfg = tempfile.mkstemp(suffix=".curlrc"); os.close(fd); os.chmod(cfg, 0o600)
    open(cfg, "w").write('header = "Authorization: Bearer %s"\n' % tok)
    base = f"https://firestore.googleapis.com/v1/projects/{proj}/databases/(default)/documents"

    try:
        if a.delete:
            total = 0
            for b in BOARDS:
                col = "boards_" + b
                q = {"structuredQuery": {"from": [{"collectionId": col}], "limit": 500}}
                out = curl([base + ":runQuery", "-X", "POST", "-H", "Content-Type: application/json",
                            "--data-binary", "@-"], data=json.dumps(q).encode(), cfg=cfg)
                try: rows = json.loads(out)
                except Exception: rows = []
                dels = []
                for r in rows:
                    doc = r.get("document") if isinstance(r, dict) else None
                    if not doc: continue
                    if doc["name"].rsplit("/", 1)[-1].startswith("dummy_"):
                        dels.append({"delete": doc["name"]})
                n = 0
                for i in range(0, len(dels), 400):
                    curl([base + ":commit", "-X", "POST", "-H", "Content-Type: application/json",
                          "--data-binary", "@-"],
                         data=json.dumps({"writes": dels[i:i+400]}).encode(), cfg=cfg)
                    n += len(dels[i:i+400])
                print(f"  {col}: 더미 {n}건 삭제")
                total += n
            print(f"\n총 {total}건 삭제")
            return

        rng = random.Random(a.seed)
        now = int(time.time() * 1000)
        docs = f"projects/{proj}/databases/(default)/documents"
        writes, per = [], {}
        for i in range(a.count):
            board = BOARDS[i % len(BOARDS)]
            col = "boards_" + board
            fields = {
                "name":    {"stringValue": f"{rng.choice(NAMES)}{rng.randint(10,999)}"},
                "country": {"stringValue": rng.choice(COUNTRIES)},
                "score":   {"integerValue": str(rng.randint(800, 45000))},
                "diff":    {"stringValue": board.replace("score_", "") if board != "ta" else "ta"},
                "seed":    {"integerValue": str(rng.randint(1, 10**6))},
                "updated": {"integerValue": str(now - rng.randint(0, 30*86400*1000))}}
            writes.append({"update": {"name": f"{docs}/{col}/dummy_{i:04d}", "fields": fields},
                           "updateMask": {"fieldPaths": list(fields)}})
            per[col] = per.get(col, 0) + 1

        # :commit 은 한 번에 최대 500건. 순차 PATCH 100번보다 훨씬 빠르다.
        done = 0
        for i in range(0, len(writes), 400):
            chunk = writes[i:i+400]
            out = curl([base + ":commit", "-X", "POST", "-H", "Content-Type: application/json",
                        "--data-binary", "@-"], data=json.dumps({"writes": chunk}).encode(), cfg=cfg)
            r = json.loads(out) if out.strip().startswith("{") else {}
            if "writeResults" not in r:
                print("배치 실패:", out[:300]); sys.exit(1)
            done += len(r["writeResults"])
            print(f"  배치 {i//400+1}: {len(chunk)}건 커밋")
        print()
        for k, v in sorted(per.items()): print(f"  {k}: {v}건")
        print(f"\n총 {done}건 투입 완료")
    finally:
        os.unlink(cfg)

if __name__ == "__main__":
    main()
