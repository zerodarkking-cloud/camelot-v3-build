import Foundation

/// Собирает JSON-конфиг sing-box (формат v1.14) из профиля.
/// Весь трафик iPhone (TUN) уходит в выбранный прокси: SOCKS5, HTTP, Shadowsocks или VLESS.
///
/// Что важно для МТС:
/// * `mtu` TUN-интерфейса = 1280 (минимум IPv6) — крупные UDP/QUIC-пакеты не теряются в сотовой сети;
/// * в TUN есть и IPv4, и IPv6 адрес, поэтому IPv6-трафик телефона не идёт мимо VPN;
/// * `domain_resolver.strategy` управляет тем, по IPv6 или IPv4 ядро подключается к VPS.
enum ConfigBuilder {
    static let tunInet4 = "172.19.0.1/30"
    static let tunInet6 = "fdfe:dcba:9876::1/126"

    static func build(_ profile: ProxyProfile) throws -> String {
        try profile.validate()

        var proxy: [String: Any] = [
            "tag": "proxy",
            "server": profile.normalizedServer,
            "server_port": profile.port,
            // Как резолвить домен VPS: системным DNS, с выбранным приоритетом IPv6/IPv4.
            "domain_resolver": [
                "server": "dns-local",
                "strategy": profile.serverIPPreference.rawValue,
            ],
        ]

        switch profile.proto {
        case .socks:
            proxy["type"] = "socks"
            proxy["version"] = "5"
            if !profile.username.isEmpty {
                proxy["username"] = profile.username
                proxy["password"] = profile.password
            }

        case .http:
            proxy["type"] = "http"
            if !profile.username.isEmpty {
                proxy["username"] = profile.username
                proxy["password"] = profile.password
            }
            if profile.useTLS {
                proxy["tls"] = ["enabled": true, "server_name": profile.normalizedServer]
            }

        case .shadowsocks:
            proxy["type"] = "shadowsocks"
            proxy["method"] = profile.ssMethod
            proxy["password"] = profile.ssPassword

        case .vless:
            proxy["type"] = "vless"
            proxy["uuid"] = profile.uuid.trimmingCharacters(in: .whitespaces)
            if !profile.flow.isEmpty {
                proxy["flow"] = profile.flow
            }
            var tls: [String: Any] = ["enabled": true]
            let sni = profile.sni.trimmingCharacters(in: .whitespaces)
            if !sni.isEmpty {
                tls["server_name"] = sni
            }
            let fingerprint = profile.fingerprint.isEmpty ? "chrome" : profile.fingerprint
            tls["utls"] = ["enabled": true, "fingerprint": fingerprint]
            if profile.security == .reality {
                tls["reality"] = [
                    "enabled": true,
                    "public_key": profile.realityPublicKey.trimmingCharacters(in: .whitespaces),
                    "short_id": profile.realityShortID.trimmingCharacters(in: .whitespaces),
                ]
            }
            proxy["tls"] = tls
        }

        var dns: [String: Any] = [
            "servers": [
                // DNS сайтов — через VPS (DoH), провайдер его не видит и не подменяет.
                ["type": "https", "tag": "dns-remote", "server": "1.1.1.1", "detour": "proxy"],
                // Системный DNS — только чтобы найти сам VPS, если указан домен.
                ["type": "local", "tag": "dns-local"],
            ],
            "final": "dns-remote",
        ]
        if !profile.tunnelIPv6 {
            // Сайтам отдаём только IPv4-адреса: работает с любым VPS, даже без исходящего IPv6.
            dns["strategy"] = "ipv4_only"
        }

        let tun: [String: Any] = [
            "type": "tun",
            "tag": "tun-in",
            "address": [tunInet4, tunInet6],
            "mtu": profile.mtu,
            "auto_route": true,
        ]

        var rules: [[String: Any]] = [
            ["action": "sniff"],
            ["protocol": "dns", "action": "hijack-dns"],
            ["ip_is_private": true, "outbound": "direct"],
        ]
        if profile.proto == .http {
            // HTTP-прокси передаёт только TCP. Остальной UDP отклоняем сразу, чтобы приложения
            // не ждали таймаута и не уходили мимо прокси (DNS уже обработан выше).
            rules.append(["network": "udp", "action": "reject"])
        } else if profile.blockQUIC {
            rules.append(["network": "udp", "port": 443, "action": "reject"])
        }

        let config: [String: Any] = [
            "log": ["level": "warn"],
            "dns": dns,
            "inbounds": [tun],
            "outbounds": [
                proxy,
                ["type": "direct", "tag": "direct"],
            ],
            "route": [
                "rules": rules,
                "final": "proxy",
                "auto_detect_interface": true,
                "default_domain_resolver": "dns-local",
            ],
        ]

        let data = try JSONSerialization.data(
            withJSONObject: config,
            options: [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]
        )
        return String(decoding: data, as: UTF8.self)
    }

    /// Сохраняет конфиг в общий контейнер — его прочитает extension при запуске не из приложения.
    static func save(_ config: String) throws {
        try config.write(to: AppConstants.configURL, atomically: true, encoding: .utf8)
    }
}
