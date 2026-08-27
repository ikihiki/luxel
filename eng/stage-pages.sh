#!/usr/bin/env bash
set -euo pipefail

usage() {
    echo "Usage: $0 <chooser-index> <gallery-wwwroot> <editor-wwwroot> <demo-wwwroot> <output-directory>" >&2
}

if [[ $# -ne 5 ]]; then
    usage
    exit 2
fi

chooser_index=$1
gallery_wwwroot=$2
editor_wwwroot=$3
demo_wwwroot=$4
output_directory=$5

validate_chooser() {
    local index_file=$1

    if [[ ! -f "$index_file" ]]; then
        echo "error: chooser index.html does not exist: $index_file" >&2
        exit 1
    fi

    for href in gallery/ editor/ demo/; do
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

path_contains() {
    local parent=$1
    local child=$2
    [[ "$child" == "$parent" || "$child" == "$parent/"* ]]
}

validate_output_directory() {
    local output=$1
    local output_absolute
    local working_directory
    local chooser_absolute
    local input_absolute

    if [[ -L "$output" ]]; then
        echo "error: refusing to recreate symlink output directory: '$output'" >&2
        exit 1
    fi

    output_absolute=$(realpath -m -- "$output")
    working_directory=$(pwd -P)
    chooser_absolute=$(realpath -m -- "$chooser_index")

    if [[ "$output_absolute" == / || "$output_absolute" == "$working_directory" ||
          ! "$output_absolute" == "$working_directory/"* ]]; then
        echo "error: refusing to recreate unsafe output directory outside the working tree: '$output'" >&2
        exit 1
    fi

    if path_contains "$output_absolute" "$chooser_absolute"; then
        echo "error: output directory must not contain the chooser index: '$output'" >&2
        exit 1
    fi

    for input_directory in "$gallery_wwwroot" "$editor_wwwroot" "$demo_wwwroot"; do
        input_absolute=$(realpath -m -- "$input_directory")
        if path_contains "$output_absolute" "$input_absolute" || path_contains "$input_absolute" "$output_absolute"; then
            echo "error: output directory must not overlap an input wwwroot: '$output' and '$input_directory'" >&2
            exit 1
        fi
    done
}

validate_chooser "$chooser_index"
validate_wwwroot "Gallery" "$gallery_wwwroot"
validate_wwwroot "Editor" "$editor_wwwroot"
validate_wwwroot "Browser Editor demo" "$demo_wwwroot"
validate_output_directory "$output_directory"

rm -rf -- "$output_directory"
mkdir -p -- "$output_directory/gallery" "$output_directory/editor" "$output_directory/demo"
cp -- "$chooser_index" "$output_directory/index.html"
cp -a -- "$gallery_wwwroot/." "$output_directory/gallery/"
cp -a -- "$editor_wwwroot/." "$output_directory/editor/"
cp -a -- "$demo_wwwroot/." "$output_directory/demo/"
: > "$output_directory/.nojekyll"

validate_chooser "$output_directory/index.html"
validate_wwwroot "Staged Gallery" "$output_directory/gallery"
validate_wwwroot "Staged Editor" "$output_directory/editor"
validate_wwwroot "Staged Browser Editor demo" "$output_directory/demo"

if [[ -e "$output_directory/_framework" ]]; then
    echo "error: app _framework assets must not be staged at the Pages root" >&2
    exit 1
fi

gallery_file_count=$(find "$gallery_wwwroot" -type f | wc -l | tr -d '[:space:]')
editor_file_count=$(find "$editor_wwwroot" -type f | wc -l | tr -d '[:space:]')
demo_file_count=$(find "$demo_wwwroot" -type f | wc -l | tr -d '[:space:]')
staged_gallery_file_count=$(find "$output_directory/gallery" -type f | wc -l | tr -d '[:space:]')
staged_editor_file_count=$(find "$output_directory/editor" -type f | wc -l | tr -d '[:space:]')
staged_demo_file_count=$(find "$output_directory/demo" -type f | wc -l | tr -d '[:space:]')

if [[ "$gallery_file_count" != "$staged_gallery_file_count" ||
      "$editor_file_count" != "$staged_editor_file_count" ||
      "$demo_file_count" != "$staged_demo_file_count" ]]; then
    echo "error: staged app file counts do not match published wwwroots" >&2
    exit 1
fi

total_file_count=$(find "$output_directory" -type f | wc -l | tr -d '[:space:]')
printf 'Staged Pages: Gallery %s files at /gallery/, Editor %s files at /editor/, Browser Editor demo %s files at /demo/, total %s files -> %s\n' \
    "$gallery_file_count" "$editor_file_count" "$demo_file_count" "$total_file_count" "$output_directory"
