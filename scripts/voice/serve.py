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
import re
import socketserver
import sys
import webbrowser
from pathlib import Path
from urllib.parse import unquote

REPO = Path(__file__).resolve().parents[2]
AUDIO_ROOT = REPO / 'assets/additions/audio'
TEMPLATES_ROOT = REPO / 'templates'
PORT = 8765


def load_bank(char):
    """Map each clip to its SoundBank entry from templates/<char>/voice/soundbank.kdl.

    Returns (bank_id, {clip_stem: sound_name}). The sound_name is the itemId a
    conversation / squad-leader template references. Empty if the char has no bank.
    """
    # Canonical location. Fall back to a recursive glob for older layouts, but warn on ambiguity so a
    # stale duplicate soundbank.kdl under the same <char> segment can't silently shadow the real one.
    canonical = TEMPLATES_ROOT / 'dolls' / char / 'voice' / 'soundbank.kdl'
    if canonical.exists():
        kdl = canonical
    else:
        matches = sorted(TEMPLATES_ROOT.glob(f'**/{char}/voice/soundbank.kdl'))
        if len(matches) > 1:
            print(f'warning: {len(matches)} soundbanks match {char!r}, using {matches[0]}: {matches}', file=sys.stderr)
        kdl = matches[0] if matches else canonical
    if not kdl.exists():
        return None, {}
    text = kdl.read_text(encoding='utf-8')
    m = re.search(r'clone\s+"SoundBank".*?id="([^"]+)"', text)
    bank_id = m.group(1) if m else None
    names, cur = {}, None
    for line in text.splitlines():
        nm = re.search(r'set\s+"name"\s+"([^"]+)"', line)
        if nm:
            cur = nm.group(1)
            continue
        cl = re.search(r'set\s+"clip"\s+asset="([^"]+)"', line)
        if cl and cur is not None:
            names.setdefault(cl.group(1).split('/')[-1], cur)
    return bank_id, names

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
.col-name  { width: 230px; font-family: ui-monospace, monospace; font-size: 12px; color: #ccc; }
.col-item  { width: 230px; font-family: ui-monospace, monospace; font-size: 12px; }
.col-jp    { width: 24%; }
.col-en    { width: 24%; }
.col-note  { color: #888; font-style: italic; }
.itemid { color: #6db3f2; cursor: pointer; border-bottom: 1px dotted #456; }
.itemid:hover { color: #9cd; }
.itemid.copied { color: #6f6; border-bottom-color: #6f6; }
.itemid.orphan { color: #d66; border-bottom: none; cursor: default; }
.bank-id { color: #888; font-size: 13px; font-family: ui-monospace, monospace; }
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
    <span class="bank-id" id="bank-id"></span>
    <span class="spacer"></span>
    <span class="row-count" id="row-count"></span>
</header>
<main>
    <table>
        <thead>
            <tr>
                <th class="col-play"></th>
                <th class="col-name">file</th>
                <th class="col-item">bank itemId</th>
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
    const data = await res.json();
    rows = data.rows || [];
    document.getElementById('bank-id').textContent = data.bank_id ? `bank: ${data.bank_id}` : '(no soundbank.kdl)';
    render();
}

function render() {
    const q = filterInput.value.toLowerCase().trim();
    const filtered = q
        ? rows.filter(r =>
            r.filename.toLowerCase().includes(q) ||
            (r.item_id || '').toLowerCase().includes(q) ||
            (r.transcript || '').toLowerCase().includes(q) ||
            (r.english || '').toLowerCase().includes(q))
        : rows;
    rowCount.textContent = `${filtered.length}/${rows.length} clips`;
    tbody.innerHTML = filtered.map(r => `
        <tr>
            <td class="col-play"><button class="play" data-file="${escapeAttr(r.filename)}">▶</button></td>
            <td class="col-name">${escapeHtml(r.filename)}</td>
            <td class="col-item">${r.in_bank
                ? `<span class="itemid" data-id="${escapeAttr(r.item_id)}" title="click to copy">${escapeHtml(r.item_id)}</span>`
                : `<span class="itemid orphan" title="not registered in soundbank.kdl">${escapeHtml(r.item_id)} ⚠</span>`}</td>
            <td class="col-jp jp">${escapeHtml(r.transcript || '')}</td>
            <td class="col-en">${escapeHtml(r.english || '')}</td>
            <td class="col-note">${escapeHtml(r.note || '')}</td>
        </tr>
    `).join('');
    tbody.querySelectorAll('button.play').forEach(btn => {
        btn.addEventListener('click', () => play(btn, btn.dataset.file));
    });
    tbody.querySelectorAll('span.itemid[data-id]').forEach(el => {
        el.addEventListener('click', () => {
            navigator.clipboard.writeText(el.dataset.id).then(() => {
                el.classList.add('copied');
                setTimeout(() => el.classList.remove('copied'), 800);
            });
        });
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
                bank_id, names = load_bank(char)
                for r in rows:
                    fn = r.get('filename', '')
                    stem = fn[:-4] if fn.endswith('.wav') else fn
                    r['item_id'] = names.get(stem, stem)
                    r['in_bank'] = stem in names
                payload = {'bank_id': bank_id, 'rows': rows}
                return self._send(200, 'application/json', json.dumps(payload).encode())
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
