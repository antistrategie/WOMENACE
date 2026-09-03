#!/usr/bin/env python3
"""Talk to a running MENACE dev loader over its TCP bridge.

The dev loader opens a local bridge whose port is randomised per launch and
written to <game>/UserData/jiangyu-bridge.json. This client reads that file,
connects, and speaks the wire protocol (4-byte big-endian length prefix + a
JSON {id, method, params} request, same framing back).

Common uses:

    scripts/bridge.py gifts            # top up 5 of every gift commodity
    scripts/bridge.py gifts 10         # top up 10 of each instead
    scripts/bridge.py workshop         # unlock the ship workshop (+ blueprint vouchers)
    scripts/bridge.py workshop off     # re-lock both gates
    scripts/bridge.py oci              # grant 500 O.C.I. components
    scripts/bridge.py oci 1000         # grant a different amount (negative takes away)
    scripts/bridge.py blackmarket      # restock the black market, as a restock token would
    scripts/bridge.py blackmarket stock  # list what is on the shelf without touching it
    scripts/bridge.py blackmarket open   # open the black market screen
    scripts/bridge.py goods            # 30 valuable minerals to sell, 80 trade value each
    scripts/bridge.py goods 10 --commodity commodity.delicious_plants
    scripts/bridge.py winmission       # kill remaining enemies + complete objectives
    scripts/bridge.py hud off          # take the mission UI down for a clean shot
    scripts/bridge.py hud on           # put it back
    scripts/bridge.py hud toggle       # flip it, for framing a shot in one repeated call
    scripts/bridge.py ping             # sanity check the connection

These only exist in the dev loader, so the game must be running with the dev
loader for them to work. Power-user escape hatches:

    scripts/bridge.py verb "Save.List"                 # run any dev verb
    scripts/bridge.py verb "Gifts.Give" --args '[10]' --mutate
    scripts/bridge.py command scene                    # run any raw command

Override the bridge file with MENACE_BRIDGE_JSON if the game lives elsewhere.
"""

import argparse
import json
import os
import socket
import struct
import sys

# Candidate locations for the bridge descriptor, most specific first. The
# active file is chosen at runtime, so a fresh launch (new port) is picked up
# automatically without editing anything here.
BRIDGE_JSON_CANDIDATES = [
    os.environ.get("MENACE_BRIDGE_JSON"),
    os.path.expanduser("~/Steam/steamapps/common/Menace/UserData/jiangyu-bridge.json"),
    os.path.expanduser("~/.steam/steam/steamapps/common/Menace/UserData/jiangyu-bridge.json"),
    os.path.expanduser("~/.local/share/Steam/steamapps/common/Menace/UserData/jiangyu-bridge.json"),
]


def bridge_port():
    """The port of the currently running dev loader, from its descriptor file."""
    for path in BRIDGE_JSON_CANDIDATES:
        if path and os.path.exists(path):
            with open(path) as handle:
                return json.load(handle)["port"]
    tried = "\n  ".join(p for p in BRIDGE_JSON_CANDIDATES if p)
    raise SystemExit(
        "could not find jiangyu-bridge.json. Is MENACE running with the dev "
        f"loader?\nLooked in:\n  {tried}\n"
        "Set MENACE_BRIDGE_JSON to point at it if the game is installed elsewhere."
    )


def _read_frame(sock):
    """Read one length-prefixed JSON frame off the socket."""
    def exactly(count):
        buffer = b""
        while len(buffer) < count:
            chunk = sock.recv(count - len(buffer))
            if not chunk:
                raise EOFError("bridge closed the connection")
            buffer += chunk
        return buffer

    length = struct.unpack(">i", exactly(4))[0]
    return json.loads(exactly(length).decode())


def call(method, params=None):
    """Send one request and return the decoded response."""
    try:
        connection = socket.create_connection(("127.0.0.1", bridge_port()), timeout=15)
    except OSError as error:
        raise SystemExit(f"could not reach the bridge: {error}. Is the game still running?")
    with connection as sock:
        payload = json.dumps({"id": "1", "method": method, "params": params}).encode()
        sock.sendall(struct.pack(">i", len(payload)) + payload)
        return _read_frame(sock)


def command(name, args=None):
    """Run a bridge command (verb, winmission, scene, ui, templates, skills)."""
    return call("command", {"name": name, "args": args or {}})


