#!/usr/bin/env python3
"""Firebase RTDB + Identity Toolkit REST 를 최소한만 흉내내는 목 서버.
Leaderboard.cs 를 실제 HTTP 경로로 통과시켜 CRUD 를 검증하는 용도."""
import json, re, sys, uuid
from http.server import BaseHTTPRequestHandler, HTTPServer
from urllib.parse import urlparse, parse_qs, unquote

STORE = {}       # {board: {uid: doc}}
TOKENS = {}      # {refresh_token: uid}

class H(BaseHTTPRequestHandler):
    def log_message(self, *a): pass

    def send(self, obj, code=200):
        b = json.dumps(obj).encode() if not isinstance(obj, str) else obj.encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(b)))
        self.end_headers()
        self.wfile.write(b)

    def body(self):
        n = int(self.headers.get("Content-Length") or 0)
        return self.rfile.read(n).decode() if n else ""

    # ---- 인증 ----
    def do_POST(self):
        p = urlparse(self.path).path
        if p.endswith("/accounts:signUp"):
            uid = "u_" + uuid.uuid4().hex[:12]
            rt = "r_" + uuid.uuid4().hex[:16]
            TOKENS[rt] = uid
            return self.send({"idToken": "t_" + uid, "localId": uid,
                              "refreshToken": rt, "expiresIn": "3600"})
        if p.endswith("/token"):
            q = parse_qs(self.body())
            rt = (q.get("refresh_token") or [""])[0]
            uid = TOKENS.get(rt)
            if not uid:
                return self.send({"error": {"message": "INVALID_REFRESH_TOKEN"}}, 400)
            return self.send({"id_token": "t_" + uid, "user_id": uid,
                              "refresh_token": rt, "expires_in": "3600"})
        return self.send({"error": "unknown"}, 404)

    # ---- 데이터 ----
    def parts(self):
        u = urlparse(self.path)
        segs = [s for s in u.path.split("/") if s]
        q = parse_qs(u.query)
        return segs, q

    def do_GET(self):
        segs, q = self.parts()
        # /boards/{board}.json  또는  /boards/{board}/{uid}.json
        if len(segs) == 2 and segs[0] == "boards":
            board = segs[1][:-5]
            rows = STORE.get(board, {})
            order = unquote((q.get("orderBy") or ['""'])[0]).strip('"')
            limit = int((q.get("limitToLast") or ["100"])[0])
            if order:
                items = sorted(rows.items(), key=lambda kv: kv[1].get(order, 0))[-limit:]
                rows = dict(items)
            return self.send(rows if rows else "null")
        if len(segs) == 3 and segs[0] == "boards":
            board, uid = segs[1], segs[2][:-5]
            doc = STORE.get(board, {}).get(uid)
            return self.send(doc if doc is not None else "null")
        return self.send("null")

    def do_PUT(self):
        segs, _ = self.parts()
        if len(segs) == 3 and segs[0] == "boards":
            board, uid = segs[1], segs[2][:-5]
            doc = json.loads(self.body())
            STORE.setdefault(board, {})[uid] = doc
            return self.send(doc)
        return self.send({"error": "bad path"}, 400)

    def do_DELETE(self):
        segs, _ = self.parts()
        if len(segs) == 3 and segs[0] == "boards":
            board, uid = segs[1], segs[2][:-5]
            STORE.get(board, {}).pop(uid, None)
            return self.send("null")
        return self.send({"error": "bad path"}, 400)

if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8765
    print("mock rtdb on", port, flush=True)
    HTTPServer(("127.0.0.1", port), H).serve_forever()
