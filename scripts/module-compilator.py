#!/usr/bin/env python3
"""
module-compilator.py - Build & package script for ButterBror modules

Usage:
    python3 module-compilator.py --type <chat|command> --csproj <path> [OPTIONS]

= Examples =================================================================
Build & install Twitch chat module
    python3 module-compilator.py --type chat --csproj ButterBror.ChatModules.Twitch/ButterBror.ChatModules.Twitch.csproj

Don't install, save archive to ./dist/
    python3 build_module.py --type chat --csproj ../ButterBror.ChatModules.Discord/ButterBror.ChatModules.Discord.csproj --no-install --output ./dist

Debug build, don't install
    python3 build_module.py --type command --csproj ./SillyApps.Bot/ButterBror.CommandModules.Fun.csproj --config Debug --no-install

= General options ==========================================================
Dry-run (show what would happen without doing anything)
    python3 build_module.py --type chat --csproj ./MyModule/MyModule.csproj --dry-run

Exclude extra assemblies from archive
    python3 build_module.py --type chat --csproj ./MyModule/MyModule.csproj --exclude SomeShared.Library
"""

import argparse
import json
import os
import platform
import shutil
import subprocess
import sys
import zipfile
from dataclasses import dataclass, field
from enum import Enum
from pathlib import Path
from typing import Optional


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

# ButterBror assemblies
CORE_ASSEMBLY_PREFIXES = [
    "ButterBror.Application",
    "ButterBror.ChatModule",
    "ButterBror.ChatModules.Abstractions",
    "ButterBror.CommandModule",
    "ButterBror.Core",
    "ButterBror.Data",
    "ButterBror.Domain",
    "ButterBror.Infrastructure",
    "ButterBror.Localization",
    "ButterBror.Scripting",
    "ButterBror.AI",
    "ButterBror.Dashboard",
    "Polly.Core",
    "Polly.RateLimiting",
    "Microsoft.Extensions.Resilience",
]

ASSEMBLY_EXTENSIONS = {".dll", ".pdb"}
DEFAULT_TARGET_FRAMEWORK = "net10.0"

APP_DATA_VENDOR = "SillyApps"
APP_DATA_APP    = "ButterBror2"

# ---------------------------------------------------------------------------
# Module type descriptor
# ---------------------------------------------------------------------------

class ModuleType(Enum):
    CHAT    = "chat"
    COMMAND = "command"


@dataclass(frozen=True)
class ModuleTypeConfig:
    """Static metadata that differs between chat and command module types"""
    module_type: ModuleType
    appdata_subdir: str
    label: str


CHAT_TYPE = ModuleTypeConfig(
    module_type=ModuleType.CHAT,
    appdata_subdir="Chat",
    label="Chat",
)

COMMAND_TYPE = ModuleTypeConfig(
    module_type=ModuleType.COMMAND,
    appdata_subdir="Command",
    label="Command",
)

MODULE_TYPES: dict = {
    ModuleType.CHAT.value:    CHAT_TYPE,
    ModuleType.COMMAND.value: COMMAND_TYPE,
}


# ---------------------------------------------------------------------------
# Module build config
# ---------------------------------------------------------------------------

@dataclass
class ModuleConfig:
    """Per-module build configuration derived from a .csproj path"""
    csproj_path: Path
    type_cfg: ModuleTypeConfig
    version: str = "1.0.0"
    description: str = ""
    author: str = "Author"
    extra_excludes: list = field(default_factory=list)

    @property
    def project_name(self) -> str:
        """The project name is the .csproj filename stem"""
        return self.csproj_path.stem

    @property
    def name(self) -> str:
        """Short name — last component of the project name"""
        return self.project_name.rsplit(".", 1)[-1]

    @property
    def main_dll(self) -> str:
        return f"{self.project_name}.dll"


# ---------------------------------------------------------------------------
# Console helpers
# ---------------------------------------------------------------------------

class Colors:
    """ANSI color codes. Disabled on Windows unless FORCE_COLOR is set"""
    _enabled = bool(
        (sys.stdout.isatty() and platform.system() != "Windows")
        or os.environ.get("FORCE_COLOR")
    )
    RESET  = "\033[0m"  if _enabled else ""
    BOLD   = "\033[1m"  if _enabled else ""
    GREEN  = "\033[32m" if _enabled else ""
    YELLOW = "\033[33m" if _enabled else ""
    RED    = "\033[31m" if _enabled else ""
    CYAN   = "\033[36m" if _enabled else ""
    DIM    = "\033[2m"  if _enabled else ""


