#!/bin/bash

# Enable strict mode for robust error handling
set -euo pipefail

# Script to build the Twitch chat module and package it as a ZIP archive

# Get the directory where the script is located
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$SCRIPT_DIR/.."

# Function to determine the target home directory
get_target_home() {
    # 1. Check for explicit override environment variable
    if [[ -n "${BB_INSTALL_HOME:-}" ]]; then
        echo "$BB_INSTALL_HOME"
        return
    fi

    # 2. If running via sudo, detect the original user's home directory
    if [[ -n "${SUDO_USER:-}" ]] && [[ "$EUID" -eq 0 ]]; then
        local user_home
        user_home=$(getent passwd "$SUDO_USER" | cut -d: -f6)
        if [[ -n "$user_home" ]]; then
            echo "$user_home"
            return
        fi
    fi

    # 3. Fallback to current user's HOME
    echo "$HOME"
}

# Determine the AppData/Chat directory based on OS and user context
TARGET_HOME=$(get_target_home)

if [[ "$OSTYPE" == "linux-gnu"* ]] || [[ "$OSTYPE" == "darwin"* ]]; then
    CHAT_MODULES_DIR="$TARGET_HOME/.local/share/SillyApps/ButterBror2/Chat"
else
    # Windows handling (assuming Git Bash or similar where APPDATA exists)
    CHAT_MODULES_DIR="$APPDATA/SillyApps/ButterBror2/Chat"
fi

# Create directory if it doesn't exist
mkdir -p "$CHAT_MODULES_DIR"

# Create temp directory for packaging
TEMP_DIR=""
cleanup() {
    if [[ -n "$TEMP_DIR" ]] && [[ -d "$TEMP_DIR" ]]; then
        rm -rf "$TEMP_DIR"
    fi
}
trap cleanup EXIT

TEMP_DIR=$(mktemp -d)
MODULE_OUTPUT="$PROJECT_ROOT/ButterBror.ChatModules.Twitch/bin/Release/net10.0"

echo "Building ButterBror.ChatModules.Twitch..."
dotnet build "$PROJECT_ROOT/ButterBror.ChatModules.Twitch/ButterBror.ChatModules.Twitch.csproj" -c Release

if [ $? -ne 0 ]; then
    echo "Failed to build Twitch module"
    exit 1
fi

echo "Packaging module from: $MODULE_OUTPUT"
echo "Target directory: $CHAT_MODULES_DIR"

# Copy all files except ButterBror.* assemblies (core libraries) and their PDBs
echo "Copying module files (excluding ButterBror core assemblies and PDBs)..."
MAIN_DLL=""
shopt -s nullglob
for file in "$MODULE_OUTPUT"/*; do
    filename=$(basename "$file")
    # Skip ButterBror core DLLs and PDBs
    # Using case statement for cleaner pattern matching
    case "$filename" in
        ButterBror.Application.dll|ButterBror.Application.pdb| \
        ButterBror.ChatModule.dll|ButterBror.ChatModule.pdb| \
        ButterBror.ChatModules.Abstractions.dll|ButterBror.ChatModules.Abstractions.pdb| \
        ButterBror.Core.dll|ButterBror.Core.pdb| \
        ButterBror.Data.dll|ButterBror.Data.pdb| \
        ButterBror.Domain.dll|ButterBror.Domain.pdb| \
        Polly.Core.dll|Polly.Core.pdb| \
        Polly.RateLimiting.dll|Polly.RateLimiting.pdb| \
        Microsoft.Extensions.Resilience.dll|Microsoft.Extensions.Resilience.pdb)
            echo "  Skipping core: $filename"
            ;;
        *)
            cp "$file" "$TEMP_DIR/"
            echo "  Added: $filename"

            # Identify the main module DLL (first non-core DLL found)
            if [[ "$filename" == *.dll ]] && [[ -z "$MAIN_DLL" ]]; then
                MAIN_DLL="$filename"
            fi
            ;;
    esac
done
shopt -u nullglob

if [[ -z "$MAIN_DLL" ]]; then
    echo "Error: No main module DLL found in output directory."
    exit 1
fi

MANIFEST_FILE="$TEMP_DIR/module.manifest.json"
MODULE_NAME="ButterBror.ChatModules.Twitch"
MODULE_VERSION="1.0.0"

echo "Creating module manifest..."
cat > "$MANIFEST_FILE" << EOF
{
  "mainDll": "$MAIN_DLL",
  "name": "$MODULE_NAME",
  "version": "$MODULE_VERSION",
  "description": "Twitch chat module for ButterBror2",
  "author": "ButterBror Team"
}
EOF
echo "  Created: module.manifest.json"

# Create ZIP archive
ZIP_FILE="$CHAT_MODULES_DIR/ButterBror.ChatModules.Twitch.zip"

# Remove old archive if exists
if [ -f "$ZIP_FILE" ]; then
    rm "$ZIP_FILE"
    echo "Removed old archive: $ZIP_FILE"
fi

echo "Creating ZIP archive: $ZIP_FILE"
cd "$TEMP_DIR"
# Use -r to include all files in current directory recursively
zip -r "$ZIP_FILE" .

# Cleanup is handled by trap

echo "Done! Twitch module packaged to $ZIP_FILE"
echo ""
echo "Files in Chat directory:"
ls -la "$CHAT_MODULES_DIR"