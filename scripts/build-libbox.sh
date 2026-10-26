#!/usr/bin/env bash
# Сборка ядра sing-box (Libbox.xcframework) для iOS.
# Нужно: macOS, Xcode (с Command Line Tools), Go 1.24+ (нужная версия Go скачается сама).
#
#   ./scripts/build-libbox.sh                      # устройство + симулятор
#   PLATFORM=ios ./scripts/build-libbox.sh         # только iPhone (быстрее)
#   SING_BOX_VERSION=v1.14.4 ./scripts/build-libbox.sh
#
# Swift-код PlatformInterface рассчитан на sing-box v1.14.x. При смене мажорной версии
# сверьте список методов с experimental/libbox/platform.go.
set -euo pipefail

SING_BOX_VERSION="${SING_BOX_VERSION:-v1.14.4}"
PLATFORM="${PLATFORM:-ios,iossimulator}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WORK="$ROOT/build/sing-box"
OUT="$ROOT/Frameworks"

die() { echo "Ошибка: $*" >&2; exit 1; }

[ "$(uname -s)" = "Darwin" ] || die "собрать iOS-фреймворк можно только на macOS"
command -v go >/dev/null || die "не найден Go. Установите: brew install go"
xcrun --sdk iphoneos --show-sdk-path >/dev/null 2>&1 || die "не найден iOS SDK. Установите Xcode и выполните: sudo xcode-select -s /Applications/Xcode.app"

echo "==> sing-box $SING_BOX_VERSION"
if [ -d "$WORK/.git" ]; then
  git -C "$WORK" fetch --depth 1 origin tag "$SING_BOX_VERSION"
  git -C "$WORK" checkout -q "$SING_BOX_VERSION"
else
  mkdir -p "$(dirname "$WORK")"
  git clone --depth 1 --branch "$SING_BOX_VERSION" https://github.com/SagerNet/sing-box.git "$WORK"
fi

cd "$WORK"
echo "==> Установка gomobile (форк SagerNet)"
make lib_install
export PATH="$(go env GOPATH)/bin:$PATH"

# Официальный сборщик проекта: правильные build-теги (with_gvisor, with_quic, with_utls, ...),
# для iOS автоматически добавляется with_low_memory — ядро укладывается в лимит памяти extension.
echo "==> Сборка Libbox для: $PLATFORM (несколько минут)"
go run ./cmd/internal/build_libbox -target apple -platform "$PLATFORM"

[ -d Libbox.xcframework ] || die "сборка завершилась без Libbox.xcframework"
mkdir -p "$OUT"
rm -rf "$OUT/Libbox.xcframework"
mv Libbox.xcframework "$OUT/"
echo "==> Готово: $OUT/Libbox.xcframework"
