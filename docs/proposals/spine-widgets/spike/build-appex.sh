#!/bin/zsh
set -e
S=$(cd "$(dirname "$0")" && pwd)
OUT=$S/out/SpineWidget.appex
rm -rf "$OUT"; mkdir -p "$OUT"
xcrun -sdk iphonesimulator swiftc \
  -target arm64-apple-ios17.0-simulator \
  -parse-as-library -O -application-extension \
  -module-name SpineWidget \
  -framework WidgetKit -framework SwiftUI -framework ActivityKit \
  -Xlinker -e -Xlinker _NSExtensionMain \
  -Xlinker -rpath -Xlinker @executable_path/../../Frameworks \
  -o "$OUT/SpineWidget" \
  "$S/SpineWidget/SpineWidget.swift"
cp "$S/SpineWidget/Info.plist" "$OUT/Info.plist"
echo "built $OUT"; ls -la "$OUT"
