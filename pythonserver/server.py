import http.server
import socketserver

PORT = 8000
DIRECTORY = "."

class Handler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=DIRECTORY, **kwargs)

print(f"Starte Update-Server auf Port {PORT}...")
with socketserver.TCPServer(("", PORT), Handler) as httpd:
    print("Server läuft. Drücke STRG+C zum Beenden.")
    httpd.serve_forever()
