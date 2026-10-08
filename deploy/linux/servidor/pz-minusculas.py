#!/usr/bin/env python3
"""Corrige mods feitos no Windows: o PZ procura arquivos de animação com o caminho todo em minúsculas
("mods/example traits/42.20/media/animsets/..."), e no Linux o nome real ("Example Traits/.../AnimSets/...") não bate.
Cria um espelho em minúsculas só das pastas media/animsets e media/actiongroups, com atalhos (symlinks)
para os arquivos reais. Não espelha mod.info, então o jogo não enxerga mods duplicados.
Roda antes de cada início do servidor (ExecStartPre) e é seguro repetir."""
import os
import re
from pathlib import Path

settings = {"PZ_ROOT": "/opt/pzserver", "PZ_SERVER_NAME": "servertest"}
environment_file = Path("/etc/pzmanager/server.env")
if environment_file.is_file():
    for raw_line in environment_file.read_text(encoding="utf-8").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        if key in settings:
            settings[key] = value.strip().strip('"\'')
if not re.fullmatch(r"[A-Za-z0-9_-]{1,32}", settings["PZ_SERVER_NAME"]):
    raise SystemExit("PZ_SERVER_NAME inválido")
pz_root = Path(settings["PZ_ROOT"])
if not pz_root.is_absolute() or pz_root == Path("/") or ".." in pz_root.parts:
    raise SystemExit("PZ_ROOT inválido")

RAIZ = pz_root / "pz/steamapps/workshop/content/108600"
PASTAS = ("animsets", "actiongroups")
criados = 0
if RAIZ.is_dir():
    for item in RAIZ.iterdir():
        mods = item / "mods"
        if not mods.is_dir():
            continue
        for atual, _, arquivos in os.walk(mods, followlinks=False):
            rel = Path(atual).relative_to(mods)
            partes = [p.lower() for p in rel.parts]
            # só dentro de .../media/<animsets|actiongroups>/...
            if "media" not in partes or partes[partes.index("media") + 1:][:1] not in ([p] for p in PASTAS):
                continue
            destino_dir = mods / Path(*partes)
            for nome in arquivos:
                real = Path(atual) / nome
                link = destino_dir / nome.lower()
                if link.exists() or link.is_symlink():
                    continue
                destino_dir.mkdir(parents=True, exist_ok=True)
                link.symlink_to(real)
                criados += 1
print(f"pz-minusculas: {criados} atalhos criados")
