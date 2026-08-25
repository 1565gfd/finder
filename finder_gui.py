#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# FINDER (Linux GUI) — поиск файлов по имени в графическом окне (в браузере).
# Движок на Python (стандартная библиотека, ничего ставить не нужно — работает
# на SteamOS/Arch и любом Linux с python3). Файлы НЕ открываются: читаются
# только имена каталогов. Двойной клик по результату открывает его папку.
#
# Запуск:  python3 finder_gui.py    (или ./finder_gui.py после chmod +x)
# © 1565gfd

import os, sys, time, fnmatch, json, threading, webbrowser, subprocess, urllib.parse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

SKIP = {"/proc", "/sys", "/dev", "/run"}
LIMIT = 5000  # максимум показываемых путей (счётчик считает все)


def do_search(pattern, root, mode):
    pat = pattern.lower()
    wild = ("*" in pat) or ("?" in pat)
    if mode == "ext":
        name_pat = "*." + pat.lstrip(".")
        wild = True
    elif mode == "wild":
        name_pat = pat
        wild = True
    else:
        name_pat = pat

    results, count = [], 0
    start = time.time()
    for dirpath, dirnames, filenames in os.walk(root, followlinks=False,
                                                onerror=lambda e: None):
        if dirpath in SKIP:
            dirnames[:] = []
            continue
        dirnames[:] = [d for d in dirnames
                       if os.path.join(dirpath, d) not in SKIP]
        for fn in filenames:
            low = fn.lower()
            if mode == "exact":
                m = (low == pat)
            elif wild:
                m = fnmatch.fnmatch(low, name_pat)
            else:
                m = (pat in low)
            if m:
                count += 1
                if len(results) < LIMIT:
                    results.append(os.path.join(dirpath, fn))
    return results, count, round(time.time() - start, 1)


PAGE = r"""<!doctype html><html lang="ru"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>FINDER · 1565gfd</title>
<style>
:root{--bg:#0a0e1a;--bg2:#05060b;--panel:#0e1220;--card:#141a2b;--cardhi:#1d2740;
--line:#22305a;--tx:#e6eaf5;--sub:#6b7690;--blue:#3b6bff;--bluehi:#5b8cff;--green:#34d399;--red:#f87171}
*{box-sizing:border-box}
body{margin:0;font-family:"Cascadia Mono","Consolas",ui-monospace,monospace;
background:linear-gradient(160deg,var(--bg),var(--bg2));color:var(--tx);min-height:100vh;padding:26px}
.wrap{max-width:900px;margin:0 auto}
.top{display:flex;align-items:center;gap:12px;margin-bottom:18px}
.logo{width:38px;height:38px;flex:0 0 auto}
h1{font-size:20px;margin:0;font-weight:700}
.sub{color:var(--sub);font-size:12px}
.row{display:flex;gap:10px;margin-bottom:10px}
input{flex:1;background:var(--card);border:1px solid var(--line);border-radius:10px;
color:var(--tx);font:inherit;font-size:14px;padding:12px 14px;outline:none}
input:focus{border-color:var(--blue)}
#where{flex:0 0 240px}
button{border:0;border-radius:10px;font:inherit;font-weight:700;cursor:pointer}
#go{background:var(--blue);color:#fff;padding:0 26px;font-size:14px}
#go:hover{background:var(--bluehi)}
.chips{display:flex;gap:8px;margin-bottom:12px;flex-wrap:wrap}
.chip{background:var(--card);border:1px solid var(--line);color:var(--sub);
border-radius:14px;padding:6px 14px;font-size:12px;cursor:pointer;user-select:none}
.chip.on{background:var(--blue);border-color:var(--blue);color:#fff}
.chip.all{border-color:var(--blue);color:var(--bluehi)}
#list{background:var(--panel);border:1px solid var(--line);border-radius:12px;
padding:6px;height:52vh;overflow:auto}
.item{padding:6px 10px;border-radius:7px;font-size:12.5px;cursor:pointer;white-space:nowrap}
.item:hover{background:var(--card)}
.item .n{color:var(--tx)} .item .d{color:var(--sub)}
.bar{display:flex;justify-content:space-between;align-items:center;margin-top:10px;
font-size:12px;color:var(--sub)}
.count{color:var(--green);font-weight:700}
.wm{color:var(--sub);opacity:.7;font-size:11px;text-align:center;margin-top:16px}
.spin{display:inline-block;width:14px;height:14px;border:2px solid var(--line);
border-top-color:var(--bluehi);border-radius:50%;animation:s .8s linear infinite;vertical-align:-2px}
@keyframes s{to{transform:rotate(360deg)}}
</style></head><body><div class="wrap">
<div class="top">
<svg class="logo" viewBox="0 0 26 26" fill="none">
<path d="M3 9 H11 V6 H23 V21 H3 Z" stroke="#3b6bff" stroke-width="2.2" stroke-linejoin="round"/>
<circle cx="17.5" cy="15.5" r="5.5" stroke="#e6eaf5" stroke-width="2.4"/>
<line x1="21" y1="19" x2="25" y2="23" stroke="#e6eaf5" stroke-width="2.6" stroke-linecap="round"/>
</svg>
<div><h1>FINDER</h1><div class="sub">поиск файлов · © 1565gfd</div></div>
</div>
<div class="row">
<input id="q" placeholder="что искать: имя или *.pdf" autofocus>
<input id="where" placeholder="где: пусто = ~/">
<button id="go">НАЙТИ</button>
</div>
<div class="chips">
<div class="chip on" data-m="name">по имени</div>
<div class="chip" data-m="wild">маска *</div>
<div class="chip" data-m="exact">точно</div>
<div class="chip" data-m="ext">расширение</div>
<div class="chip all" id="all">🖥 вся система</div>
</div>
<div id="list"></div>
<div class="bar"><span id="status">введите запрос и нажмите «Найти»</span><span class="count" id="count"></span></div>
<div class="wm">© 1565gfd</div>
</div>
<script>
let mode="name";
const $=s=>document.querySelector(s);
document.querySelectorAll('.chip[data-m]').forEach(c=>c.onclick=()=>{
  document.querySelectorAll('.chip[data-m]').forEach(x=>x.classList.remove('on'));
  c.classList.add('on'); mode=c.dataset.m;
});
function run(all){
  const q=$('#q').value.trim();
  if(!q){$('#status').textContent='введите, что искать';return;}
  const where=all?'/':$('#where').value.trim();
  $('#list').innerHTML=''; $('#count').textContent='';
  $('#status').innerHTML='<span class="spin"></span> идёт поиск…';
  const u='/search?q='+encodeURIComponent(q)+'&where='+encodeURIComponent(where)+'&mode='+mode;
  fetch(u).then(r=>r.json()).then(d=>{
    if(d.error){$('#status').textContent='ошибка: '+d.error;return;}
    const L=$('#list'); L.innerHTML='';
    for(const p of d.results){
      const i=p.lastIndexOf('/');
      const el=document.createElement('div'); el.className='item';
      el.innerHTML='<span class="d">'+p.slice(0,i+1).replace(/</g,'&lt;')+'</span><span class="n">'+p.slice(i+1).replace(/</g,'&lt;')+'</span>';
      el.ondblclick=()=>fetch('/open?path='+encodeURIComponent(p));
      L.appendChild(el);
    }
    $('#count').textContent=d.elapsed+' с · '+d.count+' найдено';
    $('#status').textContent=d.count? 'готово · двойной клик — открыть папку':'ничего не найдено';
  }).catch(e=>{$('#status').textContent='ошибка соединения';});
}
$('#go').onclick=()=>run(false);
$('#all').onclick=()=>run(true);
$('#q').addEventListener('keydown',e=>{if(e.key==='Enter')run(false);});
$('#where').addEventListener('keydown',e=>{if(e.key==='Enter')run(false);});
</script></body></html>"""


