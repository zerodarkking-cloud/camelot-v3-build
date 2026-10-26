#!/usr/bin/env bash
# Упаковка собранного без подписи CamelotTunnel.app в .tipa для установки через TrollStore.
# Бинарники подписываются ldid вместе с entitlements: без них iOS не даст создать VPN-профиль.
#
#   ./scripts/package-tipa.sh path/to/CamelotTunnel.app [dist]
set -euo pipefail

APP="${1:?укажите путь к CamelotTunnel.app}"
OUT="${2:-dist}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

command -v ldid >/dev/null || { echo "Нужен ldid: brew install ldid" >&2; exit 1; }
[ -d "$APP" ] || { echo "Не найден $APP" >&2; exit 1; }

# Значения из Config.xcconfig (единственное место, где заданы идентификаторы)
xc() { sed -n "s/^$1 *= *//p" "$ROOT/Config.xcconfig" | tail -n1; }
APP_GROUP_ID="$(xc APP_GROUP_ID)"
[ -n "$APP_GROUP_ID" ] || { echo "APP_GROUP_ID не задан в Config.xcconfig" >&2; exit 1; }

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

cat > "$WORK/entitlements.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>com.apple.developer.networking.networkextension</key>
	<array>
		<string>packet-tunnel-provider</string>
	</array>
	<key>com.apple.security.application-groups</key>
	<array>
		<string>$APP_GROUP_ID</string>
	</array>
</dict>
</plist>
EOF

mkdir -p "$WORK/Payload"
cp -R "$APP" "$WORK/Payload/"
BUNDLE="$WORK/Payload/$(basename "$APP")"
rm -rf "$BUNDLE/_CodeSignature" "$BUNDLE/embedded.mobileprovision"

# Сначала вложенный extension, потом само приложение.
for appex in "$BUNDLE"/PlugIns/*.appex; do
  [ -d "$appex" ] || continue
  rm -rf "$appex/_CodeSignature" "$appex/embedded.mobileprovision"
  exe="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$appex/Info.plist")"
  ldid -S"$WORK/entitlements.plist" "$appex/$exe"
  echo "Подписан extension: $(basename "$appex")"
done
exe="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$BUNDLE/Info.plist")"
ldid -S"$WORK/entitlements.plist" "$BUNDLE/$exe"
echo "Подписано приложение: $(basename "$BUNDLE")"

echo "Entitlements приложения:"
ldid -e "$BUNDLE/$exe"

mkdir -p "$OUT"
VERSION="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$BUNDLE/Info.plist")"
NAME="CamelotTunnel-$VERSION.tipa"
(cd "$WORK" && zip -qry "$OLDPWD/$OUT/$NAME" Payload)
echo "Готово: $OUT/$NAME"
