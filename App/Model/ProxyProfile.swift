import Foundation

enum ProxyProtocol: String, Codable, CaseIterable, Identifiable {
    case socks
    case http
    case shadowsocks
    case vless

    var id: String { rawValue }

    var title: String {
        switch self {
        case .socks: return "SOCKS5"
        case .http: return "HTTP"
        case .shadowsocks: return "Shadowsocks"
        case .vless: return "VLESS"
        }
    }

    /// Обычные прокси без собственного шифрования (SOCKS5 / HTTP).
    var isPlainProxy: Bool {
        self == .socks || self == .http
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
    var name = "Мой прокси"
    var proto: ProxyProtocol = .socks
    var server = ""
    var port = 1080

    // SOCKS5 / HTTP
    var username = ""
    var password = ""
    /// HTTPS-прокси: TLS-соединение до самого прокси.
    var useTLS = false

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
    /// Для SOCKS/HTTP включено по умолчанию: многие прокси не пропускают UDP.
    var blockQUIC = true

    init() {}

    // Терпимое чтение: профили, сохранённые старой версией, не теряются при добавлении новых полей.
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let d = ProxyProfile()
        id = try c.decodeIfPresent(UUID.self, forKey: .id) ?? d.id
        name = try c.decodeIfPresent(String.self, forKey: .name) ?? d.name
        proto = (try? c.decodeIfPresent(ProxyProtocol.self, forKey: .proto)) ?? d.proto
        server = try c.decodeIfPresent(String.self, forKey: .server) ?? d.server
        port = try c.decodeIfPresent(Int.self, forKey: .port) ?? d.port
        username = try c.decodeIfPresent(String.self, forKey: .username) ?? d.username
        password = try c.decodeIfPresent(String.self, forKey: .password) ?? d.password
        useTLS = try c.decodeIfPresent(Bool.self, forKey: .useTLS) ?? d.useTLS
        ssMethod = try c.decodeIfPresent(String.self, forKey: .ssMethod) ?? d.ssMethod
        ssPassword = try c.decodeIfPresent(String.self, forKey: .ssPassword) ?? d.ssPassword
        uuid = try c.decodeIfPresent(String.self, forKey: .uuid) ?? d.uuid
        flow = try c.decodeIfPresent(String.self, forKey: .flow) ?? d.flow
        security = (try? c.decodeIfPresent(VLESSSecurity.self, forKey: .security)) ?? d.security
        sni = try c.decodeIfPresent(String.self, forKey: .sni) ?? d.sni
        fingerprint = try c.decodeIfPresent(String.self, forKey: .fingerprint) ?? d.fingerprint
        realityPublicKey = try c.decodeIfPresent(String.self, forKey: .realityPublicKey) ?? d.realityPublicKey
        realityShortID = try c.decodeIfPresent(String.self, forKey: .realityShortID) ?? d.realityShortID
        mtu = try c.decodeIfPresent(Int.self, forKey: .mtu) ?? d.mtu
        serverIPPreference = (try? c.decodeIfPresent(ServerIPPreference.self, forKey: .serverIPPreference)) ?? d.serverIPPreference
        tunnelIPv6 = try c.decodeIfPresent(Bool.self, forKey: .tunnelIPv6) ?? d.tunnelIPv6
        blockQUIC = try c.decodeIfPresent(Bool.self, forKey: .blockQUIC) ?? d.blockQUIC
    }

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
        case .socks: return username.isEmpty ? "SOCKS5" : "SOCKS5 · \(username)"
        case .http: return (useTLS ? "HTTPS" : "HTTP") + (username.isEmpty ? "" : " · \(username)")
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
        case .socks, .http:
            if username.isEmpty, !password.isEmpty {
                throw ProfileError("Указан пароль, но не указан логин прокси")
            }
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
