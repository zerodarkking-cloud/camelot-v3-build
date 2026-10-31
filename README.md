# Camelot для Windows

Простой клиент на русском: вставляете строку прокси (SOCKS5, HTTP/HTTPS, Shadowsocks, VLESS Reality), нажимаете кнопку, и прокси работает как VPN: через него идёт весь трафик компьютера. Внутри работает готовое ядро **sing-box 1.14.4**.

```
src/Camelot.Core/      профиль, разбор строк прокси, генерация конфига sing-box (без зависимостей от Windows)
src/Camelot.App/       WPF-приложение: окно, трей, запуск sing-box, системный прокси, статистика
tests/Camelot.Core.Tests/  проверки логики; конфиги затем проверяет настоящий sing-box
packaging/README.txt   инструкция внутри архива
.github/workflows/build-windows.yml   сборка на GitHub: Camelot-win-x64.zip в Releases
```

## Как это работает

- **Режим «Как VPN».** sing-box создаёт адаптер wintun (драйвер встроен в sing-box.exe):
  - только IPv4: `strict_route` правилами брандмауэра Windows делает IPv6 недоступным и не выпускает DNS мимо туннеля, поэтому ничего не уходит в обход прокси;
  - стек gVisor работает внутри sing-box и не зависит от брандмауэра Windows и сторонних антивирусов;
  - DNS сайтов идёт через прокси (DoH);
  - QUIC отключён, поэтому браузеры работают через TCP. Для HTTP-прокси отключён весь UDP: такой прокси передаёт только TCP.
- **Проверка прокси перед подключением** (`ProxyProbe`) для SOCKS5/HTTP: рукопожатие, логин и пароль, CONNECT к www.gstatic.com и HTTPS через туннель. Вместо «подключено, но не работает» выдаётся понятная причина: неверный пароль (407), закрытый порт, IP не в белом списке (403) и т. п. Если тип указан неверно (SOCKS5 ↔ HTTP, лишняя галочка HTTPS), он подбирается и исправляется в профиле. Та же проверка — кнопка «Проверить» в редакторе.
- **Адаптер.** sing-box останавливается штатно, через Ctrl+C в его скрытую консоль, и сам удаляет адаптер. Если от аварийного завершения остался «зависший» адаптер и Windows не даёт создать новый, Camelot сам перезапускается с новым именем адаптера.
- **Режим «Системный прокси».** sing-box слушает `127.0.0.1:10808` (SOCKS5 + HTTP), а Camelot прописывает этот адрес в настройки прокси Windows. Прежние настройки сохраняются и возвращаются при отключении. Если Camelot упал, они вернутся при следующем запуске.
- **Пароли.** Конфиг передаётся в sing-box через stdin, поэтому пароли не пишутся на диск. Список прокси хранится в `%APPDATA%\Camelot\profiles.dat`, зашифрованный Windows DPAPI.
- **Аварийное завершение.** sing-box запускается в Job Object: если Camelot закроется аварийно, Windows завершит и ядро.

## Сборка

Собирается автоматически при push в ветку `windows-client`. Результат появляется в **Releases** (`win-N`) и в артефактах запуска.

Локально, на Windows с .NET SDK 10:

```powershell
dotnet run --project tests/Camelot.Core.Tests -- build/configs
# проверка ProxyProbe на настоящих прокси: dotnet run --project tests/Camelot.Core.Tests -- probe <socks> <http> <https> <цель>
dotnet publish src/Camelot.App/Camelot.App.csproj -c Release -r win-x64 -o build/publish
# рядом с Camelot.exe положить sing-box.exe и libcronet.dll из sing-box-1.14.4-windows-amd64.zip
```

## Требования

Windows 10 или 11, x64. Нужны права администратора: без них Windows не даст создать адаптер. Установка .NET не нужна, среда выполнения встроена в Camelot.exe.

## Лицензия ядра

sing-box распространяется по GPL-3.0. Его лицензия лежит в архиве (`sing-box-LICENSE.txt`), исходники: https://github.com/SagerNet/sing-box.
