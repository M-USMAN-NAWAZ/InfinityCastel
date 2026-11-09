"""Call the project's configured local Unity MCP server without extra packages."""
import json
import sys
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
URL = "http://127.0.0.1:8080/mcp"
session = None


def request(method, params=None, notification=False):
    global session
    message = {"jsonrpc": "2.0", "method": method}
    if not notification:
        message["id"] = 1
    if params is not None:
        message["params"] = params
    headers = {"Content-Type": "application/json", "Accept": "application/json, text/event-stream"}
    if session:
        headers["Mcp-Session-Id"] = session
    req = urllib.request.Request(URL, json.dumps(message).encode(), headers)
    with urllib.request.urlopen(req, timeout=90) as response:
        session = response.headers.get("Mcp-Session-Id", session)
        text = response.read().decode()
    if not text.strip():
        return None
    events = [line[5:].strip() for line in text.splitlines() if line.startswith("data:")]
    if events:
        text = next((event for event in reversed(events) if '"id"' in event), events[-1])
    result = json.loads(text)
    if "error" in result:
        raise RuntimeError(result["error"])
    return result.get("result")


request("initialize", {"protocolVersion": "2025-03-26", "capabilities": {},
                       "clientInfo": {"name": "project-unity-review", "version": "1.0"}})
request("notifications/initialized", notification=True)
method = sys.argv[1] if len(sys.argv) > 1 else "tools/list"
params = json.loads(Path(sys.argv[2]).read_text(encoding="utf-8")) if len(sys.argv) > 2 else {}
if method == "tools/call" and params.get("name") != "set_active_instance":
    request("tools/call", {"name": "set_active_instance", "arguments": {"instance": "InfinityCastel@5bd1c25141e19b77"}})
result = request(method, params)
output = ROOT / "Temp" / "unity-mcp-result.json"
output.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
if method == "tools/list":
    (ROOT / "Temp" / "unity-mcp-tools.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps([{ "name": t["name"], "description": t.get("description", "")[:160] }
                      for t in result.get("tools", [])], indent=2))
else:
    printable = result.get("structuredContent")
    if printable is None:
        if "contents" in result:
            printable = [item.get("text", "") for item in result["contents"]]
        else:
            printable = [item.get("text", "") for item in result.get("content", []) if item.get("type") == "text"]
    print(json.dumps(printable, ensure_ascii=False)[:16000])
