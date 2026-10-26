# Camelot — iOS-клиент прокси для МТС

Простое приложение на русском: вводите свой прокси (Shadowsocks или VLESS Reality), нажимаете одну кнопку. Внутри — готовое ядро **sing-box 1.14.4** (Libbox), обёрнутое в Packet Tunnel extension. Прокси-ядро с нуля не пишется.

```
CamelotTunnel/
├── App/                    SwiftUI-приложение (экран, список серверов, редактор, импорт ссылок)
│   └── Model/              профиль, генератор конфига sing-box, VPNManager, статистика
├── TunnelExtension/        NEPacketTunnelProvider + мост к ядру (PlatformInterface)
├── Shared/                 общие константы (App Group, пути)
├── Frameworks/             сюда кладётся Libbox.xcframework
├── scripts/build-libbox.sh сборка ядра sing-box для iOS
├── server/setup-vps.sh     настройка VPS: listen "::", MSS/MTU, ключи и ссылки
├── Config.xcconfig         Team ID, bundle ID, App Group — меняются только здесь
└── project.yml             описание проекта для XcodeGen
```

## В чём была проблема с МТС и что сделано

**На VPS.** sing-box с `"listen": "0.0.0.0"` открывает сокет **только IPv4** — это видно в исходниках (`NetworkFromNetAddr` превращает `0.0.0.0` в `tcp4`). Телефон в сети МТС подключается к VPS по IPv6 и попадает в пустоту. С `"listen": "::"` один сокет принимает и IPv4, и IPv6. Скрипт `setup-vps.sh fix` правит это автоматически.

**В клиенте.**
- MTU туннеля **1280** (минимум IPv6), настраивается в профиле.
- В туннеле есть и IPv4, и IPv6 адрес с маршрутом `::/0` — IPv6-трафик телефона не уходит мимо VPN.
- Выбор, как подключаться к VPS: «Сначала IPv6» (по умолчанию), «Сначала IPv4», «Только IPv6/IPv4».
- DNS сайтов идёт через VPS (DoH), провайдер его не видит.

**Важное уточнение про MTU.** MTU туннеля определяет размер пакетов *внутри* VPN — это решает проблему для UDP/QUIC (YouTube, Instagram), которые инкапсулируются целиком. Но соединение до VPS по TCP (VLESS, Shadowsocks) iOS открывает обычным сокетом со своим MSS, и MTU туннеля на него не влияет. Поэтому `setup-vps.sh` дополнительно включает на VPS **MSS-clamping** (1220 байт для IPv6, 1240 для IPv4 на портах прокси) и `tcp_mtu_probing` — так крупные TCP-пакеты тоже не теряются в сотовой сети.

**Про US-профиль.** Приложение ставится из Xcode прямо на iPhone, регион Apple ID и App Store (US или RU) роли не играет.

## Что нужно

- Mac с актуальным Xcode и Homebrew: `brew install go xcodegen`
- **Платный Apple Developer Program.** На бесплатном аккаунте (Personal Team) capability Network Extensions недоступна — VPN-приложение не подпишется.
- iPhone с iOS 16+, включённый «Режим разработчика» (Настройки → Конфиденциальность и безопасность).
- VPS с IPv6 (обычно включается в панели хостера).

## Шаг 1. VPS

```bash
scp server/setup-vps.sh root@ВАШ_VPS:/root/
ssh root@ВАШ_VPS
chmod +x setup-vps.sh

./setup-vps.sh fix   # уже есть sing-box: "0.0.0.0" → "::", sysctl, MSS-clamping, рестарт
# или
./setup-vps.sh new   # с нуля: sing-box 1.14.4, VLESS Reality :443 + Shadowsocks-2022 :8443, ссылки для приложения
```

Режим `new` в конце печатает ссылки `vless://` и `ss://` для IPv6 и IPv4 (и сохраняет их в `/etc/sing-box/camelot-links.txt`). Для МТС берите ссылки с IPv6.

Проверка вручную:

```bash
ss -lntu | grep -E ':(443|8443)\b'
#  [::]:443 или *:443   — правильно, IPv4 + IPv6
#  0.0.0.0:443          — только IPv4, телефон в МТС не подключится
ip -6 addr show scope global      # у VPS есть глобальный IPv6?
```

**Если у вас Xray / 3x-ui:** в настройках inbound укажите адрес прослушивания (Listen IP) `::`, перезапустите Xray и проверьте той же командой `ss`. MSS-фиксы можно взять из `setup-vps.sh` (функции `setup_sysctl` и `setup_mss`).

## Шаг 2. Ядро sing-box

На Mac, из корня проекта:

```bash
./scripts/build-libbox.sh          # iPhone + симулятор, 5–10 минут
PLATFORM=ios ./scripts/build-libbox.sh   # только iPhone, быстрее
```

Скрипт скачивает sing-box v1.14.4 и собирает его официальным сборщиком проекта (с флагом `with_low_memory` для iOS). Результат — `Frameworks/Libbox.xcframework`.

## Шаг 3. Xcode

