#!/bin/bash
# Builds the Spine widget extension (.appex) and the bridge framework from the Swift sources with
# swiftc alone — no Xcode project — plus the plists the .NET build needs. Invoked by
# Plugin.Maui.Spine.Widgets.targets; every input arrives as an argument.
set -euo pipefail

OUT=""; SOURCES=""; SDK="iphonesimulator"; ARCH="arm64"; MIN_OS="17.0"; CONFIG="Debug"
BUNDLE_ID=""; APP_GROUP=""; NAME="SpineWidgets"; DISPLAY_NAME=""; URL_SCHEME=""; LIVE="true"; BACKGROUND="true"; FREQUENT="false"
PROVISION=""; REQUIRE_PROVISION="false"
WIDGETS=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --out) OUT="$2"; shift 2;;
    --sources) SOURCES="$2"; shift 2;;
    --sdk) SDK="$2"; shift 2;;
    --arch) ARCH="$2"; shift 2;;
    --min-os) MIN_OS="$2"; shift 2;;
    --config) CONFIG="$2"; shift 2;;
    --bundle-id) BUNDLE_ID="$2"; shift 2;;
    --app-group) APP_GROUP="$2"; shift 2;;
    --name) NAME="$2"; shift 2;;
    --display-name) DISPLAY_NAME="$2"; shift 2;;
    --url-scheme) URL_SCHEME="$2"; shift 2;;
    --live-activities) LIVE="$2"; shift 2;;
    --background-refresh) BACKGROUND="$2"; shift 2;;
    --frequent-updates) FREQUENT="$2"; shift 2;;
    --provision) PROVISION="$2"; shift 2;;
    --require-provision) REQUIRE_PROVISION="$2"; shift 2;;
    --widget) WIDGETS+=("$2"); shift 2;;
    *) echo "spine-widgets-build.sh: unknown argument $1" >&2; exit 2;;
  esac
done

[[ -n "$OUT" && -n "$SOURCES" && -n "$BUNDLE_ID" && -n "$APP_GROUP" ]] || { echo "spine-widgets-build.sh: --out, --sources, --bundle-id and --app-group are required" >&2; exit 2; }
[[ ${#WIDGETS[@]} -gt 0 ]] || { echo "spine-widgets-build.sh: at least one --widget is required" >&2; exit 2; }
[[ ${#WIDGETS[@]} -le 9 ]] || { echo "spine-widgets-build.sh: WidgetKit bundles hold at most 10 widgets; Spine reserves one for Live Activities" >&2; exit 2; }

case "$ARCH" in x64) ARCH="x86_64";; esac
case "$SDK" in
  iphonesimulator) TARGET="$ARCH-apple-ios$MIN_OS-simulator"; PLATFORM="iPhoneSimulator";;
  iphoneos) TARGET="$ARCH-apple-ios$MIN_OS"; PLATFORM="iPhoneOS";;
  *) echo "spine-widgets-build.sh: unsupported sdk $SDK" >&2; exit 2;;
esac
case "$CONFIG" in Release) OPT=(-O);; *) OPT=(-Onone -g);; esac

APPEX="$OUT/$NAME.appex"
FRAMEWORK="$OUT/SpineWidgetBridge.framework"
GEN="$OUT/gen"
rm -rf "$APPEX" "$FRAMEWORK" "$GEN"
mkdir -p "$APPEX" "$FRAMEWORK" "$GEN"

