import Foundation

enum ProxyProtocol: String, Codable, CaseIterable, Identifiable {
    case shadowsocks
    case vless

    var id: String { rawValue }

    var title: String {
        switch self {
        case .shadowsocks: return "Shadowsocks"
        case .vless: return "VLESS"
        }
    }
}

enum VLESSSecurity: String, Codable, CaseIterable, Identifiable {
    case reality
    case tls

    var id: String { rawValue }

    var title: String {
        switch self {
        case .reality: return "Reality"
        case .tls: return "TLS"
        }
    }
}

/// Каким IP-протоколом ядро подключается к VPS, если адрес сервера — домен.
/// Значения совпадают со стратегиями DNS в sing-box.
enum ServerIPPreference: String, Codable, CaseIterable, Identifiable {
    case preferIPv6 = "prefer_ipv6"
    case preferIPv4 = "prefer_ipv4"
    case ipv6Only = "ipv6_only"
    case ipv4Only = "ipv4_only"

    var id: String { rawValue }

    var title: String {
        switch self {
        case .preferIPv6: return "Сначала IPv6"
        case .preferIPv4: return "Сначала IPv4"
        case .ipv6Only: return "Только IPv6"
        case .ipv4Only: return "Только IPv4"
        }
    }
}

struct ProxyProfile: Codable, Identifiable, Equatable {
    var id = UUID()
    var name = "Мой сервер"
    var proto: ProxyProtocol = .shadowsocks
    var server = ""
    var port = 443

    // Shadowsocks
    var ssMethod = "aes-128-gcm"
    var ssPassword = ""

    // VLESS
    var uuid = ""
    var flow = "xtls-rprx-vision"
    var security: VLESSSecurity = .reality
    var sni = ""
    var fingerprint = "chrome"
    var realityPublicKey = ""
    var realityShortID = ""

    // Сеть
    var mtu = AppConstants.defaultMTU
    var serverIPPreference: ServerIPPreference = .preferIPv6
    /// Отдавать приложениям IPv6-адреса сайтов (AAAA). Включайте, только если у VPS есть исходящий IPv6.
    var tunnelIPv6 = false
    /// Блокировать QUIC (UDP 443), чтобы приложения переходили на TCP.
    var blockQUIC = false

    static let ssMethods = [
        "2022-blake3-aes-128-gcm",
        "2022-blake3-aes-256-gcm",
        "2022-blake3-chacha20-poly1305",
        "aes-128-gcm",
        "aes-192-gcm",
        "aes-256-gcm",
        "chacha20-ietf-poly1305",
        "xchacha20-ietf-poly1305",
    ]

    static let fingerprints = ["chrome", "firefox", "safari", "ios", "edge", "randomized"]

    static let mtuRange = 1280 ... 1500

    /// Адрес без квадратных скобок и пробелов: «[2a01::1]» → «2a01::1».
    var normalizedServer: String {
        server.trimmingCharacters(in: .whitespacesAndNewlines)
            .trimmingCharacters(in: CharacterSet(charactersIn: "[]"))
    }

    var isIPv6Literal: Bool {
        normalizedServer.contains(":")
    }

    /// «host:port» с квадратными скобками для IPv6.
    var displayAddress: String {
        let host = normalizedServer.isEmpty ? "—" : normalizedServer
        return isIPv6Literal ? "[\(host)]:\(port)" : "\(host):\(port)"
    }

    var protocolTitle: String {
        switch proto {
        case .shadowsocks: return "Shadowsocks · \(ssMethod)"
        case .vless: return "VLESS · \(security.title)"
        }
    }

    func validate() throws {
        let host = normalizedServer
        guard !host.isEmpty else { throw ProfileError("Укажите адрес сервера") }
        guard !host.contains(" "), !host.contains("/") else {
            throw ProfileError("Адрес сервера указан неверно: «\(host)»")
        }
        guard (1 ... 65535).contains(port) else { throw ProfileError("Порт должен быть от 1 до 65535") }
        guard Self.mtuRange.contains(mtu) else {
            throw ProfileError("MTU должен быть от \(Self.mtuRange.lowerBound) до \(Self.mtuRange.upperBound)")
        }

        switch proto {
        case .shadowsocks:
            guard Self.ssMethods.contains(ssMethod) else {
                throw ProfileError("Метод шифрования «\(ssMethod)» не поддерживается")
            }
            guard !ssPassword.isEmpty else { throw ProfileError("Укажите пароль Shadowsocks") }
            if let keyLength = Self.ss2022KeyLength(ssMethod) {
                // В SS-2022 пароль — ключ base64; для нескольких пользователей «ключСервера:ключПользователя».
                for part in ssPassword.split(separator: ":") {
                    guard let data = Data(base64Encoded: String(part)), data.count == keyLength else {
                        throw ProfileError("Для \(ssMethod) пароль должен быть ключом base64 длиной \(keyLength) байт (sing-box generate rand --base64 \(keyLength))")
                    }
                }
            }
        case .vless:
            guard UUID(uuidString: uuid.trimmingCharacters(in: .whitespaces)) != nil else {
                throw ProfileError("UUID пользователя VLESS указан неверно")
            }
            if security == .reality {
                guard !sni.trimmingCharacters(in: .whitespaces).isEmpty else {
                    throw ProfileError("Для Reality укажите SNI (server name)")
                }
                guard !realityPublicKey.trimmingCharacters(in: .whitespaces).isEmpty else {
                    throw ProfileError("Для Reality укажите публичный ключ (pbk)")
                }
                let sid = realityShortID.trimmingCharacters(in: .whitespaces)
                let isHex = sid.allSatisfy(\.isHexDigit)
                guard sid.count <= 16, sid.count % 2 == 0, isHex else {
                    throw ProfileError("Short ID — до 16 шестнадцатеричных символов, чётное количество")
                }
            }
        }
    }

    static func ss2022KeyLength(_ method: String) -> Int? {
        switch method {
        case "2022-blake3-aes-128-gcm": return 16
        case "2022-blake3-aes-256-gcm", "2022-blake3-chacha20-poly1305": return 32
        default: return nil
        }
    }
}

struct ProfileError: LocalizedError {
    let message: String
    init(_ message: String) { self.message = message }
    var errorDescription: String? { message }
}
