#!/bin/bash
set -eu
cd "$(dirname "$0")"
bundle=build-universal/Snapline.app
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
mkdir -p "$bundle/Contents/MacOS" "$bundle/Contents/Resources"
for arch in x86_64 arm64; do
    min=10.15
    if [[ $arch == arm64 ]]; then min=11.0; fi
    xcrun clang -O2 -fobjc-arc -fblocks -arch "$arch" -mmacosx-version-min="$min" -DNAVODKA_INTEL_COMPAT -framework Cocoa -framework CoreMedia -framework CoreGraphics -framework AVFoundation -framework AVKit -framework CoreImage main.m -o "$tmp/$arch"
done
xcrun lipo -create "$tmp/x86_64" "$tmp/arm64" -output "$bundle/Contents/MacOS/Snapline"
cp Info-Intel.plist "$bundle/Contents/Info.plist"
cp Snapline.icns "$bundle/Contents/Resources/"
cp -R en.lproj ru.lproj "$bundle/Contents/Resources/"
codesign --force --sign - "$bundle"
codesign --verify --strict "$bundle"
