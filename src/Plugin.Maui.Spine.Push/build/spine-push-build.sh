#!/bin/bash
# Builds Spine's Notification Service Extension (.appex) from its Swift source with swiftc alone — no
# Xcode project. The same shape as Plugin.Maui.Spine.Widgets' spine-widgets-build.sh; invoked by
# Plugin.Maui.Spine.Push.targets when SpinePushImages is on, with every input as an argument.
set -euo pipefail

OUT=""; SOURCES=""; SDK="iphonesimulator"; ARCH="arm64"; MIN_OS="15.0"; CONFIG="Debug"
BUNDLE_ID=""; NAME="SpineNotificationService"; DISPLAY_NAME=""; PROVISION=""; REQUIRE_PROVISION="false"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --out) OUT="$2"; shift 2;;
    --sources) SOURCES="$2"; shift 2;;
    --sdk) SDK="$2"; shift 2;;
    --arch) ARCH="$2"; shift 2;;
    --min-os) MIN_OS="$2"; shift 2;;
    --config) CONFIG="$2"; shift 2;;
    --bundle-id) BUNDLE_ID="$2"; shift 2;;
    --name) NAME="$2"; shift 2;;
    --display-name) DISPLAY_NAME="$2"; shift 2;;
    --provision) PROVISION="$2"; shift 2;;
    --require-provision) REQUIRE_PROVISION="$2"; shift 2;;
    *) echo "spine-push-build.sh: unknown argument $1" >&2; exit 2;;
  esac
done

[[ -n "$OUT" && -n "$SOURCES" && -n "$BUNDLE_ID" ]] || { echo "spine-push-build.sh: --out, --sources and --bundle-id are required" >&2; exit 2; }

case "$ARCH" in x64) ARCH="x86_64";; esac
case "$SDK" in
  iphonesimulator) TARGET="$ARCH-apple-ios$MIN_OS-simulator"; PLATFORM="iPhoneSimulator";;
  iphoneos) TARGET="$ARCH-apple-ios$MIN_OS"; PLATFORM="iPhoneOS";;
  *) echo "spine-push-build.sh: unsupported sdk $SDK" >&2; exit 2;;
esac
case "$CONFIG" in Release) OPT=(-O);; *) OPT=(-Onone -g);; esac

APPEX="$OUT/$NAME.appex"
rm -rf "$APPEX"
mkdir -p "$APPEX"

plist_escape() { printf '%s' "$1" | sed -e 's/&/\&amp;/g' -e 's/</\&lt;/g' -e 's/>/\&gt;/g'; }

XCODE_VERSION="$(xcodebuild -version | awk 'NR==1 {print $2}')"
XCODE_BUILD="$(xcodebuild -version | awk 'NR==2 {print $3}')"
SDK_VERSION="$(xcrun --sdk "$SDK" --show-sdk-version)"
SDK_BUILD="$(xcrun --sdk "$SDK" --show-sdk-build-version)"
MACHINE_BUILD="$(sw_vers -buildVersion)"
IFS='.' read -r xmaj xmin <<< "$XCODE_VERSION"
DT_XCODE="$(printf '%02d%d0' "$xmaj" "${xmin:-0}")"

write_plist_header() {
  cat <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
PLIST
}

# --- Extension Info.plist ------------------------------------------------------------------------
# The principal class is named with @objc in the Swift source, so it is not module-qualified here.
{
  write_plist_header
  cat <<PLIST
	<key>CFBundleDevelopmentRegion</key><string>en</string>
	<key>CFBundleDisplayName</key><string>$(plist_escape "${DISPLAY_NAME:-$NAME}")</string>
	<key>CFBundleExecutable</key><string>$NAME</string>
	<key>CFBundleIdentifier</key><string>$BUNDLE_ID</string>
	<key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
	<key>CFBundleName</key><string>$NAME</string>
	<key>CFBundlePackageType</key><string>XPC!</string>
	<key>CFBundleShortVersionString</key><string>1.0</string>
	<key>CFBundleVersion</key><string>1</string>
	<key>CFBundleSupportedPlatforms</key><array><string>$PLATFORM</string></array>
	<key>MinimumOSVersion</key><string>$MIN_OS</string>
	<key>UIDeviceFamily</key><array><integer>1</integer><integer>2</integer></array>
	<key>DTCompiler</key><string>com.apple.compilers.llvm.clang.1_0</string>
	<key>DTPlatformName</key><string>$SDK</string>
	<key>DTPlatformVersion</key><string>$SDK_VERSION</string>
	<key>DTPlatformBuild</key><string>$SDK_BUILD</string>
	<key>DTSDKName</key><string>$SDK$SDK_VERSION</string>
	<key>DTSDKBuild</key><string>$SDK_BUILD</string>
	<key>DTXcode</key><string>$DT_XCODE</string>
	<key>DTXcodeBuild</key><string>$XCODE_BUILD</string>
	<key>BuildMachineOSBuild</key><string>$MACHINE_BUILD</string>
	<key>NSExtension</key>
	<dict>
		<key>NSExtensionPointIdentifier</key><string>com.apple.usernotifications.service</string>
		<key>NSExtensionPrincipalClass</key><string>SpineNotificationService</string>
	</dict>
</dict>
</plist>
PLIST
} > "$APPEX/Info.plist"

