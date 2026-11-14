#!/usr/bin/env python3
"""Guarda do CNCSTUDIO. Custo zero (sem IA).
Bloqueia merge, push em main/dev, force, exclusão e edição de arquivos protegidos.
O Igor libera caminhos por missão em .claude/liberados.local (fora do Git).
É trava de processo, não segurança."""
import json, os, re, sys

PROTEGIDOS = [
    "account.js", "manifest.webmanifest", "service-worker.js",
    ".github/workflows/", "supabase/migrations/", "supabase/templates/",
    "PlatformModules.cs", ".claude/",
]
COMANDOS = [
    r"\bgh\s+pr\s+merge\b",
    r"\bgit\s+push\b.*(--force|--force-with-lease|\s-f\b)",
    r"\bgit\s+push\b.*(:|\s)(main|dev)\b",
    r"\bgit\s+push\b.*--delete",
    r"\bgit\s+worktree\s+prune\b",
    r"\bgit\s+remote\s+(update|prune)\b",
    r"\bgh\s+api\b.*(/merge|-X\s*(PUT|DELETE)|--method\s*(PUT|DELETE))",
]
ESCRITA = re.compile(
    r"(>|\btee\b|\bsed\s+-i|\bmv\b|\bcp\b|\brm\b|\bgit\s+(checkout|restore|apply)\b"
    r"|\bSet-Content\b|\bOut-File\b|\bAdd-Content\b|\bRemove-Item\b|\bMove-Item\b|\bCopy-Item\b)", re.I)
SHELLS = ("Bash", "PowerShell")


def negar(motivo):
    print(json.dumps({"hookSpecificOutput": {
        "hookEventName": "PreToolUse",
        "permissionDecision": "deny",
        "permissionDecisionReason": motivo}}))
    sys.exit(0)


def liberados(raiz):
    try:
        with open(os.path.join(raiz, ".claude", "liberados.local"), encoding="utf-8") as f:
            return [l.strip().replace("\\", "/") for l in f
                    if l.strip() and not l.startswith("#")]
    except OSError:
        return []


def relativo(caminho, raiz):
    """Caminho relativo à raiz do projeto; None se estiver fora dela
    (ex.: ~/.claude do próprio Claude não é arquivo protegido do projeto)."""
    c = os.path.normpath(os.path.join(raiz, caminho))
    r = os.path.normpath(raiz)
    try:
        if os.path.commonpath([os.path.normcase(c), os.path.normcase(r)]) != os.path.normcase(r):
            return None
    except ValueError:  # unidades diferentes no Windows
        return None
    return os.path.relpath(c, r).replace("\\", "/")


def main():
    dados = json.load(sys.stdin)
    ferramenta = dados.get("tool_name", "")
    entrada = dados.get("tool_input") or {}
    raiz = os.environ.get("CLAUDE_PROJECT_DIR") or dados.get("cwd") or os.getcwd()
    livres = liberados(raiz)

    if ferramenta in SHELLS:
        cmd = (entrada.get("command") or "").replace("\\", "/")
        for padrao in COMANDOS:
            if re.search(padrao, cmd, re.I):
                negar("Bloqueado: merge, push em main/dev, force ou exclusão são só do Igor.")
        if ESCRITA.search(cmd):
            for p in PROTEGIDOS:
                if p in cmd and not any(l in cmd for l in livres):
                    negar(f"Bloqueado: o comando altera arquivo protegido ({p}). Peça ao Igor para liberar.")
        return

    caminho = entrada.get("file_path") or entrada.get("notebook_path") or ""
    if not caminho:
        return
    rel = relativo(caminho, raiz)
    if rel is None:
        return
    alvo = "/" + rel
    for p in PROTEGIDOS:
        if "/" + p in alvo and not any(l in alvo for l in livres):
            negar(f"Arquivo protegido ({p}). Peça ao Igor para liberar em .claude/liberados.local.")


if __name__ == "__main__":
    main()
