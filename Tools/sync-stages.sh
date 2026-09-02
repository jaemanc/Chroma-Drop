#!/usr/bin/env bash
# sync-stages.sh — stages/ 가 원본이고, 빌드에 들어가는 사본은 StreamingAssets 다.
# 밸런싱은 stages/ 만 고치면 되고, 이 스크립트가 사본을 맞춘다.
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p Assets/StreamingAssets/stages
cp stages/stages.json stages/stage-schema.json stages/curve.config.json Assets/StreamingAssets/stages/
echo "stages/ → Assets/StreamingAssets/stages/ 동기화"
