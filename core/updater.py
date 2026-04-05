from __future__ import annotations

import json
import os
import re
import shlex
import shutil
import ssl
import subprocess
import sys
import urllib.request
from typing import Callable, Optional, Tuple

import certifi


_SSL_CONTEXT = ssl.create_default_context(cafile=certifi.where())

REPO_URL = "https://github.com/walsoup/Fichegen"
REPO_BRANCH = "main"
APPLICATIONS_APP_PATH = "/Applications/FicheGen.app"


def parse_repo_slug(repo_url: str) -> Tuple[str, str]:
    """Extract owner/repo from a GitHub URL."""
    match = re.search(r"github\.com[:/]+([^/]+)/([^/]+?)(?:\.git)?/?$", (repo_url or "").strip())
    if not match:
        raise ValueError(f"Invalid GitHub URL: {repo_url}")
    return match.group(1), match.group(2)


def compare_commits(local_sha: Optional[str], remote_sha: Optional[str]) -> bool:
    """Fallback SHA comparison used when ahead/behind data is unavailable."""
    local = (local_sha or "").strip()
    remote = (remote_sha or "").strip()
    if not remote or not local:
        return False
    return local != remote


def _run_cmd(cmd: list[str], cwd: Optional[str], log: Callable[[str], None], timeout: int = 1800) -> str:
    proc = subprocess.run(
        cmd,
        cwd=cwd,
        capture_output=True,
        text=True,
        timeout=timeout,
    )

    if proc.stdout:
        out = proc.stdout.strip()
        if out:
            log(out)
    if proc.stderr:
        err = proc.stderr.strip()
        if err:
            log(err)

    if proc.returncode != 0:
        raise RuntimeError(f"Command failed ({proc.returncode}): {' '.join(cmd)}")

    return (proc.stdout or "").strip()


def _github_latest_commit(repo_url: str, branch: str, timeout: int = 12) -> str:
    owner, repo = parse_repo_slug(repo_url)
    api_url = f"https://api.github.com/repos/{owner}/{repo}/commits/{branch}"
    req = urllib.request.Request(
        api_url,
        headers={
            "Accept": "application/vnd.github+json",
            "User-Agent": "FicheGen-Updater",
        },
    )
    ctx = _SSL_CONTEXT
    with urllib.request.urlopen(req, timeout=timeout, context=ctx) as response:
        payload = json.loads(response.read().decode("utf-8"))
    sha = payload.get("sha", "") if isinstance(payload, dict) else ""
    return (sha or "").strip()


def _git_local_commit(repo_dir: str, log: Callable[[str], None]) -> Optional[str]:
    git_dir = os.path.join(repo_dir, ".git")
    if not os.path.isdir(git_dir):
        return None
    try:
        sha = _run_cmd(["git", "rev-parse", "HEAD"], cwd=repo_dir, log=log, timeout=30)
        return (sha or "").strip()
    except Exception:
        return None


def _git_ahead_behind(repo_dir: str, branch: str, log: Callable[[str], None]) -> Optional[Tuple[int, int]]:
    """Return (local_ahead, remote_ahead) against origin/branch, or None when unavailable."""
    git_dir = os.path.join(repo_dir, ".git")
    if not os.path.isdir(git_dir):
        return None

    try:
        _run_cmd(["git", "fetch", "origin", branch, "--quiet"], cwd=repo_dir, log=log, timeout=60)
        counts = _run_cmd(
            ["git", "rev-list", "--left-right", "--count", f"HEAD...origin/{branch}"],
            cwd=repo_dir,
            log=log,
            timeout=30,
        )
        parts = (counts or "").strip().split()
        if len(parts) != 2:
            return None
        local_ahead = int(parts[0])
        remote_ahead = int(parts[1])
        return local_ahead, remote_ahead
    except Exception:
        return None


def check_update_status(repo_dir: str, repo_url: str = REPO_URL, branch: str = REPO_BRANCH, log: Optional[Callable[[str], None]] = None):
    """
    Check if a newer commit exists on GitHub.

    Returns tuple: (update_available, local_sha, remote_sha, message)
    """
    logger = log or (lambda _msg: None)

    local_sha = _git_local_commit(repo_dir, logger)
    remote_sha = _github_latest_commit(repo_url, branch)
    ahead_behind = _git_ahead_behind(repo_dir, branch, logger)

    if ahead_behind is not None:
        local_ahead, remote_ahead = ahead_behind
        available = remote_ahead > 0
        if available:
            message = f"A newer version is available on GitHub ({remote_ahead} commit(s) ahead)."
        elif local_ahead > 0:
            message = "You are ahead of GitHub; no update is required."
        else:
            message = "You already have the latest version."
    else:
        available = compare_commits(local_sha, remote_sha)
        if available:
            message = "A newer version is available on GitHub."
        elif local_sha and remote_sha:
            message = "You already have the latest version."
        else:
            message = "Could not determine local git history; update status is unavailable."

    return available, local_sha, remote_sha, message


def _ensure_git_available() -> None:
    if not shutil.which("git"):
        raise RuntimeError("Git is required for updates but was not found on this Mac.")


def _ensure_python_command() -> str:
    python_bin = shutil.which("python3")
    if python_bin:
        return python_bin

    # Fallback for source execution contexts where sys.executable is a Python binary.
    if os.path.basename(sys.executable).startswith("python"):
        return sys.executable

    raise RuntimeError("Python 3 is required for updates but was not found.")


