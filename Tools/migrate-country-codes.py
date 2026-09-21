#!/usr/bin/env python3
"""Chroma Drop — 랭킹 기록의 2자리 국가 코드(KR)를 3자리(KOR)로 바꾼다.

  python3 Tools/migrate-country-codes.py <서비스계정키.json>            # 미리보기 (쓰지 않음)
  python3 Tools/migrate-country-codes.py <서비스계정키.json> --apply    # 옛 값을 백업한 뒤 실제로 바꾼다

게임 클라이언트의 웹 API 키로는 안 된다 — 보안 규칙이 자기 문서 쓰기만 허용한다.
서비스 계정 키는 규칙을 우회하는 관리자 권한이다. 커밋하지 말고 Assets/ 안에 두지 말 것.
변환표는 Tools/country-codes.json. 표에 없는 코드는 건드리지 않고 알려 준다.
"""
import argparse, importlib.util, json, os, sys, tempfile, time

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("seed", os.path.join(HERE, "seed-dummy-scores.py"))
seed = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(seed)   # 토큰 발급·curl·판 목록을 그대로 쓴다

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("key")
    ap.add_argument("--apply", action="store_true")
    a = ap.parse_args()
    table = json.load(open(os.path.join(HERE, "country-codes.json")))

    tok, proj = seed.get_token(a.key)
    fd, cfg = tempfile.mkstemp(suffix=".curlrc"); os.close(fd); os.chmod(cfg, 0o600)
    open(cfg, "w").write('header = "Authorization: Bearer %s"\n' % tok)
    base = f"https://firestore.googleapis.com/v1/projects/{proj}/databases/(default)/documents"

    try:
        writes, backup, unknown = [], [], {}
        for b in seed.BOARDS:
            q = {"structuredQuery": {"from": [{"collectionId": "boards_" + b}]}}
            out = seed.curl([base + ":runQuery", "-X", "POST", "-H", "Content-Type: application/json",
                             "--data-binary", "@-"], data=json.dumps(q).encode(), cfg=cfg)
            if not out.strip().startswith("["):
                sys.exit("읽기 실패: " + out[:300])
            n = 0
            for r in json.loads(out):
                doc = r.get("document")
                if not doc:
                    continue
                old = doc["fields"].get("country", {}).get("stringValue", "")
                if len(old) != 2:
                    continue
                new = table.get(old.upper())
                if not new:
                    unknown[old] = unknown.get(old, 0) + 1
                    continue
                backup.append({"name": doc["name"], "country": old})
                writes.append({"update": {"name": doc["name"], "fields": {"country": {"stringValue": new}}},
                               "updateMask": {"fieldPaths": ["country"]},
                               "currentDocument": {"exists": True}})
                n += 1
            print(f"  boards_{b}: 바꿀 문서 {n}건")
        if unknown:
            print("  표에 없어 건너뛴 코드:", unknown)
        print(f"\n총 {len(writes)}건")

        if not a.apply:
            print("미리보기만 했다. 실제로 바꾸려면 --apply 를 붙인다.")
            return
        if not writes:
            return

        path = os.path.join(HERE, time.strftime("country-backup-%Y%m%d-%H%M%S.json"))
        json.dump(backup, open(path, "w"), ensure_ascii=False, indent=1)
        print("옛 값 백업:", path)

        # :commit 은 한 번에 최대 500건
        for i in range(0, len(writes), 400):
            chunk = writes[i:i+400]
            out = seed.curl([base + ":commit", "-X", "POST", "-H", "Content-Type: application/json",
                             "--data-binary", "@-"], data=json.dumps({"writes": chunk}).encode(), cfg=cfg)
            r = json.loads(out) if out.strip().startswith("{") else {}
            if "writeResults" not in r:
                sys.exit("배치 실패: " + out[:300])
            print(f"  배치 {i//400+1}: {len(chunk)}건 커밋")
        print("완료")
    finally:
        os.unlink(cfg)

if __name__ == "__main__":
    main()
