#!/usr/bin/env bash
set -euo pipefail

IMAGE="${1:-maoraw/minedash:latest}"
PUSH="${PUSH:-0}"
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

if ! docker info >/dev/null 2>&1; then
  echo "Docker isn't currently running. Please ensure Docker is installed and running." >&2
  exit 1
fi

echo "Building ${IMAGE} ..."
docker build -t "${IMAGE}" .

if [[ "${PUSH}" == "1" || "${PUSH}" == "true" ]]; then
  echo "Pushing ${IMAGE} ..."
  docker push "${IMAGE}"
fi

echo "Done."
