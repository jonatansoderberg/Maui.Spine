#!/bin/zsh
set -e
S=$(cd "$(dirname "$0")" && pwd)
FW=$S/out/SpineWidgetBridge.framework
rm -rf "$FW"; mkdir -p "$FW"
xcrun -sdk iphonesimulator swiftc \
  -target arm64-apple-ios17.0-simulator \
  -emit-library -O -module-name SpineWidgetBridge \
  -framework WidgetKit -framework ActivityKit \
  -Xlinker -install_name -Xlinker @rpath/SpineWidgetBridge.framework/SpineWidgetBridge \
  -o "$FW/SpineWidgetBridge" \
  "$S/Bridge/SpineWidgetBridge.swift"
cp "$S/Bridge/Info.plist" "$FW/Info.plist"
echo "built $FW"; nm -g "$FW/SpineWidgetBridge" | grep -i "OBJC_CLASS_\$_SpineWidgetBridge" 
