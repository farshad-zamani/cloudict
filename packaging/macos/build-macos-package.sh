#!/usr/bin/env bash
#
# Builds Cloudict.app and a .dmg. Must run on macOS: hdiutil and codesign exist nowhere else.
#
#   ./build-macos-package.sh <publish-dir> <version> <arch> [output-dir]
#
# arch is x64 or arm64, and only labels the output; the payload comes from <publish-dir>.
#
# Signing is optional and off unless the environment supplies credentials. Without them the .dmg
# still works, but Gatekeeper will refuse it on first open until the user right-clicks and chooses
# Open, or strips the quarantine attribute. Set MACOS_SIGN_IDENTITY (and, to notarise,
# APPLE_ID / APPLE_TEAM_ID / APPLE_APP_PASSWORD) to produce a build that opens with no warning.
set -euo pipefail

PUBLISH_DIR="${1:?usage: build-macos-package.sh <publish-dir> <version> <arch> [output-dir]}"
VERSION="${2:?missing version}"
ARCH="${3:?missing arch (x64 or arm64)}"
OUT_DIR="${4:-$(pwd)/dist}"

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"

mkdir -p "$OUT_DIR"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

APP="$STAGE/Cloudict.app"
echo "=== assembling $APP ==="
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

cp -R "$PUBLISH_DIR"/. "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/Cloudict"
find "$APP/Contents/MacOS/Drivers" -name chromedriver -exec chmod +x {} \; 2>/dev/null || true

# Icon: macOS wants an .icns, which is built from the PNG.
ICONSET="$STAGE/cloudict.iconset"
mkdir -p "$ICONSET"
for size in 16 32 64 128 256 512; do
  sips -z $size $size "$REPO_ROOT/src/Cloudict.App/Assets/logo.png" \
       --out "$ICONSET/icon_${size}x${size}.png" >/dev/null 2>&1 || true
  sips -z $((size*2)) $((size*2)) "$REPO_ROOT/src/Cloudict.App/Assets/logo.png" \
       --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null 2>&1 || true
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/Cloudict.icns" 2>/dev/null || \
  echo "!! iconutil failed; the bundle will use the default icon"

sed -e "s/@VERSION@/$VERSION/g" "$HERE/Info.plist" > "$APP/Contents/Info.plist"

# ------------------------------------------------------------------ signing (optional)
if [ -n "${MACOS_SIGN_IDENTITY:-}" ]; then
  echo "=== signing ==="
  # Nested binaries must be signed before the bundle that contains them.
  find "$APP/Contents/MacOS" -type f \( -name '*.dylib' -o -name 'chromedriver' \) -exec \
    codesign --force --timestamp --options runtime --sign "$MACOS_SIGN_IDENTITY" {} \;

  codesign --force --timestamp --options runtime \
           --entitlements "$HERE/entitlements.plist" \
           --sign "$MACOS_SIGN_IDENTITY" "$APP"

  codesign --verify --deep --strict --verbose=2 "$APP"
else
  echo "=== not signing (MACOS_SIGN_IDENTITY unset) ==="
  echo "    The .dmg will work, but Gatekeeper blocks it until the user right-clicks and"
  echo "    chooses Open, or runs: xattr -dr com.apple.quarantine /Applications/Cloudict.app"
fi

# ------------------------------------------------------------------ dmg
DMG="$OUT_DIR/Cloudict-${VERSION}-macos-${ARCH}.dmg"
echo "=== building $DMG ==="

DMG_STAGE="$STAGE/dmg"
mkdir -p "$DMG_STAGE"
cp -R "$APP" "$DMG_STAGE/"
ln -s /Applications "$DMG_STAGE/Applications"   # the familiar drag-to-install layout

rm -f "$DMG"
hdiutil create -volname "Cloudict $VERSION" -srcfolder "$DMG_STAGE" \
               -ov -format UDZO "$DMG" >/dev/null

# ------------------------------------------------------------------ notarisation (optional)
if [ -n "${MACOS_SIGN_IDENTITY:-}" ] && [ -n "${APPLE_ID:-}" ] && [ -n "${APPLE_TEAM_ID:-}" ] && [ -n "${APPLE_APP_PASSWORD:-}" ]; then
  echo "=== notarising ==="
  xcrun notarytool submit "$DMG" --apple-id "$APPLE_ID" --team-id "$APPLE_TEAM_ID" \
        --password "$APPLE_APP_PASSWORD" --wait
  # Stapling lets the ticket travel with the file, so it opens even offline.
  xcrun stapler staple "$DMG"
else
  echo "=== not notarising (Apple credentials unset) ==="
fi

echo
echo "=== done ==="
ls -lh "$DMG"