def run_verb(verb, args=None, mutate=False):
    """Run a dev verb by its Class.Method name, with positional args."""
    request = {"verb": verb}
    if args:
        request["args"] = args
    if mutate:
        request["mutate"] = True
    return command("verb", request)


def _emit(response):
    print(json.dumps(response, indent=2))


def main():
    parser = argparse.ArgumentParser(
        description="Drive a running MENACE dev loader over its bridge.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    sub = parser.add_subparsers(dest="cmd", required=True)

    gifts = sub.add_parser("gifts", help="top up every gift commodity (default 5 of each)")
    gifts.add_argument("count", nargs="?", type=int, default=5, help="how many of each gift (default 5)")

    workshop = sub.add_parser("workshop", help="unlock the ship workshop (+ blueprint vouchers)")
    workshop.add_argument("state", nargs="?", choices=["on", "off"], default="on",
                          help="on unlocks (default), off re-locks both gates")

    oci = sub.add_parser("oci", help="grant O.C.I. components (default 500)")
    oci.add_argument("amount", nargs="?", type=int, default=500,
                     help="how many components to add, negative takes away (default 500)")

    blackmarket = sub.add_parser("blackmarket", help="restock the black market, or inspect or open it")
    blackmarket.add_argument("action", nargs="?", choices=["restock", "stock", "open"], default="restock",
                             help="restock the shelf (default), stock lists it, open shows the screen")

    goods = sub.add_parser("goods", help="add trade goods to sell at the black market (default 30 valuable minerals)")
    goods.add_argument("count", nargs="?", type=int, default=30, help="how many units to add (default 30)")
    goods.add_argument("--commodity", default="commodity.valuable_minerals",
                       help="commodity template id to add (default commodity.valuable_minerals)")

    sub.add_parser("winmission", help="kill remaining enemies and complete objectives")

    hud = sub.add_parser("hud", help="take the mission UI down (needs a running mission)")
    hud.add_argument("state", nargs="?", choices=["off", "on", "toggle", "state"], default="off",
                     help="off hides the UI (default), on restores it, toggle flips it, "
                          "state reports without changing anything")
    hud.add_argument("--keep-markers", action="store_true",
                     help="leave the world-space unit and objective markers on screen")

    sub.add_parser("ping", help="check the bridge is reachable")

    verb = sub.add_parser("verb", help="run any dev verb by Class.Method name")
    verb.add_argument("name", help='the verb, e.g. "Save.List" or "Gifts.Give"')
    verb.add_argument("--args", help="positional args as a JSON array, e.g. '[10]'")
    verb.add_argument("--mutate", action="store_true", help="allow a state-mutating verb")

    raw = sub.add_parser("command", help="run any raw bridge command")
    raw.add_argument("name", help="command name, e.g. scene, ui, templates, skills")
    raw.add_argument("args", nargs="?", help="args as a JSON object, e.g. '{}'")

    options = parser.parse_args()

    if options.cmd == "ping":
        _emit(call("ping"))
    elif options.cmd == "gifts":
        _emit(run_verb("Gifts.Give", [options.count], mutate=True))
    elif options.cmd == "workshop":
        _emit(run_verb("Workshop.Unlock", [options.state == "on"], mutate=True))
    elif options.cmd == "oci":
        _emit(run_verb("Oci.Grant", [options.amount], mutate=True))
    elif options.cmd == "blackmarket":
        if options.action == "stock":
            _emit(run_verb("Trade.Stock"))
        else:
            _emit(run_verb("Trade.Restock" if options.action == "restock" else "Trade.Open", mutate=True))
    elif options.cmd == "goods":
        _emit(run_verb("Trade.Goods", [options.count, options.commodity], mutate=True))
    elif options.cmd == "winmission":
        _emit(command("winmission"))
    elif options.cmd == "hud":
        # Only Hud.Hide takes the marker flag; the other three are argument-free, and passing a
        # spare positional would resolve to no overload at all.
        if options.state == "off":
            _emit(run_verb("Hud.Hide", [not options.keep_markers], mutate=True))
        elif options.state == "state":
            _emit(run_verb("Hud.State"))
        else:
            _emit(run_verb("Hud.Show" if options.state == "on" else "Hud.Toggle", mutate=True))
    elif options.cmd == "verb":
        args = json.loads(options.args) if options.args else None
        _emit(run_verb(options.name, args, mutate=options.mutate))
    elif options.cmd == "command":
        args = json.loads(options.args) if options.args else {}
        _emit(command(options.name, args))


if __name__ == "__main__":
    sys.exit(main())
