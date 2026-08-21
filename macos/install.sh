#!/bin/sh

set -eu

script_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
install_directory=${MIMI_INSTALL_DIR:-"$HOME/.local/bin"}

swift build --package-path "$script_directory" -c release
binary_directory=$(swift build --package-path "$script_directory" -c release --show-bin-path)

mkdir -p "$install_directory"
install -m 755 "$binary_directory/mimi" "$install_directory/mimi"

echo "Installed: $install_directory/mimi"
echo "Run 'mimi --check' to verify the API key and microphone permission."
