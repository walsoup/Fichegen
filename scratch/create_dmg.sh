#!/bin/bash
set -e

APP_NAME="FicheGen"
DMG_NAME="FicheGen-Installer.dmg"
TEMP_DMG="temp_$DMG_NAME"
VOLUME_NAME="FicheGen Installer"
DESKTOP_DIR="/Users/wal/Desktop"
FINAL_DMG="$DESKTOP_DIR/$DMG_NAME"

# Build paths
APP_PATH="BuildData/Build/Products/Debug/$APP_NAME.app"
BACKGROUND_PNG="BuildData/Build/Products/Debug/dmg_background.png"

echo "=== Step 1: Generating custom background image ==="
mkdir -p "BuildData/Build/Products/Debug"
xcrun -sdk macosx swiftc scratch/generate_background.swift -o scratch/generate_background
./scratch/generate_background "$BACKGROUND_PNG"
rm -f scratch/generate_background

echo "=== Step 2: Preparing staging directory ==="
STAGING_DIR="dmg_staging"
rm -rf "$STAGING_DIR"
mkdir -p "$STAGING_DIR"

# Copy App to staging
cp -R "$APP_PATH" "$STAGING_DIR/"

# Create symlink to Applications in staging
ln -s /Applications "$STAGING_DIR/Applications"

echo "=== Step 3: Creating raw disk image ==="
rm -f "$TEMP_DMG" "$FINAL_DMG"
# Create a raw read/write DMG of 50MB
hdiutil create -size 50m -fs HFS+ -volname "$VOLUME_NAME" -ov "$TEMP_DMG"

echo "=== Step 4: Mounting disk image ==="
MOUNT_INFO=$(hdiutil attach -readwrite -noverify "$TEMP_DMG")
DEV_NAME=$(echo "$MOUNT_INFO" | grep "/dev/disk" | head -n 1 | cut -f 1 -d ' ')
MOUNT_POINT=$(echo "$MOUNT_INFO" | grep -o '/Volumes/.*')

echo "Device: $DEV_NAME"
echo "Mount point: $MOUNT_POINT"

echo "=== Step 5: Copying files to mounted volume ==="
cp -R "$STAGING_DIR/$APP_NAME.app" "$MOUNT_POINT/"
ln -s /Applications "$MOUNT_POINT/Applications"

# Create hidden background directory and copy background.png
mkdir "$MOUNT_POINT/.background"
cp "$BACKGROUND_PNG" "$MOUNT_POINT/.background/background.png"

echo "=== Step 6: Setting custom Finder layout via AppleScript ==="
# Open, configure window style, set background picture, move icons, and close.
osascript <<EOF || true
tell application "Finder"
    tell disk "$VOLUME_NAME"
        open
        delay 1
        set the_window to container window
        set current view of the_window to icon view
        set toolbar visible of the_window to false
        set statusbar visible of the_window to false
        set the bounds of the_window to {400, 100, 1000, 500} -- 600x400 size
        
        set arrangement of icon view options of the_window to not arranged
        set icon size of icon view options of the_window to 110
        
        -- Set background picture using POSIX file reference
        set background picture of icon view options of the_window to POSIX file "/Volumes/$VOLUME_NAME/.background/background.png"
        
        -- Position the icons
        set position of item "$APP_NAME.app" of the_window to {150, 180}
        set position of item "Applications" of the_window to {450, 180}
        
        update target of the_window
        delay 3
        close the_window
    end tell
end tell
EOF

# Give Finder a moment to write to the .DS_Store
sleep 5

echo "=== Step 7: Cleaning permissions and files ==="
chmod -Rf go-w "$MOUNT_POINT" || true

echo "=== Step 8: Detaching disk image ==="
hdiutil detach "$DEV_NAME"

echo "=== Step 9: Converting image to read-only compressed DMG ==="
hdiutil convert "$TEMP_DMG" -format UDZO -imagekey zlib-level=9 -o "$FINAL_DMG"

echo "=== Step 10: Cleaning up temp files ==="
rm -f "$TEMP_DMG"
rm -rf "$STAGING_DIR"

echo "=== DMG successfully created at $FINAL_DMG ==="