def log(msg: str, color: str = "") -> None:
    print(f"{color}{msg}{Colors.RESET}")

def info(msg: str) -> None: log(f"  {msg}", Colors.CYAN)
def ok(msg: str)   -> None: log(f"  \u2713 {msg}", Colors.GREEN)
def warn(msg: str) -> None: log(f"  \u26a0 {msg}", Colors.YELLOW)
def err(msg: str)  -> None: log(f"  \u2717 {msg}", Colors.RED)
def step(msg: str) -> None: log(f"\n{Colors.BOLD}{msg}{Colors.RESET}")
def dim(msg: str)  -> None: log(f"    {msg}", Colors.DIM)


# ---------------------------------------------------------------------------
# Path helpers
# ---------------------------------------------------------------------------

def get_install_path(type_cfg: ModuleTypeConfig) -> Path:
    """Returns the platform-specific directory where module PAGs are installed"""
    system = platform.system()
    if system == "Windows":
        appdata = os.environ.get("APPDATA")
        if not appdata:
            raise EnvironmentError("APPDATA environment variable is not set")
        base = Path(appdata)
    elif system in ("Linux", "Darwin"):
        base = Path.home() / ".local" / "share"
    else:
        raise OSError(f"Unsupported operating system: {system}")

    return base / APP_DATA_VENDOR / APP_DATA_APP / type_cfg.appdata_subdir


def find_project_root(start: Path) -> Path:
    """Walk up the directory tree looking for a .sln or .slnx file"""
    current = start
    for _ in range(8):
        if any(current.glob("*.sln")) or any(current.glob("*.slnx")):
            return current
        parent = current.parent
        if parent == current:
            break
        current = parent
    return start


# ---------------------------------------------------------------------------
# Assembly filtering
# ---------------------------------------------------------------------------

def is_core_assembly(filename: str) -> bool:
    """Returns True if `filename` is a core host assembly that must NOT be bundled"""
    stem = Path(filename).stem
    ext  = Path(filename).suffix.lower()

    if ext not in ASSEMBLY_EXTENSIONS:
        return False

    for prefix in CORE_ASSEMBLY_PREFIXES:
        if stem == prefix or stem.startswith(prefix + "."):
            return True
    return False


def collect_module_files(
    build_output: Path,
    module_config: ModuleConfig,
) -> dict:
    """Scans the build output directory and returns {archive_filename: source_path}"""
    included: dict = {}
    excluded: list = []

    if not build_output.exists():
        warn(f"Build output directory does not exist: {build_output}")
        return included

    for file in sorted(build_output.iterdir()):
        if not file.is_file():
            continue

        name = file.name

        if is_core_assembly(name):
            excluded.append(name)
            continue

        stem = file.stem
        skip = any(
            stem == ex or stem.startswith(ex + ".")
            for ex in module_config.extra_excludes
        )
        if skip:
            excluded.append(name)
            continue

        included[name] = file

    dim(f"Including {len(included)} file(s), excluding {len(excluded)} core/extra file(s)")
    return included


# ---------------------------------------------------------------------------
# dotnet build
# ---------------------------------------------------------------------------

def run_dotnet_build(
    project_path: Path,
    config: str,
    framework: str,
    dry_run: bool,
) -> Path:
    """Runs `dotnet build` and returns the expected output directory"""
    output_dir = project_path.parent / "bin" / config / framework

    cmd = [
        "dotnet", "build",
        str(project_path),
        "-c", config,
        "--framework", framework,
        "--nologo",
    ]
    info(f"Command: {' '.join(cmd)}")

    if dry_run:
        warn("[dry-run] skipping actual build")
        return output_dir

    result = subprocess.run(cmd, text=True, capture_output=True)
    if result.returncode != 0:
        err(f"Build output:\n{result.stdout}")
        err(f"Build errors:\n{result.stderr}")
        raise RuntimeError(f"dotnet build failed (exit code {result.returncode})")

    if not output_dir.exists():
        raise FileNotFoundError(f"Expected build output not found: {output_dir}")

    return output_dir


