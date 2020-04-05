#!/bin/sh

TARGET_DIR="/mnt/c/Program Files (x86)/Steam/steamapps/common/RimWorld/Mods/TerrainMovementKit"
rm -rf "${TARGET_DIR}"
mkdir "${TARGET_DIR}"
cp -r About "${TARGET_DIR}"
cp -r 1.1 "${TARGET_DIR}"
