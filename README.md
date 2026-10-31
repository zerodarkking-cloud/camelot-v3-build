# Camelot для Windows

Простой клиент на русском: вставляете строку прокси (SOCKS5, HTTP/HTTPS, Shadowsocks, VLESS Reality), нажимаете кнопку, и через прокси идёт весь трафик компьютера. Внутри работает готовое ядро **sing-box 1.14.4**. Это то же решение, что и в iOS-версии (ветка `ios-tunnel`), только для Windows.

```
src/Camelot.Core/      профиль, разбор строк прокси, генерация конфига sing-box (без зависимостей от Windows)
src/Camelot.App/       WPF-приложение: окно, трей, запуск sing-box, системный прокси, статистика
tests/Camelot.Core.Tests/  проверки логики; конфиги затем проверяет настоящий sing-box
packaging/README.txt   инструкция внутри архива
.github/workflows/build-windows.yml   сборка на GitHub: Camelot-win-x64.zip в Releases
```

## Как это работает

- **Режим «Весь трафик (TUN)».** sing-box создаёт адаптер wintun «Camelot» (драйвер встроен в sing-box.exe):
  - MTU **1280** — минимальный для IPv6, поэтому крупные пакеты не теряются в сотовых сетях вроде МТС;
  - адреса IPv4 и IPv6, маршрут `::/0` тоже уходит в туннель;
  - `strict_route` правилами брандмауэра Windows запрещает DNS мимо туннеля;
  - DNS сайтов идёт через прокси по DoH;
  - адрес самого прокси находит системный DNS, с приоритетом IPv6 (настраивается).
- **Режим «Системный прокси».** sing-box слушает `127.0.0.1:10808` (SOCKS5 + HTTP), а Camelot прописывает этот адрес в настройки прокси Windows. Прежние настройки сохраняются и возвращаются при отключении. Если Camelot упал, они вернутся при следующем запуске.
- Конфиг передаётся в sing-box через stdin, поэтому пароли прокси не пишутся на диск. Список прокси хранится в `%APPDATA%\Camelot\profiles.dat`, зашифрованный Windows DPAPI для текущего пользователя.
- sing-box запускается в Job Object: если Camelot закроется аварийно, Windows завершит и ядро, а вместе с ним уберёт адаптер и правила брандмауэра.
- Скорость, трафик и задержка берутся из Clash API ядра. Он слушает только 127.0.0.1 со случайным токеном.
- При закрытии окна во время подключения Camelot уходит в трей. Выход через меню значка.

## Сборка

Собирается автоматически при push в ветку `windows-client`. Результат появляется в **Releases** (`win-N`) и в артефактах запуска.

Локально, на Windows с .NET SDK 10:

```powershell
dotnet run --project tests/Camelot.Core.Tests -- build/configs
dotnet publish src/Camelot.App/Camelot.App.csproj -c Release -r win-x64 -o build/publish
# рядом с Camelot.exe положить sing-box.exe и libcronet.dll из sing-box-1.14.4-windows-amd64.zip
```

## Требования

Windows 10 или 11, x64. Нужны права администратора: без них Windows не даст создать адаптер. Установка .NET не нужна, среда выполнения встроена в Camelot.exe.

## Лицензия ядра

sing-box распространяется по GPL-3.0. Его лицензия лежит в архиве (`sing-box-LICENSE.txt`), исходники: https://github.com/SagerNet/sing-box.
