#!/bin/zsh
# Builds the lab for the owner's iPhone (Release with the harness), installs and launches it, retrying
# while the phone is unreachable (it connects over the network; it must be unlocked).
cd "$(dirname "$0")/../.."
export PATH=/usr/local/share/dotnet:$PATH MD_APPLE_SDK_ROOT=/Applications/Xcode-26.2.0.app
DEVICE=${DEVICE:-00008140-001215800A98801C}
dotnet build -f net10.0-ios -c Release -r ios-arm64 -p:LabHarness=true \
  -p:CodesignKey="Apple Development: Jonatan Soderberg (43NSER2TDB)" \
  -p:CodesignProvision="iOS Team Provisioning Profile: se.cosmomedia.*" 2>&1 | grep -E " error |Build succeeded" | sort -u
for i in $(seq 1 ${TRIES:-60}); do
  if xcrun devicectl device install app --device $DEVICE bin/Release/net10.0-ios/ios-arm64/SpineAvatarLab.app >/dev/null 2>&1; then
    xcrun devicectl device process launch --device $DEVICE se.cosmomedia.spineavatarlab >/dev/null 2>&1; echo "installed"; exit 0
  fi
  sleep 20
done
echo "phone not reachable"; exit 1