# ---------------------------------------------------------------------------
# Manifest generation
# ---------------------------------------------------------------------------

def create_manifest(module_config: ModuleConfig) -> dict:
    """Builds the manifest dict."""
    return {
        "mainDll":     module_config.main_dll,
        "name":        module_config.project_name,
        "version":     module_config.version,
        "description": module_config.description
                       or f"{module_config.name} {module_config.type_cfg.label.lower()} module for ButterBror2",
        "author":      module_config.author,
    }


# ---------------------------------------------------------------------------
# PAG packaging (ZIP-based archive with .pag extension)
# ---------------------------------------------------------------------------

def package_module(
    files: dict,
    manifest: dict,
    pag_path: Path,
    dry_run: bool,
) -> None:
    """Packs collected files + manifest into a flat PAG archive (ZIP format)"""
    if dry_run:
        warn(f"[dry-run] would create PAG: {pag_path}")
        for name in sorted(files):
            dim(f"  + {name}")
        return

    pag_path.parent.mkdir(parents=True, exist_ok=True)
    tmp_path = pag_path.with_suffix(".tmp")

    try:
        # .pag is still a ZIP archive, just with a different extension
        with zipfile.ZipFile(tmp_path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as zf:
            for archive_name, source_path in sorted(files.items()):
                zf.write(source_path, arcname=archive_name)
                dim(f"  + {archive_name}  ({source_path.stat().st_size:,} bytes)")

            manifest_json = json.dumps(manifest, indent=2, ensure_ascii=False)
            zf.writestr("module.manifest.json", manifest_json)

        tmp_path.replace(pag_path)
    except Exception:
        tmp_path.unlink(missing_ok=True)
        raise


# ---------------------------------------------------------------------------
# Main per-module pipeline
# ---------------------------------------------------------------------------

def build_and_package_module(
    module_config: ModuleConfig,
    build_config: str,
    framework: str,
    output_dir: Optional[Path],
    install: bool,
    dry_run: bool,
) -> Path:
    """Full pipeline for one module"""
    type_cfg = module_config.type_cfg
    step(
        f"Building {Colors.BOLD}{type_cfg.label} module{Colors.RESET}: "
        f"{Colors.BOLD}{module_config.project_name}{Colors.RESET}"
    )

    csproj = module_config.csproj_path
    if not csproj.exists() and not dry_run:
        raise FileNotFoundError(f".csproj not found: {csproj}")
    info(f"Project   : {csproj}")
    info(f"Config    : {build_config}")
    info(f"Framework : {framework}")
    info(f"Manifest  : module.manifest.json")

    # 1. Build
    step("1/4  dotnet build")
    build_output = run_dotnet_build(csproj, build_config, framework, dry_run)
    ok(f"Build output: {build_output}")

    # 2. Collect files
    step("2/4  Collecting module files")
    files = collect_module_files(build_output, module_config)

    if not dry_run and module_config.main_dll not in files:
        raise FileNotFoundError(
            f"Main DLL '{module_config.main_dll}' not found in build output\n"
            f"Expected at: {build_output / module_config.main_dll}"
        )
    ok(f"Collected {len(files)} file(s)")

    # 3. Manifest
    step("3/4  Creating manifest")
    manifest = create_manifest(module_config)
    info(
        f"name={manifest['name']}  "
        f"version={manifest['version']}  "
        f"mainDll={manifest['mainDll']}"
    )

    # 4. Package
    step("4/4  Packaging PAG")
    default_output = module_config.csproj_path.parent.parent / "dist"
    pag_dest = (
        Path(output_dir) / f"{module_config.project_name}.pag"
        if output_dir
        else default_output / f"{module_config.project_name}.pag"
    )

    package_module(files, manifest, pag_dest, dry_run)

    if not dry_run:
        size_kb = pag_dest.stat().st_size / 1024
        ok(f"PAG created: {pag_dest}  ({size_kb:.1f} KB)")
    else:
        ok(f"[dry-run] PAG target: {pag_dest}")

    # 5. Install
    if install:
        step(f"5/5  Installing to AppData/{type_cfg.appdata_subdir}")
        try:
            install_path = get_install_path(type_cfg)
            install_pag  = install_path / f"{module_config.project_name}.pag"
            info(f"Install target: {install_pag}")

            if dry_run:
                warn(f"[dry-run] would copy {pag_dest.name} -> {install_pag}")
            else:
                install_path.mkdir(parents=True, exist_ok=True)
                shutil.copy2(pag_dest, install_pag)
                ok(f"Installed: {install_pag}")
        except OSError as exc:
            warn(f"Could not resolve install path: {exc}")
            warn("Module was built but not installed — copy the PAG manually")

    return pag_dest


# ---------------------------------------------------------------------------
# Argument parsing
# ---------------------------------------------------------------------------

def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Build & package a ButterBror2 chat or command module as a PAG archive",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )

    # Module type
    parser.add_argument(
        "--type", "-t",
        required=True,
        choices=[t.value for t in ModuleType],
        metavar="TYPE",
        help="Module type: 'chat' or 'command'",
    )

    # Module selection: now just --csproj
    parser.add_argument(
        "--csproj", "-p",
        required=True,
        metavar="PATH",
        help="Path to the .csproj file of the module to build",
    )

    # Build options
    parser.add_argument(
        "--config", "-c",
        default="Release",
        choices=["Debug", "Release"],
        help="Build configuration (default: Release)",
    )
    parser.add_argument(
        "--framework", "-f",
        default=DEFAULT_TARGET_FRAMEWORK,
        help=f"Target framework (default: {DEFAULT_TARGET_FRAMEWORK})",
    )
    parser.add_argument(
        "--version",
        default="1.0.0",
        help="Module version written into the manifest (default: 1.0.0)",
    )
    parser.add_argument(
        "--author",
        default="ButterBror Team",
        help="Author written into the manifest",
    )

    # Output / install
    parser.add_argument(
        "--output", "-o",
        metavar="DIR",
        help="Directory for the output PAG (default: <project_root>/dist/)",
    )
    parser.add_argument(
        "--no-install",
        action="store_true",
        help="Skip installation to AppData; only produce the PAG file.",
    )

    # Misc
    parser.add_argument(
        "--dry-run", "-n",
        action="store_true",
        help="Show what would be done without executing any commands.",
    )
    parser.add_argument(
        "--exclude",
        metavar="PREFIX",
        action="append",
        default=[],
        help="Additional assembly prefix to exclude from the PAG (repeatable).",
    )

    return parser.parse_args()


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

def main() -> int:
    args = parse_args()

    type_cfg: ModuleTypeConfig = MODULE_TYPES[args.type]

    # Resolve csproj path
    csproj_path = Path(args.csproj).resolve()
    if not csproj_path.exists() and not args.dry_run:
        err(f".csproj file not found: {csproj_path}")
        return 1

    # Resolve project root for default output path
    project_root = find_project_root(csproj_path.parent)

    log(f"\n{Colors.BOLD}ButterBror2 Module Builder{Colors.RESET}")
    log(f"  Module type  : {Colors.BOLD}{type_cfg.label}{Colors.RESET}")
    log(f"  Project      : {csproj_path}")
    log(f"  Project root : {project_root}")
    log(f"  Build config : {args.config}")
    log(f"  Framework    : {args.framework}")
    log(f"  Manifest     : module.manifest.json")
    log(f"  Install dir  : AppData/{type_cfg.appdata_subdir}/")
    if args.dry_run:
        log(f"  {Colors.YELLOW}DRY-RUN -- no files will be created{Colors.RESET}")

    # Create module config
    module_config = ModuleConfig(
        csproj_path=csproj_path,
        type_cfg=type_cfg,
        version=args.version,
        author=args.author,
        extra_excludes=args.exclude,
    )

    output_dir = Path(args.output).resolve() if args.output else None
    install    = not args.no_install

    # Process
    try:
        pag_path = build_and_package_module(
            module_config=module_config,
            build_config=args.config,
            framework=args.framework,
            output_dir=output_dir,
            install=install,
            dry_run=args.dry_run,
        )
        ok(f"{module_config.project_name}  ->  {pag_path}")
        return 0
    except Exception as exc:
        err(f"\nFailed to build {module_config.project_name}: {exc}")
        return 1


if __name__ == "__main__":
    sys.exit(main())