#!/usr/bin/env python3
"""Tiny HTTP server for browsing voice-line transcripts.

Run from the repo root:
    python3 scripts/voice/serve.py

Then open http://localhost:8765/. Pick a character; play the WAV files
alongside their JP transcript and EN translation.

Discovers any directory under assets/additions/audio/<char>/ that has a
.trans.csv file. Pure stdlib; no extra deps.
"""
import csv
import http.server
import json
import socketserver
import sys
import webbrowser
from pathlib import Path
from urllib.parse import unquote

REPO = Path(__file__).resolve().parents[2]
AUDIO_ROOT = REPO / 'assets/additions/audio'
PORT = 8765

INDEX_HTML = r"""<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>WOMENACE voice lines</title>
<style>
:root { color-scheme: dark; }
body {
    font-family: -apple-system, system-ui, sans-serif;
    margin: 0;
    background: #1a1a1a;
    color: #e0e0e0;
}
header {
    padding: 12px 20px;
    background: #222;
    border-bottom: 1px solid #333;
    display: flex;
    gap: 16px;
    align-items: center;
    position: sticky;
    top: 0;
}
header h1 { margin: 0; font-size: 1.2em; font-weight: 600; }
select, input[type=text] {
    background: #2a2a2a;
    color: #e0e0e0;
    border: 1px solid #444;
    border-radius: 4px;
    padding: 6px 10px;
    font-size: 14px;
}
input[type=text] { width: 200px; }
.spacer { flex: 1; }
.row-count { color: #888; font-size: 13px; }
main { padding: 16px 20px; }
table { width: 100%; border-collapse: collapse; }
th, td {
    padding: 8px 10px;
    text-align: left;
    vertical-align: top;
    border-bottom: 1px solid #2a2a2a;
}
th {
    font-weight: 600;
    color: #aaa;
    font-size: 12px;
    text-transform: uppercase;
    letter-spacing: 0.5px;
    border-bottom: 2px solid #333;
    position: sticky;
    top: 53px;
    background: #1a1a1a;
}
.col-play  { width: 60px; }
.col-name  { width: 280px; font-family: ui-monospace, monospace; font-size: 12px; color: #ccc; }
.col-jp    { width: 30%; }
.col-en    { width: 30%; }
.col-note  { color: #888; font-style: italic; }
button.play {
    background: #2a2a2a;
    border: 1px solid #444;
    border-radius: 4px;
    color: #4af;
    padding: 4px 10px;
    cursor: pointer;
    font-size: 16px;
}
button.play:hover { background: #333; }
button.play.playing { background: #4af; color: #1a1a1a; }
tr:hover { background: #222; }
.muted { color: #666; }
.jp { font-family: 'Noto Sans CJK JP', system-ui, sans-serif; }
</style>
</head>
<body>
<header>
    <h1>voice lines</h1>
    <select id="char-select"></select>
    <input type="text" id="filter" placeholder="filter by text..." autocomplete="off">
    <span class="spacer"></span>
    <span class="row-count" id="row-count"></span>
</header>
<main>
    <table>
        <thead>
            <tr>
                <th class="col-play"></th>
                <th class="col-name">file</th>
                <th class="col-jp">transcript (JP)</th>
                <th class="col-en">english</th>
                <th class="col-note">note</th>
            </tr>
        </thead>
        <tbody id="rows"></tbody>
    </table>
</main>
<audio id="player"></audio>
<script>
const sel = document.getElementById('char-select');
const tbody = document.getElementById('rows');
const filterInput = document.getElementById('filter');
const player = document.getElementById('player');
const rowCount = document.getElementById('row-count');
let rows = [];
let currentChar = null;
let currentBtn = null;

async function loadChars() {
    const res = await fetch('/api/chars');
    const chars = await res.json();
    sel.innerHTML = chars.map(c => `<option value="${c}">${c}</option>`).join('');
    if (chars.length) {
        currentChar = chars[0];
        await loadTrans();
    }
}

async function loadTrans() {
    const res = await fetch(`/api/trans/${currentChar}`);
    rows = await res.json();
    render();
}

function render() {
    const q = filterInput.value.toLowerCase().trim();
    const filtered = q
        ? rows.filter(r =>
            r.filename.toLowerCase().includes(q) ||
            (r.transcript || '').toLowerCase().includes(q) ||
            (r.english || '').toLowerCase().includes(q))
        : rows;
    rowCount.textContent = `${filtered.length}/${rows.length} clips`;
    tbody.innerHTML = filtered.map(r => `
        <tr>
            <td class="col-play"><button class="play" data-file="${escapeAttr(r.filename)}">▶</button></td>
            <td class="col-name">${escapeHtml(r.filename)}</td>
            <td class="col-jp jp">${escapeHtml(r.transcript || '')}</td>
            <td class="col-en">${escapeHtml(r.english || '')}</td>
            <td class="col-note">${escapeHtml(r.note || '')}</td>
        </tr>
    `).join('');
    tbody.querySelectorAll('button.play').forEach(btn => {
        btn.addEventListener('click', () => play(btn, btn.dataset.file));
    });
}

function play(btn, filename) {
    if (currentBtn) currentBtn.classList.remove('playing');
    if (currentBtn === btn && !player.paused) {
        player.pause();
        currentBtn = null;
        return;
    }
    player.src = `/audio/${currentChar}/${encodeURIComponent(filename)}`;
    player.play();
    btn.classList.add('playing');
    currentBtn = btn;
}

player.addEventListener('ended', () => {
    if (currentBtn) currentBtn.classList.remove('playing');
    currentBtn = null;
});

function escapeHtml(s) {
    return String(s).replace(/[&<>"']/g, c => ({'&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'}[c]));
}
function escapeAttr(s) { return escapeHtml(s); }

sel.addEventListener('change', () => { currentChar = sel.value; loadTrans(); });
filterInput.addEventListener('input', render);
loadChars();
</script>
</body>
</html>
"""


