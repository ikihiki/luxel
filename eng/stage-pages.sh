#!/usr/bin/env bash
set -euo pipefail

usage() {
    echo "Usage: $0 <chooser-index> <gallery-wwwroot> <editor-wwwroot> <output-directory>" >&2
}

if [[ $# -ne 4 ]]; then
    usage
    exit 2
fi

chooser_index=$1
gallery_wwwroot=$2
editor_wwwroot=$3
output_directory=$4

validate_chooser() {
    local index_file=$1

    if [[ ! -f "$index_file" ]]; then
        echo "error: chooser index.html does not exist: $index_file" >&2
        exit 1
    fi

    for href in gallery/ editor/; do
        if ! grep -Eiq "href=[\"']${href}[\"']" "$index_file"; then
            echo "error: chooser index.html must link to $href: $index_file" >&2
            exit 1
        fi
    done
}

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

validate_chooser "$chooser_index"
validate_wwwroot "Gallery" "$gallery_wwwroot"
validate_wwwroot "Editor" "$editor_wwwroot"

case "$output_directory" in
    ""|/|.|..)
        echo "error: refusing to recreate unsafe output directory: '$output_directory'" >&2
        exit 1
        ;;
esac

rm -rf -- "$output_directory"
mkdir -p -- "$output_directory/gallery" "$output_directory/editor"
cp -- "$chooser_index" "$output_directory/index.html"
cp -a -- "$gallery_wwwroot/." "$output_directory/gallery/"
cp -a -- "$editor_wwwroot/." "$output_directory/editor/"
: > "$output_directory/.nojekyll"

validate_chooser "$output_directory/index.html"
validate_wwwroot "Staged Gallery" "$output_directory/gallery"
validate_wwwroot "Staged Editor" "$output_directory/editor"

if [[ -e "$output_directory/_framework" ]]; then
    echo "error: Gallery _framework must not be staged at the Pages root" >&2
    exit 1
fi

gallery_file_count=$(find "$gallery_wwwroot" -type f | wc -l | tr -d '[:space:]')
editor_file_count=$(find "$editor_wwwroot" -type f | wc -l | tr -d '[:space:]')
total_file_count=$(find "$output_directory" -type f | wc -l | tr -d '[:space:]')
printf 'Staged Pages: Gallery %s files at /gallery/, Editor %s files at /editor/, total %s files -> %s\n' \
    "$gallery_file_count" "$editor_file_count" "$total_file_count" "$output_directory"