json_escape() { printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g'; }
plist_escape() { printf '%s' "$1" | sed -e 's/&/\&amp;/g' -e 's/</\&lt;/g' -e 's/>/\&gt;/g'; }

# --- Manifest read by the extension at runtime ------------------------------------------------
MANIFEST="$APPEX/spine-widgets.json"
{
  printf '{"appGroup":"%s","widgets":[' "$(json_escape "$APP_GROUP")"
  first=1
  for spec in "${WIDGETS[@]}"; do
    IFS='|' read -r kind display description families <<< "$spec"
    [[ -n "$display" ]] || display="$kind"
    fams=""
    IFS=',;' read -ra parts <<< "${families:-Small}"
    for f in "${parts[@]}"; do
      f="$(printf '%s' "$f" | tr -d '[:space:]')"; [[ -n "$f" ]] || continue
      fams+="${fams:+,}\"$(json_escape "$f")\""
    done
    [[ $first -eq 1 ]] || printf ','
    first=0
    printf '{"kind":"%s","displayName":"%s","description":"%s","families":[%s]}' \
      "$(json_escape "$kind")" "$(json_escape "$display")" "$(json_escape "$description")" "$fams"
  done
  printf ']}'
} > "$MANIFEST"

# --- Generated bundle: one Widget struct per declared kind --------------------------------------
BUNDLE="$GEN/SpineWidgetBundle.swift"
{
  echo "import WidgetKit"
  echo "import SwiftUI"
  echo
  for ((i = 0; i < ${#WIDGETS[@]}; i++)); do
    echo "struct SpineWidget_$i: Widget {"
    echo "    var body: some WidgetConfiguration { spineWidgetConfiguration(index: $i) }"
    echo "}"
    echo
  done
  echo "@main"
  echo "struct SpineWidgetBundle: WidgetBundle {"
  echo "    var body: some Widget {"
  for ((i = 0; i < ${#WIDGETS[@]}; i++)); do echo "        SpineWidget_$i()"; done
  [[ "$LIVE" == "true" ]] && echo "        SpineLiveActivity()"
  echo "    }"
  echo "}"
} > "$BUNDLE"

# --- Toolchain facts for the DT keys App Store validation expects --------------------------------
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
		<key>NSExtensionPointIdentifier</key><string>com.apple.widgetkit-extension</string>
	</dict>
</dict>
</plist>
PLIST
} > "$APPEX/Info.plist"

# --- Entitlements: the extension's own, and a host default when the app has none -----------------
# The host app's entitlements are written by the shared step in Plugin.Maui.Spine; only the
# extension's own file is written here.
for target in "$OUT/$NAME.entitlements"; do
  {
    write_plist_header
    cat <<PLIST
	<key>com.apple.security.application-groups</key>
	<array><string>$APP_GROUP</string></array>
</dict>
</plist>
PLIST
  } > "$target"
done

# --- Keys merged into the host app's Info.plist ---------------------------------------------------
{
  write_plist_header
  cat <<PLIST
	<key>SpineWidgetsAppGroup</key><string>$APP_GROUP</string>
	<key>NSSupportsLiveActivities</key><$( [[ "$LIVE" == "true" ]] && echo true || echo false )/>
	<key>NSSupportsLiveActivitiesFrequentUpdates</key><$( [[ "$FREQUENT" == "true" ]] && echo true || echo false )/>
PLIST
  if [[ "$BACKGROUND" == "true" ]]; then
    cat <<PLIST
	<key>UIBackgroundModes</key><array><string>fetch</string></array>
	<key>BGTaskSchedulerPermittedIdentifiers</key><array><string>$(plist_escape "$URL_SCHEME").spine-widgets.refresh</string></array>
PLIST
  fi
  if [[ -n "$URL_SCHEME" ]]; then
    cat <<PLIST
	<key>CFBundleURLTypes</key>
	<array>
		<dict>
			<key>CFBundleURLName</key><string>$(plist_escape "$URL_SCHEME").spine-widgets</string>
			<key>CFBundleURLSchemes</key><array><string>$(plist_escape "$URL_SCHEME")</string></array>
		</dict>
	</array>
PLIST
  fi
  echo "</dict>"
  echo "</plist>"
} > "$OUT/HostManifest.plist"

# --- Bridge framework ------------------------------------------------------------------------------
xcrun -sdk "$SDK" swiftc \
  -target "$TARGET" "${OPT[@]}" -parse-as-library \
  -emit-library -module-name SpineWidgetBridge \
  -framework WidgetKit -framework ActivityKit \
  -Xlinker -install_name -Xlinker @rpath/SpineWidgetBridge.framework/SpineWidgetBridge \
  -o "$FRAMEWORK/SpineWidgetBridge" \
  "$SOURCES/SpineWidgetShared.swift" "$SOURCES/SpineWidgetBridge.swift"
{
  write_plist_header
  cat <<PLIST
	<key>CFBundleExecutable</key><string>SpineWidgetBridge</string>
	<key>CFBundleIdentifier</key><string>$BUNDLE_ID.bridge</string>
	<key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
	<key>CFBundleName</key><string>SpineWidgetBridge</string>
	<key>CFBundlePackageType</key><string>FMWK</string>
	<key>CFBundleShortVersionString</key><string>1.0</string>
	<key>CFBundleVersion</key><string>1</string>
	<key>CFBundleSupportedPlatforms</key><array><string>$PLATFORM</string></array>
	<key>MinimumOSVersion</key><string>$MIN_OS</string>
</dict>
</plist>
PLIST
} > "$FRAMEWORK/Info.plist"

# --- Widget extension --------------------------------------------------------------------------------
# App Intents (the buttons) need a Metadata.appintents bundle beside the binary, which Xcode produces
# from constant values the compiler extracts for the listed protocols. Same two steps here.
printf '["AppIntent","AppEntity","AppEnum","AppShortcutsProvider","AppIntentsPackage","EntityQuery","DynamicOptionsProvider"]' > "$GEN/protocols.json"
EXT_SOURCES=("$SOURCES/SpineWidgetShared.swift" "$SOURCES/SpineWidgetRenderer.swift" "$BUNDLE")
xcrun -sdk "$SDK" swiftc \
  -target "$TARGET" "${OPT[@]}" -parse-as-library -application-extension \
  -module-name "$NAME" \
  -framework WidgetKit -framework SwiftUI -framework ActivityKit -framework AppIntents \
  -wmo -emit-const-values-path "$GEN/$NAME.swiftconstvalues" \
  -Xfrontend -const-gather-protocols-file -Xfrontend "$GEN/protocols.json" \
  -Xlinker -e -Xlinker _NSExtensionMain \
  -Xlinker -rpath -Xlinker @executable_path/../../Frameworks \
  -o "$APPEX/$NAME" \
  "${EXT_SOURCES[@]}"

printf '%s\n' "${EXT_SOURCES[@]}" > "$GEN/sources.txt"
printf '%s\n' "$GEN/$NAME.swiftconstvalues" > "$GEN/constvalues.txt"
xcrun appintentsmetadataprocessor \
  --output "$APPEX" \
  --toolchain-dir "$(xcode-select -p)/Toolchains/XcodeDefault.xctoolchain" \
  --module-name "$NAME" \
  --sdk-root "$(xcrun --sdk "$SDK" --show-sdk-path)" \
  --xcode-version "$XCODE_BUILD" \
  --platform-family iOS \
  --deployment-target "$MIN_OS" \
  --target-triple "$TARGET" \
  --source-file-list "$GEN/sources.txt" \
  --swift-const-vals-list "$GEN/constvalues.txt" \
  --force --quiet-warnings

# swiftc -g drops a dSYM beside each product; keep it out of the bundles the SDK signs and ships.
rm -rf "$OUT/dSYM"; mkdir -p "$OUT/dSYM"
for d in "$APPEX"/*.dSYM "$FRAMEWORK"/*.dSYM; do [[ -e "$d" ]] && mv "$d" "$OUT/dSYM/"; done

# --- The extension's own provisioning profile ---------------------------------------------------
# The .NET iOS SDK embeds a profile into the app bundle only (_EmbedProvisionProfile writes
# $(_AppBundlePath)embedded.mobileprovision); nothing does it for an AdditionalAppExtensions bundle.
# Without one inside the .appex the whole app fails to install with 0xe8008015, naming the app and
# not the extension. So Spine puts it there, since Spine is what generates the bundle.
#
# Only an exact match counts. The extension carries an App Group entitlement, and a wildcard App ID
# cannot enable App Groups, so a wildcard profile that "matches" would fail at signing anyway.
find_profile() {
  local dir="$HOME/Library/MobileDevice/Provisioning Profiles"
  local best="" best_expiry="" p plist name appid expiry

  [[ -d "$dir" ]] || return 1

  for p in "$dir"/*.mobileprovision; do
    [[ -e "$p" ]] || continue
    plist=$(security cms -D -i "$p" 2>/dev/null) || continue

    appid=$(printf '%s' "$plist" | plutil -extract Entitlements.application-identifier raw - 2>/dev/null) || continue
    # application-identifier is <TeamID>.<bundle id>, and a team id holds no dots.
    [[ "${appid#*.}" == "$BUNDLE_ID" ]] || continue

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
    echo "spine-widgets-build.sh: embedded $(basename "$PROFILE") for $BUNDLE_ID"
  elif [[ "$REQUIRE_PROVISION" == "true" ]]; then
    {
      echo "spine-widgets-build.sh: no provisioning profile for the widget extension."
      echo ""
      echo "  The extension is a bundle of its own and needs its own App ID and profile:"
      echo ""
      echo "    App ID     $BUNDLE_ID"
      echo "    App Group  $APP_GROUP, enabled on that App ID and on the app's"
      echo "    Profile    a development profile for it, including the device"
      echo ""
      if [[ -n "$PROVISION" ]]; then
        echo "  SpineWidgetsCodesignProvision is '$PROVISION'; no installed profile has that name"
        echo "  and that App ID. Leave it empty to take whichever installed profile matches."
      else
        echo "  Install it, then build again. Name a specific one with SpineWidgetsCodesignProvision."
      fi
      echo "  Set SpineWidgetsEnabled=false to build this app without widgets."
    } >&2
    exit 3
  fi
fi

# Sign here, even though the SDK signs again after copying these into the app bundle: it copies and
# signs in two separate steps, so an unsigned bundle in obj/ is one interrupted build away from an
# app that dies in dyld with "Code Signature Invalid". Ad hoc is enough — a device build re-signs
# with the real identity, and the profile embedded above survives that.
codesign --force --sign - --timestamp=none "$FRAMEWORK"
codesign --force --sign - --timestamp=none --entitlements "$OUT/$NAME.entitlements" "$APPEX"

date +%s > "$OUT/build.stamp"
echo "spine-widgets-build.sh: built $APPEX and $FRAMEWORK for $TARGET"