class Handler(http.server.BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        # Quiet down default logging
        pass

    def do_GET(self):
        try:
            if self.path == '/' or self.path == '/index.html':
                return self._send(200, 'text/html; charset=utf-8', INDEX_HTML.encode('utf-8'))
            if self.path == '/api/chars':
                chars = sorted(
                    p.name for p in AUDIO_ROOT.iterdir()
                    if p.is_dir() and (p / '.trans.csv').exists()
                )
                return self._send(200, 'application/json', json.dumps(chars).encode())
            if self.path.startswith('/api/trans/'):
                char = unquote(self.path[len('/api/trans/'):])
                csv_path = AUDIO_ROOT / char / '.trans.csv'
                if not csv_path.exists():
                    return self._send(404, 'text/plain', b'not found')
                with csv_path.open(newline='', encoding='utf-8') as f:
                    rows = list(csv.DictReader(f))
                return self._send(200, 'application/json', json.dumps(rows).encode())
            if self.path.startswith('/audio/'):
                rest = unquote(self.path[len('/audio/'):])
                # Resolve under AUDIO_ROOT; reject path traversal.
                target = (AUDIO_ROOT / rest).resolve()
                if not str(target).startswith(str(AUDIO_ROOT.resolve())):
                    return self._send(403, 'text/plain', b'forbidden')
                if not target.is_file():
                    return self._send(404, 'text/plain', b'not found')
                content_type = 'audio/wav' if target.suffix.lower() == '.wav' else 'application/octet-stream'
                data = target.read_bytes()
                return self._send(200, content_type, data)
            self._send(404, 'text/plain', b'not found')
        except Exception as ex:
            self._send(500, 'text/plain', f'error: {ex}'.encode())

    def _send(self, status, content_type, body):
        self.send_response(status)
        self.send_header('Content-Type', content_type)
        self.send_header('Content-Length', str(len(body)))
        self.end_headers()
        self.wfile.write(body)


def main() -> int:
    if not AUDIO_ROOT.is_dir():
        print(f'error: {AUDIO_ROOT} not found', file=sys.stderr)
        return 1

    chars = [p.name for p in AUDIO_ROOT.iterdir() if p.is_dir() and (p / '.trans.csv').exists()]
    print(f'serving on http://localhost:{PORT}/  ({len(chars)} character(s): {", ".join(chars)})', file=sys.stderr)
    print('Ctrl+C to stop.', file=sys.stderr)
    try:
        webbrowser.open(f'http://localhost:{PORT}/')
    except Exception:
        pass

    # allow_reuse_address lets the script restart immediately after Ctrl+C
    # without waiting for the kernel to release the TIME_WAIT socket.
    class _Server(socketserver.ThreadingTCPServer):
        allow_reuse_address = True
        daemon_threads = True

    with _Server(('127.0.0.1', PORT), Handler) as httpd:
        try:
            httpd.serve_forever()
        except KeyboardInterrupt:
            print('\nshutting down', file=sys.stderr)
    return 0


if __name__ == '__main__':
    sys.exit(main())
