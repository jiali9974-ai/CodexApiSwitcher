#!/usr/bin/env sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
WORKSPACE=$(dirname "$SCRIPT_DIR")
PROJECT="$WORKSPACE/src/CodexApiSwitcher/CodexApiSwitcher.csproj"
RID=${1:-}
export DOTNET_CLI_HOME=${DOTNET_CLI_HOME:-"$WORKSPACE/.dotnet-home"}
export NUGET_PACKAGES=${NUGET_PACKAGES:-"$WORKSPACE/.nuget/packages"}

if [ -x "$WORKSPACE/.dotnet/dotnet" ]; then
  DOTNET="$WORKSPACE/.dotnet/dotnet"
else
  DOTNET=dotnet
fi

if [ -z "$RID" ]; then
  case "$(uname -s)-$(uname -m)" in
    Darwin-arm64) RID=osx-arm64 ;;
    Darwin-x86_64) RID=osx-x64 ;;
    Linux-aarch64) RID=linux-arm64 ;;
    Linux-x86_64) RID=linux-x64 ;;
    *) echo "Unsupported OS/architecture. Pass a .NET RID explicitly." >&2; exit 1 ;;
  esac
fi

DESTINATION="$WORKSPACE/outputs/cross-platform/$RID"
"$DOTNET" publish "$PROJECT" \
  --configuration Release \
  --runtime "$RID" \
  --self-contained true \
  --output "$DESTINATION" \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=None \
  -p:DebugSymbols=false

chmod +x "$DESTINATION/CodexApiSwitcher"
case "$RID" in
osx-*)
  BUNDLE="$DESTINATION/Codex API Switcher.app"
  CONTENTS="$BUNDLE/Contents"
  MACOS="$CONTENTS/MacOS"
  RESOURCES="$CONTENTS/Resources"
  rm -rf "$BUNDLE"
  mkdir -p "$MACOS" "$RESOURCES"
  cp "$DESTINATION/CodexApiSwitcher" "$MACOS/CodexApiSwitcher"
  cp "$WORKSPACE/outputs/cas-logo.ico" "$RESOURCES/cas-logo.ico"
  cp "$WORKSPACE/outputs/cas-logo.icns" "$RESOURCES/cas-logo.icns"
  chmod +x "$MACOS/CodexApiSwitcher"
  cat > "$CONTENTS/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Codex API Switcher</string>
  <key>CFBundleDisplayName</key><string>Codex API Switcher</string>
  <key>CFBundleIdentifier</key><string>com.codex-api-switcher.desktop</string>
  <key>CFBundleExecutable</key><string>CodexApiSwitcher</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>2.1.0</string>
  <key>CFBundleVersion</key><string>2.1.0</string>
  <key>CFBundleIconFile</key><string>cas-logo.icns</string>
  <key>CFBundleDevelopmentRegion</key><string>zh_CN</string>
  <key>LSApplicationCategoryType</key><string>public.app-category.developer-tools</string>
  <key>LSMultipleInstancesProhibited</key><true/>
  <key>NSHighResolutionCapable</key><true/>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
</dict>
</plist>
PLIST
  plutil -lint "$CONTENTS/Info.plist"
  if otool -L "$MACOS/CodexApiSwitcher" | grep -E '/opt/homebrew|/usr/local' >/dev/null; then
    echo "Refusing to ship a macOS app with Homebrew/local dynamic library dependencies:" >&2
    otool -L "$MACOS/CodexApiSwitcher" >&2
    exit 1
  fi
  xattr -cr "$BUNDLE" 2>/dev/null || true
  codesign --force --deep --sign - --timestamp=none "$BUNDLE"
  codesign --verify --deep --strict "$BUNDLE"
  ;;
esac
echo "Published: $DESTINATION/CodexApiSwitcher"

case "$RID" in
  win-x64)
    cp "$DESTINATION/CodexApiSwitcher.exe" "$WORKSPACE/CodexApiSwitcher-win-x64.exe"
    ;;
  linux-x64|linux-arm64)
    cp "$DESTINATION/CodexApiSwitcher" "$WORKSPACE/CodexApiSwitcher-$RID"
    chmod +x "$WORKSPACE/CodexApiSwitcher-$RID"
    ;;
  osx-arm64)
    rm -rf "$WORKSPACE/CodexApiSwitcher-macos-arm64.app"
    cp -R "$DESTINATION/Codex API Switcher.app" "$WORKSPACE/CodexApiSwitcher-macos-arm64.app"
    chmod +x "$WORKSPACE/CodexApiSwitcher-macos-arm64.app/Contents/MacOS/CodexApiSwitcher"
    rm -f "$WORKSPACE/CodexApiSwitcher-macos-arm64.zip"
    ditto -c -k --sequesterRsrc --keepParent "$WORKSPACE/CodexApiSwitcher-macos-arm64.app" "$WORKSPACE/CodexApiSwitcher-macos-arm64.zip"
    ;;
  osx-x64)
    rm -rf "$WORKSPACE/CodexApiSwitcher-macos-x64.app"
    cp -R "$DESTINATION/Codex API Switcher.app" "$WORKSPACE/CodexApiSwitcher-macos-x64.app"
    chmod +x "$WORKSPACE/CodexApiSwitcher-macos-x64.app/Contents/MacOS/CodexApiSwitcher"
    rm -f "$WORKSPACE/CodexApiSwitcher-macos-x64.zip"
    ditto -c -k --sequesterRsrc --keepParent "$WORKSPACE/CodexApiSwitcher-macos-x64.app" "$WORKSPACE/CodexApiSwitcher-macos-x64.zip"
    ;;
esac
