#!/usr/bin/env python3
"""Firestore + Identity Toolkit REST 를 최소한만 흉내내는 목 서버.
Leaderboard.cs 를 실제 HTTP 경로로 통과시켜 CRUD 를 검증하는 용도.
실제 Firebase 가 아니므로 API 키·보안 규칙·색인은 검사하지 않는다."""
import json, re, sys, uuid
from http.server import BaseHTTPRequestHandler, HTTPServer
from urllib.parse import urlparse, parse_qs

STORE = {}    # {collection: {docId: fields}}
TOKENS = {}   # {refresh_token: uid}
DOCPATH = re.compile(r"^/v1/projects/([^/]+)/databases/\(default\)/documents(?:/([^/]+)/([^/]+))?$")

class H(BaseHTTPRequestHandler):
    def log_message(self, *a): pass

    def send(self, obj, code=200):
        b = (obj if isinstance(obj, str) else json.dumps(obj)).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(b)))
        self.end_headers()
        self.wfile.write(b)

    def body(self):
        n = int(self.headers.get("Content-Length") or 0)
        return self.rfile.read(n).decode() if n else ""

    def docname(self, proj, col, did):
        return f"projects/{proj}/databases/(default)/documents/{col}/{did}"

    def do_POST(self):
        u = urlparse(self.path)
        if u.path.endswith("/accounts:signUp"):
            uid = "u_" + uuid.uuid4().hex[:12]
            rt = "r_" + uuid.uuid4().hex[:16]
            TOKENS[rt] = uid
            return self.send({"idToken": "t_" + uid, "localId": uid,
                              "refreshToken": rt, "expiresIn": "3600"})
        if u.path.endswith("/token"):
            rt = (parse_qs(self.body()).get("refresh_token") or [""])[0]
            uid = TOKENS.get(rt)
            if not uid:
                return self.send({"error": {"message": "INVALID_REFRESH_TOKEN"}}, 400)
            return self.send({"id_token": "t_" + uid, "user_id": uid,
                              "refresh_token": rt, "expires_in": "3600"})

        # documents:runQuery
        m = re.match(r"^/v1/projects/([^/]+)/databases/\(default\)/documents:runQuery$", u.path)
        if m:
            proj = m.group(1)
            q = json.loads(self.body()).get("structuredQuery", {})
            col = q.get("from", [{}])[0].get("collectionId", "")
            rows = list(STORE.get(col, {}).items())
            for ob in reversed(q.get("orderBy", [])):
                fp = ob["field"]["fieldPath"]
                desc = ob.get("direction") == "DESCENDING"
                rows.sort(key=lambda kv: int(kv[1].get(fp, {}).get("integerValue", 0)), reverse=desc)
            rows = rows[:q.get("limit", 100)]
            if not rows:
                return self.send([{"readTime": "1970-01-01T00:00:00Z"}])
            return self.send([{"document": {"name": self.docname(proj, col, d), "fields": f}}
                              for d, f in rows])
        return self.send({"error": {"code": 404, "message": "unknown"}}, 404)

    def do_PATCH(self):
        u = urlparse(self.path)
        m = DOCPATH.match(u.path)
        if not m or not m.group(2):
            return self.send({"error": {"code": 400, "message": "bad path"}}, 400)
        proj, col, did = m.groups()
        incoming = json.loads(self.body()).get("fields", {})
        mask = parse_qs(u.query).get("updateMask.fieldPaths")
        cur = STORE.setdefault(col, {}).setdefault(did, {})
        for k, v in incoming.items():
            if mask is None or k in mask:
                cur[k] = v
        return self.send({"name": self.docname(proj, col, did), "fields": cur})

    def do_GET(self):
        m = DOCPATH.match(urlparse(self.path).path)
        if not m or not m.group(2):
            return self.send({"error": {"code": 400, "message": "bad path"}}, 400)
        proj, col, did = m.groups()
        doc = STORE.get(col, {}).get(did)
        if doc is None:
            return self.send({"error": {"code": 404, "status": "NOT_FOUND",
                                        "message": "Document not found."}}, 404)
        return self.send({"name": self.docname(proj, col, did), "fields": doc})

    def do_DELETE(self):
        m = DOCPATH.match(urlparse(self.path).path)
        if not m or not m.group(2):
            return self.send({"error": {"code": 400}}, 400)
        _, col, did = m.groups()
        STORE.get(col, {}).pop(did, None)
        return self.send({})

if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8765
    print("mock firestore on", port, flush=True)
    HTTPServer(("127.0.0.1", port), H).serve_forever()
