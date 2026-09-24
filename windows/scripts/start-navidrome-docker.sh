#!/bin/sh
# Starts Navidrome (Subsonic API) in Docker with the test library (Linux CI) and waits until the scan finished.
# Usage: start-navidrome-docker.sh <music-dir> [port] [user] [password]
set -e
MUSIC="$(cd "$1" && pwd)"
PORT="${2:-4533}"
USER_NAME="${3:-admin}"
PASSWORD="${4:-amperfy-test}"
BASE="http://127.0.0.1:$PORT"

docker run -d --name navidrome -p "$PORT:4533" -v "$MUSIC:/music:ro" \
  -e ND_SCANNER_SCHEDULE=0 -e ND_LOGLEVEL=info deluan/navidrome:latest >/dev/null

for i in $(seq 1 60); do
  curl -sf "$BASE/ping" >/dev/null && break
  sleep 1
done
curl -sf -X POST "$BASE/auth/createAdmin" -H 'Content-Type: application/json' \
  -d "{\"username\":\"$USER_NAME\",\"password\":\"$PASSWORD\"}" >/dev/null

SALT=abc123
TOKEN=$(printf '%s' "$PASSWORD$SALT" | md5sum | cut -d' ' -f1)
AUTH="u=$USER_NAME&t=$TOKEN&s=$SALT&v=1.16.1&c=ci&f=json"
curl -sf "$BASE/rest/startScan?$AUTH&fullScan=true" >/dev/null
for i in $(seq 1 60); do
  curl -sf "$BASE/rest/getScanStatus?$AUTH" | grep -q '"scanning":false' && break
  sleep 1
done
echo "Navidrome ready at $BASE: $(curl -sf "$BASE/rest/getAlbumList2?$AUTH&type=alphabeticalByName&size=50" | grep -o '"id"' | wc -l) ids in album list"
