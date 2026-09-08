#!/usr/bin/env bash
#
# Builds the Linux packages: .deb, .rpm and an AppImage.
#
# Run on Linux (or WSL) after "dotnet publish -r linux-x64". Each package installs the same
# self-contained payload into /opt/cloudict with a launcher on PATH; only the metadata differs.
#
#   ./build-linux-packages.sh <publish-dir> <version> [output-dir]
#
set -euo pipefail

PUBLISH_DIR="${1:?usage: build-linux-packages.sh <publish-dir> <version> [output-dir]}"
VERSION="${2:?missing version}"
OUT_DIR="${3:-$(pwd)/dist}"

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"

mkdir -p "$OUT_DIR"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

echo "=== staging payload ==="
install -d "$STAGE/opt/cloudict"
cp -r "$PUBLISH_DIR"/. "$STAGE/opt/cloudict/"
chmod +x "$STAGE/opt/cloudict/Cloudict"

# The bundled driver arrives from a zip, which carries no execute bit.
find "$STAGE/opt/cloudict/Drivers" -name chromedriver -exec chmod +x {} \; 2>/dev/null || true

install -d "$STAGE/usr/bin"
ln -sf /opt/cloudict/Cloudict "$STAGE/usr/bin/cloudict"

install -d "$STAGE/usr/share/applications"
cp "$HERE/cloudict.desktop" "$STAGE/usr/share/applications/"

install -d "$STAGE/usr/share/icons/hicolor/512x512/apps"
cp "$REPO_ROOT/src/Cloudict.App/Assets/logo.png" "$STAGE/usr/share/icons/hicolor/512x512/apps/cloudict.png"

install -d "$STAGE/usr/share/doc/cloudict"
cp "$REPO_ROOT/LICENSE" "$STAGE/usr/share/doc/cloudict/"

SIZE_KB="$(du -sk "$STAGE" | cut -f1)"

# ---------------------------------------------------------------------------- deb
if command -v dpkg-deb >/dev/null 2>&1; then
  echo "=== building .deb ==="
  DEB_STAGE="$(mktemp -d)"
  cp -r "$STAGE"/. "$DEB_STAGE/"
  install -d "$DEB_STAGE/DEBIAN"

  # The alternations in Depends are not decoration. Two families of package have been renamed
  # underneath us and the name that works depends entirely on the distribution release:
  #
  #   libicu*   — carries its ABI version in its name, so every release ships a different one.
  #               Listed newest first; apt takes the first that exists.
  #   libpng16-16t64 — Ubuntu's 64-bit time_t transition renamed the package. On 24.04 and later
  #               the old name is virtual with no installation candidate, so depending on it alone
  #               is not reliably satisfiable; on Debian 12 and Ubuntu 22.04 only the old name
  #               exists. Both are listed, new name first.
  #
  # ICU is not optional here: the build sets InvariantGlobalization=false, which makes the .NET
  # runtime refuse to start without it.
  cat > "$DEB_STAGE/DEBIAN/control" <<EOF
Package: cloudict
Version: $VERSION
Section: utils
Priority: optional
Architecture: amd64
Maintainer: Farshad Zamani <farshad.z1992@gmail.com>
Installed-Size: $SIZE_KB
Depends: libx11-6, libxtst6, libice6, libsm6, libfontconfig1, libfreetype6, libexpat1, libpng16-16t64 | libpng16-16, zlib1g, libbz2-1.0, libstdc++6, ca-certificates, libicu76 | libicu74 | libicu72 | libicu71 | libicu70 | libicu67 | libicu66
Recommends: google-chrome-stable, ydotool
Homepage: https://cloudtart.com
Description: Free voice typing powered by Google's speech recognition
 Cloudict turns speech into text and types it into whatever application has
 focus. It drives the public Google Translate voice input through a helper
 Chrome window, so it needs no API key, no account and no local model.
 .
 Chrome is required and is not installed automatically. On Wayland, typing into
 other applications additionally needs ydotool with its service enabled; on X11
 nothing further is needed. Run "cloudict --diagnose" to see what this system
 supports.
EOF

  # ydotool is only a Recommends: X11 users need nothing extra, so a hard dependency would be wrong.
  cat > "$DEB_STAGE/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database -q /usr/share/applications || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor || true
fi
exit 0
EOF
  chmod 0755 "$DEB_STAGE/DEBIAN/postinst"

  dpkg-deb --build --root-owner-group "$DEB_STAGE" "$OUT_DIR/cloudict_${VERSION}_amd64.deb" >/dev/null
  rm -rf "$DEB_STAGE"
  echo "  $OUT_DIR/cloudict_${VERSION}_amd64.deb"
