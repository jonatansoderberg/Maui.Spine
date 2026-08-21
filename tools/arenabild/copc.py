"""Minimal HTTP-fil för COPC: laspy behöver en sökbar ström, servern kan Range."""
import base64, io, time, urllib.error, urllib.request
import terrain as T

RETRY = (403, 429, 500, 502, 503, 504)   # dl1 avvisar sporadiskt fullt giltiga anrop


def _open(req, tries=8):
    for i in range(tries):
        try:
            return urllib.request.urlopen(req, timeout=120).read()
        except urllib.error.HTTPError as e:
            if e.code not in RETRY or i == tries - 1:
                raise
            time.sleep(1.2 * (i + 1))


class HttpFile(io.RawIOBase):
    def __init__(self, url):
        self.url, self.pos = url, 0
        u, p = T.credentials()
        self.auth = "Basic " + base64.b64encode(f"{u}:{p}".encode()).decode()
        for i in range(8):
            req = urllib.request.Request(url, method="HEAD",
                                         headers={"Authorization": self.auth})
            try:
                with urllib.request.urlopen(req, timeout=60) as r:
                    self.size = int(r.headers["Content-Length"]); break
            except urllib.error.HTTPError as e:
                if e.code not in RETRY or i == 7:
                    raise
                time.sleep(1.2 * (i + 1))

    def readable(self): return True
    def seekable(self): return True
    def tell(self): return self.pos

    def seek(self, off, whence=0):
        self.pos = off if whence == 0 else self.pos + off if whence == 1 else self.size + off
        return self.pos

    def read(self, n=-1):
        if n < 0:
            n = self.size - self.pos
        n = min(n, self.size - self.pos)
        if n <= 0:
            return b""
        req = urllib.request.Request(self.url, headers={
            "Authorization": self.auth,
            "Range": f"bytes={self.pos}-{self.pos + n - 1}"})
        data = _open(req)
        self.pos += len(data)
        return data

    def readinto(self, buf):
        """laspy läser via readinto; RawIOBase härleder inte den ur read()."""
        data = self.read(len(buf))
        buf[:len(data)] = data
        return len(data)
