#!/usr/bin/env bash
# Builds InariIx.MbxHubPlugin and packs it into a .macroDeckPlugin artifact.
#
# Mirrors the Macro-Deck-Sample-Plugins README workflow:
#   1. dotnet restore / dotnet build - fast compile check.
#   2. macrodeck-plugin build        - framework-dependent publish for every RID in
#                                       macrodeck-build.json, packed into ./artifacts.
#   3. macrodeck-plugin validate     - default-level manifest check against the packed artifact
#                                       (pass --publication for the stricter pre-publish check).
#
# If the macrodeck-plugin CLI (https://docs.macro-deck.app/cli/) isn't installed, falls back to the
# same `dotnet publish` commands macrodeck-build.json declares, one per RID, into bin/publish/<rid>/ -
# runnable per-platform builds, but not a packed .macroDeckPlugin (only the CLI assembles that).
#
# Usage:
#   ./build.sh              # all four RIDs
#   ./build.sh win-x64      # a single RID
#   ./build.sh --skip-validate
#   ./build.sh --publication  # stricter check before actually publishing the plugin

set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

rid=""
skip_validate=false
publication=false
for arg in "$@"; do
	case "$arg" in
	--skip-validate) skip_validate=true ;;
	--publication) publication=true ;;
	win-x64 | osx-arm64 | osx-x64 | linux-x64) rid="$arg" ;;
	*)
		echo "Unknown argument: $arg" >&2
		exit 1
		;;
	esac
done

rids=(win-x64 osx-arm64 osx-x64 linux-x64)
[ -n "$rid" ] && rids=("$rid")

echo
echo "== Restore & compile check =="
dotnet restore
dotnet build -c Release --no-restore

if command -v macrodeck-plugin >/dev/null 2>&1; then
	echo
	echo "== Packing with macrodeck-plugin CLI =="
	build_args=(build --output ./artifacts)
	[ -n "$rid" ] && build_args+=(--rid "$rid")
	macrodeck-plugin "${build_args[@]}"

	if [ "$skip_validate" = false ]; then
		artifact=$(find ./artifacts -maxdepth 1 -name '*.macroDeckPlugin' -print0 |
			xargs -0 ls -t 2>/dev/null | head -n1 || true)
		if [ -n "$artifact" ]; then
			echo
			echo "== Validating $(basename "$artifact") =="
			validate_args=(validate --artifact "$artifact")
			[ "$publication" = true ] && validate_args+=(--level Publication)
			macrodeck-plugin "${validate_args[@]}"
		else
			echo "Warning: no .macroDeckPlugin artifact found in ./artifacts to validate." >&2
		fi
	fi

	echo
	echo "Done. Packed artifact(s) in ./artifacts"
else
	cat >&2 <<'EOF'
Warning: macrodeck-plugin CLI not found on PATH - falling back to plain 'dotnet publish' per RID.
This produces runnable per-platform builds under bin/publish/<rid>/, but NOT a packed
.macroDeckPlugin artifact (only the CLI assembles and signs that).

Install the CLI for the full build/pack/validate flow:
    dotnet tool install --global MacroDeck.Plugin.Cli --version 3.0.0-preview.6
EOF

	for r in "${rids[@]}"; do
		echo
		echo "== Publishing $r =="
		dotnet publish InariIx.MbxHubPlugin.csproj \
			-c Release -r "$r" --self-contained false -p:UseAppHost=false -o "bin/publish/$r"
	done

	echo
	echo "Done. Per-RID publish output in bin/publish/<rid>/"
fi