def _ensure_repo(repo_dir: str, repo_url: str, branch: str, log: Callable[[str], None]) -> None:
    os.makedirs(os.path.dirname(repo_dir), exist_ok=True)

    if os.path.isdir(os.path.join(repo_dir, ".git")):
        log("🔄 Fetching latest source...")
        _run_cmd(["git", "fetch", "origin", branch, "--depth", "1"], cwd=repo_dir, log=log)
        _run_cmd(["git", "checkout", branch], cwd=repo_dir, log=log)
        _run_cmd(["git", "pull", "--ff-only", "origin", branch], cwd=repo_dir, log=log)
        return

    if os.path.exists(repo_dir):
        shutil.rmtree(repo_dir, ignore_errors=True)

    log("📥 Downloading source repository...")
    _run_cmd(["git", "clone", "--depth", "1", "-b", branch, repo_url, repo_dir], cwd=None, log=log)


def _ensure_build_env(repo_dir: str, log: Callable[[str], None]) -> str:
    python_cmd = _ensure_python_command()
    venv_dir = os.path.join(repo_dir, ".venv-updater")
    venv_python = os.path.join(venv_dir, "bin", "python")

    if not os.path.exists(venv_python):
        log("🐍 Creating isolated build environment...")
        _run_cmd([python_cmd, "-m", "venv", venv_dir], cwd=repo_dir, log=log)

    log("📦 Installing build dependencies...")
    _run_cmd([venv_python, "-m", "pip", "install", "--upgrade", "pip"], cwd=repo_dir, log=log)
    _run_cmd([venv_python, "-m", "pip", "install", "-r", "requirements.txt"], cwd=repo_dir, log=log)
    _run_cmd([venv_python, "-m", "pip", "install", "pyinstaller"], cwd=repo_dir, log=log)
    return venv_python


def _find_build_spec(repo_dir: str) -> Optional[str]:
    """Find a PyInstaller spec file, preferring FicheGen.spec, and return an absolute path."""
    preferred = os.path.join(repo_dir, "FicheGen.spec")
    if os.path.isfile(preferred):
        return preferred

    spec_files: list[str] = []
    for root, dirs, files in os.walk(repo_dir):
        # Skip build artifacts and env folders to keep scan fast and relevant.
        dirs[:] = [
            d for d in dirs
            if d not in {".git", ".venv", ".venv-updater", "build", "dist", "__pycache__"}
        ]
        for filename in files:
            if filename.lower().endswith(".spec"):
                spec_files.append(os.path.join(root, filename))

    if not spec_files:
        return None

    # Prefer a case-insensitive match on fichegen.spec, then shortest path.
    spec_files.sort(key=lambda p: (0 if os.path.basename(p).lower() == "fichegen.spec" else 1, len(p), p.lower()))
    return spec_files[0]


def _build_app(repo_dir: str, python_bin: str, log: Callable[[str], None]) -> str:
    spec_path = _find_build_spec(repo_dir)
    log("🏗️ Building macOS app bundle...")
    if spec_path:
        log(f"🧭 Using spec file: {spec_path}")
        _run_cmd([python_bin, "-m", "PyInstaller", "--clean", "--noconfirm", spec_path], cwd=repo_dir, log=log)
    else:
        entry_script = os.path.join(repo_dir, "main.py")
        if not os.path.isfile(entry_script):
            raise RuntimeError("No .spec file found and main.py is missing; cannot build updater package.")
        log("⚠️ No .spec found. Falling back to one-shot PyInstaller build from main.py.")
        _run_cmd(
            [
                python_bin,
                "-m",
                "PyInstaller",
                "--clean",
                "--noconfirm",
                "--windowed",
                "--name",
                "FicheGen",
                entry_script,
            ],
            cwd=repo_dir,
            log=log,
        )

    app_path = os.path.join(repo_dir, "dist", "FicheGen.app")
    if not os.path.exists(app_path):
        raise RuntimeError("Build finished but FicheGen.app was not found in dist/.")
    return app_path


def install_app_to_applications(app_path: str, log: Optional[Callable[[str], None]] = None) -> str:
    """Install a built app bundle into /Applications, replacing existing FicheGen.app."""
    logger = log or (lambda _msg: None)
    source = os.path.abspath(app_path)
    target = APPLICATIONS_APP_PATH

    if not os.path.exists(source):
        raise RuntimeError(f"Built app not found: {source}")

    logger("📦 Installing FicheGen.app into /Applications...")

    try:
        if os.path.exists(target):
            shutil.rmtree(target)
        shutil.copytree(source, target)
        logger("✅ Installed to /Applications without admin prompt.")
        return target
    except PermissionError:
        logger("🔐 Admin privileges required; requesting macOS authorization...")

    quoted_source = shlex.quote(source)
    quoted_target = shlex.quote(target)
    script = f"rm -rf {quoted_target} && cp -R {quoted_source} {quoted_target}"
    osa_cmd = [
        "osascript",
        "-e",
        f'do shell script "{script}" with administrator privileges',
    ]
    _run_cmd(osa_cmd, cwd=None, log=logger, timeout=600)

    if not os.path.exists(target):
        raise RuntimeError("Installation command finished but /Applications/FicheGen.app was not found.")

    logger("✅ Installed to /Applications/FicheGen.app")
    return target


def update_and_build(
    repo_dir: str,
    repo_url: str = REPO_URL,
    branch: str = REPO_BRANCH,
    *,
    log: Optional[Callable[[str], None]] = None,
) -> str:
    """Update source from GitHub, build FicheGen.app, and install into /Applications."""
    logger = log or (lambda _msg: None)
    _ensure_git_available()
    _ensure_repo(repo_dir, repo_url, branch, logger)
    py = _ensure_build_env(repo_dir, logger)
    built = _build_app(repo_dir, py, logger)
    return install_app_to_applications(built, logger)