1. Откройте `Config.xcconfig`, впишите `DEVELOPMENT_TEAM` (или выберите команду в Xcode позже). Bundle ID и App Group взяты из ТЗ; если Apple скажет, что ID занят, поменяйте `APP_BUNDLE_ID` и `APP_GROUP_ID` — больше нигде править не нужно.
2. `xcodegen generate && open CamelotTunnel.xcodeproj`
3. Выберите свой iPhone и нажмите Run. При первом подключении iOS попросит разрешить добавление VPN-конфигурации.

Если Xcode ругается на подпись: у обеих целей (CamelotTunnel и TunnelExtension) во вкладке Signing & Capabilities должны быть App Groups и Network Extensions → Packet Tunnel. Они берутся из файлов `.entitlements`; автоматическая подпись сама зарегистрирует идентификаторы.

## Шаг 4. В приложении

1. «Серверы» → «+» → «Вставить ссылку из буфера» (или «Ввести вручную»). Перед сохранением откроется редактор — можно проверить поля.
2. Для МТС: IPv6-адрес VPS или домен с записями A и AAAA + «Подключение к VPS: Сначала IPv6».
3. Большая кнопка — подключиться. При подключении видны скорость, трафик и память ядра.

Настройки «Сеть (МТС)» в редакторе сервера:

| Параметр | По умолчанию | Когда менять |
|---|---|---|
| MTU | 1280 | Обычно не трогать. |
| Подключение к VPS | Сначала IPv6 | Действует, если адрес — домен. |
| IPv6 для сайтов | выкл | Включить, если у VPS есть исходящий IPv6. |
| Блокировать QUIC | выкл | Включить, если видео грузится рывками: приложения перейдут на TCP. |

Смена сервера или настроек при включённом VPN применяется без отключения.

## Что исправлено относительно ТЗ

| В ТЗ | Как на самом деле |
|---|---|
| entitlement `com.apple.developer.networking.vpn.api` (allow-vpn) | Это Personal VPN (IKEv2). Для Packet Tunnel нужен `com.apple.developer.networking.networkextension` = `packet-tunnel-provider`. |
| `includedRouteMaps` | Такого свойства нет, правильно `includedRoutes`. |
| `NETunnelProviderManager.sharedManager()` | Такого метода нет, используется `loadAllFromPreferences()`. |
| Сеть настраивается вручную до старта ядра, `Libbox.startEngine(path)` | В libbox сеть настраивает само ядро через колбэк `PlatformInterface.openTun` — там и задаются MTU и IPv6. |
| `gomobile bind -tags with_shadowsocks,with_utun` | Таких тегов нет (Shadowsocks встроен). Сборка идёт официальным `build_libbox`. |
| `inet4_address`, `inet6_address`, `interface_name: utun12`, `strict_route` | Поля удалены в sing-box 1.12, теперь `address`. Имя utun на iOS выбирает система, `strict_route` на iOS не действует. |
| `tcp_fragment` в outbound Shadowsocks | Такой опции нет. В sing-box есть `tls.fragment`, но к Shadowsocks (без TLS) и к Reality (собственный uTLS-хендшейк) она не применяется — проверено по исходникам. |
| DNS `1.1.1.1, 8.8.8.8` в `NEDNSSettings` | Так DNS-запросы шли бы мимо прокси. DNS перехватывает ядро и отправляет через VPS. |
| Память extension < 12–15 МБ | Начиная с iOS 15 лимит extension около 50 МБ; 12 МБ для Go-ядра недостижимо. Ядро собирается с `with_low_memory` и встроенным OOM-контролем. |

## Как проверено

- Все варианты клиентского конфига, которые формирует `ConfigBuilder` (VLESS Reality/TLS, Shadowsocks, IPv6/IPv4, все переключатели), прошли `sing-box check` версии 1.14.4. Серверный конфиг из `setup-vps.sh` — тоже.
- Сквозной тест: клиент с тем же конфигом (TUN заменён на SOCKS) → сервер из `setup-vps.sh new` → трафик прошёл и по VLESS Reality, и по Shadowsocks-2022; без сервера запросы не проходят.
- Swift-код не компилировался в этой среде (нужен Xcode). Методы моста `PlatformInterface` сверены с исходниками sing-box 1.14.4 и официального клиента sing-box-for-apple той же версии. Если соберёте ядро другой версии и Xcode скажет «does not conform to protocol», сверьте список методов с `experimental/libbox/platform.go`.

## Диагностика

- **Кнопка включается и сразу гаснет** — причина показывается на главном экране (extension сохраняет текст ошибки).
- **На Wi-Fi работает, на МТС нет** — VPS слушает только IPv4 (см. `ss -lntu`), IPv6 закрыт файрволом (`ip6tables -L INPUT -n`), или в профиле указан IPv4-адрес.
- **Логи extension:** Mac → «Консоль» → ваш iPhone → поиск `camelot`.
- **Есть ли у МТС IPv6:** без VPN откройте на телефоне test-ipv6.com.
- Если включены «Частный узел iCloud» или «Ограничить трекинг IP» для сотовой сети, на время проверки выключите их — они меняют DNS и маршрут Safari.

## Лицензия

sing-box распространяется по GPL-3.0. Приложение со встроенным ядром при распространении тоже подпадает под GPL-3.0; для личного использования это ничего не меняет.