# --- Entitlements: none needed, but the SDK signs the extension with a file ------------------------
{
  write_plist_header
  echo "</dict>"
  echo "</plist>"
} > "$OUT/$NAME.entitlements"

# --- The extension ---------------------------------------------------------------------------------
xcrun -sdk "$SDK" swiftc \
  -target "$TARGET" "${OPT[@]}" -parse-as-library -application-extension \
  -module-name "$NAME" \
  -framework UserNotifications \
  -Xlinker -e -Xlinker _NSExtensionMain \
  -o "$APPEX/$NAME" \
  "$SOURCES/SpineNotificationService.swift"

# swiftc -g drops a dSYM beside the product; keep it out of the bundle the SDK signs and ships.
rm -rf "$OUT/dSYM"; mkdir -p "$OUT/dSYM"
for d in "$APPEX"/*.dSYM; do [[ -e "$d" ]] && mv "$d" "$OUT/dSYM/"; done

# --- The extension's own provisioning profile ---------------------------------------------------
# As for the widget extension: the .NET iOS SDK embeds a profile into the app bundle only, and an
# extension without one makes the whole app fail to install with 0xe8008015. This extension carries
# no entitlements, so a wildcard App ID would do — but an exact one is what a team normally has, and
# it is what is looked for first.
find_profile() {
  local dir="$HOME/Library/MobileDevice/Provisioning Profiles"
  local best="" best_expiry="" p plist name appid expiry id

  [[ -d "$dir" ]] || return 1

  for p in "$dir"/*.mobileprovision; do
    [[ -e "$p" ]] || continue
    plist=$(security cms -D -i "$p" 2>/dev/null) || continue

    appid=$(printf '%s' "$plist" | plutil -extract Entitlements.application-identifier raw - 2>/dev/null) || continue
    # application-identifier is <TeamID>.<bundle id>, and a team id holds no dots.
    id="${appid#*.}"
    [[ "$id" == "$BUNDLE_ID" ]] || continue

    if [[ -n "$PROVISION" ]]; then
      name=$(printf '%s' "$plist" | plutil -extract Name raw - 2>/dev/null) || continue
      [[ "$name" == "$PROVISION" ]] || continue
    fi

    expiry=$(printf '%s' "$plist" | plutil -extract ExpirationDate raw - 2>/dev/null) || continue
    if [[ -z "$best" || "$expiry" > "$best_expiry" ]]; then best="$p"; best_expiry="$expiry"; fi
  done

  [[ -n "$best" ]] || return 1
  printf '%s' "$best"
}

if [[ "$SDK" == "iphoneos" ]]; then
  if PROFILE=$(find_profile); then
    cp "$PROFILE" "$APPEX/embedded.mobileprovision"
    echo "spine-push-build.sh: embedded $(basename "$PROFILE") for $BUNDLE_ID"
  elif [[ "$REQUIRE_PROVISION" == "true" ]]; then
    {
      echo "spine-push-build.sh: no provisioning profile for the Notification Service Extension."
      echo ""
      echo "  SpinePushImages=true adds an extension that fetches pictures for pushed notifications."
      echo "  It is a bundle of its own and needs its own App ID and profile:"
      echo ""
      echo "    App ID     $BUNDLE_ID"
      echo "    Profile    a development profile for it, including the device"
      echo ""
      if [[ -n "$PROVISION" ]]; then
        echo "  SpinePushImagesCodesignProvision is '$PROVISION'; no installed profile has that name"
        echo "  and that App ID. Leave it empty to take whichever installed profile matches."
      else
        echo "  Install it, then build again. Name a specific one with SpinePushImagesCodesignProvision."
      fi
      echo "  Set SpinePushImages=false to build without pictures in pushed notifications on iOS."
    } >&2
    exit 3
  fi
fi

# Signed here as well as by the SDK after copying, for the reason spine-widgets-build.sh gives: an
# unsigned bundle in obj/ is one interrupted build away from an app that dies in dyld.
codesign --force --sign - --timestamp=none --entitlements "$OUT/$NAME.entitlements" "$APPEX"

date +%s > "$OUT/build.stamp"
echo "spine-push-build.sh: built $APPEX for $TARGET"
