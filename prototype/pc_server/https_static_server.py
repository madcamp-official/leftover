"""
폰 브라우저가 접속할 정적 페이지(phone_client/index.html)를 HTTPS로 서빙한다.

WHY HTTPS인가: 최신 브라우저(Android Chrome, iOS Safari 모두)는 DeviceMotionEvent /
DeviceOrientationEvent 같은 센서 API를 "보안 컨텍스트"(HTTPS, 혹은 localhost)에서만
허용한다. 로컬 와이파이에서 http://<PC-IP>로 열면 센서 권한 자체가 막힌다.
그래서 자체 서명 인증서(certs/)로 로컬 HTTPS를 띄운다. 폰에서 처음 열면 인증서 경고가
뜨는데, "고급 -> 계속 진행(안전하지 않음)"을 눌러야 한다. 이건 자체 서명 인증서라서
당연히 뜨는 경고이고, 로컬 프로토타입 용도이므로 무시해도 된다.

실행: python https_static_server.py
"""

import http.server
import pathlib
import ssl

PORT = 8443
DIRECTORY = pathlib.Path(__file__).parent.parent / "phone_client"
CERT_DIR = pathlib.Path(__file__).parent / "certs"


class Handler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=str(DIRECTORY), **kwargs)

    def end_headers(self):
        # 캐시 끄기: index.html/app.js 수정 후 폰에서 새로고침해도 바로 반영되도록.
        self.send_header("Cache-Control", "no-store")
        super().end_headers()


def main():
    httpd = http.server.HTTPServer(("0.0.0.0", PORT), Handler)
    ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    ctx.load_cert_chain(
        certfile=str(CERT_DIR / "cert.pem"),
        keyfile=str(CERT_DIR / "key.pem"),
    )
    httpd.socket = ctx.wrap_socket(httpd.socket, server_side=True)
    print(f"[https] serving {DIRECTORY} on https://0.0.0.0:{PORT}")
    print("        phone browser에서 https://<PC-IP>:8443 으로 접속")
    httpd.serve_forever()


if __name__ == "__main__":
    main()
