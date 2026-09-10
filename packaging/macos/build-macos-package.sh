#!/usr/bin/env bash
#
# Builds Cloudict.app and a .dmg. Must run on macOS: hdiutil and codesign exist nowhere else.
#
#   ./build-macos-package.sh <publish-dir> <version> <arch> [output-dir]
#
# arch is x64 or arm64, and only labels the output; the payload comes from <publish-dir>.
#
# Signing is optional and off unless the environment supplies credentials. Without them the .dmg
# still works, but Gatekeeper refuses the first open until the user approves the app in
# System Settings -> Privacy & Security, or strips the quarantine attribute. (Note that macOS 15
# removed the older right-click -> Open shortcut, so that advice is no longer enough on its own.)
# Set MACOS_SIGN_IDENTITY (and, to notarise, APPLE_ID / APPLE_TEAM_ID / APPLE_APP_PASSWORD) to
# produce a build that opens with no warning at all.
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

# ChromeDriver goes to Contents/Resources, which is where macOS expects a helper tool. Left under
# Contents/MacOS, its version-named folder is read by codesign as a nested bundle it cannot parse
# ("bundle format unrecognized, invalid, or unsuitable"), and the app will not sign at all.
# BrowserProvisioner looks in both places.
if [ -d "$APP/Contents/MacOS/Drivers" ]; then
  mv "$APP/Contents/MacOS/Drivers" "$APP/Contents/Resources/Drivers"
fi
find "$APP/Contents/Resources/Drivers" -name chromedriver -exec chmod +x {} \; 2>/dev/null || true

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
  # Ad-hoc signing, which needs no Apple account and no certificate.
  #
  # This is not a nicety. macOS refuses to execute an arm64 binary that carries no signature at
  # all — the kernel requires at least an ad-hoc one — so an unsigned build does not merely warn
  # on an Apple Silicon Mac, it fails outright, usually reported as "the application is damaged
  # and should be moved to the Trash". Ad-hoc signing makes the code loadable; it does not make it
  # trusted, so Gatekeeper still blocks the first open — see HOW TO OPEN THIS APP.txt, written into
  # the disk image below. Only notarisation removes that step, and notarisation needs a paid
  # Developer ID.
  echo "=== ad-hoc signing (no MACOS_SIGN_IDENTITY, so no Developer ID) ==="

  # Every file under Contents/MacOS is signed, then the bundle — and deliberately without --deep.
  #
  # Every *file*, not only the Mach-O ones: codesign walks the bundle's nested code when sealing it
  # and refuses the lot if anything inside is unsigned, and a self-contained .NET publish lays
  # hundreds of managed assemblies beside the executable. Filtering to Mach-O left those untouched
  # and the bundle would not sign at all — "code object is not signed at all, in subcomponent
  # System.Diagnostics.Contracts.dll". Signing a non-Mach-O file simply records the signature in an
  # extended attribute, which is enough to satisfy the check. This is the recipe .NET and Avalonia
  # both document for macOS bundles.
  #
  # --deep is avoided throughout: it cannot cope with code sitting in subdirectories of
  # Contents/MacOS and gives up with "bundle format unrecognized, invalid, or unsuitable".
  find "$APP/Contents/MacOS" "$APP/Contents/Resources/Drivers" -type f -print0 2>/dev/null |
    while IFS= read -r -d '' f; do
      codesign --force --sign - "$f" >/dev/null 2>&1 || true
    done

  codesign --force --sign - --identifier com.cloudtart.cloudict "$APP"

  # What has to be true is that the thing macOS executes carries a signature: an Apple Silicon Mac
  # refuses to load an arm64 binary without one, which is why the app was reported as damaged.
  codesign --verify --verbose=2 "$APP/Contents/MacOS/Cloudict"

  echo "    Signed ad-hoc, so the app loads but is not notarised. The first open needs approval in"
  echo "    System Settings -> Privacy & Security, or: xattr -dr com.apple.quarantine /Applications/Cloudict.app"
fi

# ------------------------------------------------------------------ dmg
DMG="$OUT_DIR/Cloudict-${VERSION}-macos-${ARCH}.dmg"
echo "=== building $DMG ==="

DMG_STAGE="$STAGE/dmg"
mkdir -p "$DMG_STAGE"
cp -R "$APP" "$DMG_STAGE/"
ln -s /Applications "$DMG_STAGE/Applications"   # the familiar drag-to-install layout

# The first-launch instructions travel inside the disk image, because that is the one moment the
# user is guaranteed to be looking at it — and the moment macOS gives them a dialog with no way
# forward. A build with a Developer ID is notarised and needs none of this, so the file is only
# written for the ad-hoc case.
if [ -z "${MACOS_SIGN_IDENTITY:-}" ]; then
  cat > "$DMG_STAGE/HOW TO OPEN THIS APP.txt" <<'NOTE'
Opening Cloudict for the first time
==================================

1. Drag Cloudict onto the Applications folder in this window.
2. Eject this disk image. Do not run the app from inside it.
3. Open Cloudict from Applications.

macOS will refuse the first launch, saying:

    "Cloudict can't be opened because Apple cannot check it
     for malicious software."

That message means this app carries no Apple notarisation ticket, which costs
a paid Developer Program membership. It does not mean anything was found in it.
Cloudict is open source: https://github.com/farshad-zamani/cloudict

To open it anyway, once:

  macOS 15 (Sequoia) and later
    Apple removed the old right-click -> Open shortcut, which is why the
    warning itself offers no way forward. Instead:
      1. Dismiss the warning.
      2. System Settings -> Privacy & Security -> scroll to "Security".
      3. Next to "Cloudict was blocked to protect your Mac", click
         "Open Anyway" and authenticate.

  macOS 14 and earlier
    Right-click Cloudict -> Open -> Open.

  Either version, from Terminal
    xattr -dr com.apple.quarantine /Applications/Cloudict.app

Only the first launch is affected.

Cloudict also needs two things to work:
  * Google Chrome installed.
  * Accessibility permission, so it can type into other apps:
    System Settings -> Privacy & Security -> Accessibility.
    Without it, macOS silently discards every keystroke Cloudict sends.
NOTE
fi

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
echo "=== signature on the packaged app ==="
codesign -dvv "$APP" 2>&1 | sed 's/^/    /' || echo "    UNSIGNED — this build will not run on Apple Silicon"

echo
echo "=== done ==="
ls -lh "$DMG"
