#!/bin/sh

set -eu

script_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
install_directory=${MIMI_INSTALL_DIR:-"$HOME/git/maro/bin"}

if [ -z "${DEVELOPER_DIR:-}" ] && [ -x /Library/Developer/CommandLineTools/usr/bin/swiftc ]; then
    DEVELOPER_DIR=/Library/Developer/CommandLineTools
    export DEVELOPER_DIR
fi

swift build --package-path "$script_directory" -c release
binary_directory=$(swift build --package-path "$script_directory" -c release --show-bin-path)

mkdir -p "$install_directory"
install -m 755 "$binary_directory/mimi" "$install_directory/mi"

echo "Installed: $install_directory/mi"
echo "Run 'mi --check' to verify the API key and microphone permission."
