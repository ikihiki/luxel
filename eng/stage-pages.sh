#!/usr/bin/env bash
set -euo pipefail

usage() {
    echo "Usage: $0 <gallery-wwwroot> <editor-wwwroot> <output-directory>" >&2
}

if [[ $# -ne 3 ]]; then
    usage
    exit 2
fi

gallery_wwwroot=$1
editor_wwwroot=$2
output_directory=$3

validate_wwwroot() {
    local name=$1
    local directory=$2
    local index_file="$directory/index.html"

    if [[ ! -d "$directory" ]]; then
        echo "error: $name wwwroot directory does not exist: $directory" >&2
        exit 1
    fi

    if [[ ! -f "$index_file" ]]; then
        echo "error: $name index.html does not exist: $index_file" >&2
        exit 1
    fi

    if ! grep -Eiq '<base[[:space:]][^>]*href=["'"'"']\./["'"'"'][^>]*>' "$index_file"; then
        echo "error: $name index.html must contain a relative <base href=\"./\">: $index_file" >&2
        exit 1
    fi
}

validate_wwwroot "Gallery" "$gallery_wwwroot"
validate_wwwroot "Editor" "$editor_wwwroot"

if [[ -z "$output_directory" || "$output_directory" == "/" ]]; then
    echo "error: refusing to recreate unsafe output directory: '$output_directory'" >&2
    exit 1
fi

rm -rf -- "$output_directory"
mkdir -p -- "$output_directory"
cp -a -- "$gallery_wwwroot/." "$output_directory/"
rm -rf -- "$output_directory/editor"
mkdir -p -- "$output_directory/editor"
cp -a -- "$editor_wwwroot/." "$output_directory/editor/"
: > "$output_directory/.nojekyll"

validate_wwwroot "Staged Gallery" "$output_directory"
validate_wwwroot "Staged Editor" "$output_directory/editor"

gallery_file_count=$(find "$gallery_wwwroot" -type f | wc -l | tr -d '[:space:]')
editor_file_count=$(find "$editor_wwwroot" -type f | wc -l | tr -d '[:space:]')
printf 'Staged Pages: Gallery %s files at /, Editor %s files at /editor/ -> %s\n' \
    "$gallery_file_count" "$editor_file_count" "$output_directory"