class H(BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def _send(self, code, ctype, body):
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        u = urllib.parse.urlparse(self.path)
        qs = urllib.parse.parse_qs(u.query)
        if u.path == "/":
            self._send(200, "text/html; charset=utf-8", PAGE.encode("utf-8"))
        elif u.path == "/search":
            q = (qs.get("q", [""])[0]).strip()
            where = (qs.get("where", [""])[0]).strip()
            mode = qs.get("mode", ["name"])[0]
            if not where:
                where = os.path.expanduser("~")
            try:
                if not os.path.isdir(where):
                    raise ValueError("папка не найдена: " + where)
                results, count, elapsed = do_search(q, where, mode)
                body = json.dumps({"results": results, "count": count,
                                   "elapsed": elapsed}).encode("utf-8")
            except Exception as e:
                body = json.dumps({"error": str(e)}).encode("utf-8")
            self._send(200, "application/json; charset=utf-8", body)
        elif u.path == "/open":
            path = qs.get("path", [""])[0]
            folder = os.path.dirname(path)
            try:
                if os.path.isdir(folder):
                    subprocess.Popen(["xdg-open", folder],
                                     stdout=subprocess.DEVNULL,
                                     stderr=subprocess.DEVNULL)
            except Exception:
                pass
            self._send(200, "application/json", b"{}")
        else:
            self._send(404, "text/plain", b"not found")


def main():
    # только localhost — сервер не виден извне
    srv = ThreadingHTTPServer(("127.0.0.1", 0), H)
    port = srv.server_address[1]
    url = "http://127.0.0.1:%d/" % port
    print("FINDER GUI запущен:", url)
    print("Окно откроется в браузере. Закрыть программу — Ctrl+C в этом окне.")
    try:
        webbrowser.open(url)
    except Exception:
        pass
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        print("\nвыход")
        srv.shutdown()


if __name__ == "__main__":
    main()