else
  echo "!! dpkg-deb not found, skipping .deb"
fi

# ---------------------------------------------------------------------------- rpm
if command -v rpmbuild >/dev/null 2>&1; then
  echo "=== building .rpm ==="
  RPM_TOP="$(mktemp -d)"
  mkdir -p "$RPM_TOP"/{BUILD,RPMS,SOURCES,SPECS,BUILDROOT}

  cat > "$RPM_TOP/SPECS/cloudict.spec" <<EOF
Name:           cloudict
Version:        $VERSION
Release:        1
Summary:        Free voice typing powered by Google's speech recognition
License:        MIT
URL:            https://cloudtart.com
BuildArch:      x86_64
Requires:       libX11, libXtst, libICE, libSM, fontconfig, freetype, expat, libpng, zlib, libstdc++, ca-certificates, libicu
Recommends:     google-chrome-stable
AutoReqProv:    no

%description
Cloudict turns speech into text and types it into whatever application has
focus, driving the public Google Translate voice input through a helper Chrome
window. Chrome is required and is not installed automatically. On Wayland,
typing into other applications additionally needs ydotool.

%install
mkdir -p %{buildroot}
cp -r $STAGE/. %{buildroot}/

%files
/opt/cloudict
/usr/bin/cloudict
/usr/share/applications/cloudict.desktop
/usr/share/icons/hicolor/512x512/apps/cloudict.png
/usr/share/doc/cloudict/LICENSE

%changelog
EOF

  rpmbuild --define "_topdir $RPM_TOP" -bb "$RPM_TOP/SPECS/cloudict.spec" >/dev/null 2>&1
  find "$RPM_TOP/RPMS" -name '*.rpm' -exec cp {} "$OUT_DIR/" \;
  rm -rf "$RPM_TOP"
  echo "  $(ls "$OUT_DIR"/*.rpm 2>/dev/null | tail -1)"
else
  echo "!! rpmbuild not found, skipping .rpm"
fi

# ---------------------------------------------------------------------------- AppImage
# Covers every distribution the two package formats miss, and needs no installation at all.
APPIMAGETOOL="${APPIMAGETOOL:-$(command -v appimagetool || true)}"
if [ -n "$APPIMAGETOOL" ]; then
  echo "=== building AppImage ==="
  APPDIR="$(mktemp -d)/Cloudict.AppDir"
  install -d "$APPDIR"
  cp -r "$STAGE/opt/cloudict" "$APPDIR/usr"
  install -d "$APPDIR/usr/bin"

  cp "$HERE/cloudict.desktop" "$APPDIR/cloudict.desktop"
  sed -i 's|^Exec=.*|Exec=Cloudict|' "$APPDIR/cloudict.desktop"
  sed -i 's|^Exec=/opt/cloudict/Cloudict --toggle|Exec=Cloudict --toggle|' "$APPDIR/cloudict.desktop"
  sed -i 's|^Exec=/opt/cloudict/Cloudict --stop|Exec=Cloudict --stop|' "$APPDIR/cloudict.desktop"
  cp "$REPO_ROOT/src/Cloudict.App/Assets/logo.png" "$APPDIR/cloudict.png"

  cat > "$APPDIR/AppRun" <<'EOF'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"

# The .deb and .rpm can declare that they need ICU and let the package manager install it. An
# AppImage has no such mechanism: it runs on whatever the machine happens to have. Without ICU,
# .NET does not start at all — it exits with "Couldn't find a valid ICU package installed on the
# system" before any of Cloudict's own code runs, which reads to the user as the app doing nothing.
#
# So look first, and fall back to invariant globalization if it is genuinely absent. That costs
# culture-aware sorting and casing, which this app barely uses, and buys an application that starts.
if ! ls /usr/lib/libicuuc.so* /usr/lib64/libicuuc.so*       /usr/lib/*/libicuuc.so* /lib/*/libicuuc.so* >/dev/null 2>&1; then
  export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
fi

exec "$HERE/usr/Cloudict" "$@"
EOF
  chmod +x "$APPDIR/AppRun"

  ARCH=x86_64 "$APPIMAGETOOL" "$APPDIR" "$OUT_DIR/Cloudict-${VERSION}-x86_64.AppImage" >/dev/null 2>&1
  echo "  $OUT_DIR/Cloudict-${VERSION}-x86_64.AppImage"
else
  echo "!! appimagetool not found, skipping AppImage"
fi

echo
echo "=== packages in $OUT_DIR ==="
ls -lh "$OUT_DIR" | tail -n +2
