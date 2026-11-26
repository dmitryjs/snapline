#!/bin/sh
set -eu
cd "$(dirname "$0")"
bundle="build-intel/Snapline.app/Contents"
mkdir -p "$bundle/MacOS" "$bundle/Resources"
xcrun clang -O2 -fobjc-arc -fblocks -arch x86_64 -mmacosx-version-min=10.15 -DNAVODKA_INTEL_COMPAT -framework Cocoa -framework CoreMedia -framework CoreGraphics -framework AVFoundation -framework AVKit -framework CoreImage main.m -o "$bundle/MacOS/Snapline"
cp Info-Intel.plist "$bundle/Info.plist"
cp Snapline.icns "$bundle/Resources/"
codesign --force --sign - "build-intel/Snapline.app"
codesign --verify --strict "build-intel/Snapline.app"
