import http.server
import sys
import time

class Handler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == '/slow-header':
            time.sleep(15)
        try:
            self.send_response(200)
            self.send_header('X-DemoMux-Verification', 'slow-page-marker')
            self.send_header('Content-Length', '1400000')
            self.end_headers()
            # Deliberately ignores Range and never completes the body in time.
            for _ in range(14000):
                self.wfile.write(b'x' * 100)
                self.wfile.flush()
                time.sleep(0.1)
        except (BrokenPipeError, ConnectionResetError):
            pass
    def log_message(self, *args):
        pass

server = http.server.ThreadingHTTPServer(('127.0.0.1', int(sys.argv[1])), Handler)
print('ready', flush=True)
server.serve_forever()
