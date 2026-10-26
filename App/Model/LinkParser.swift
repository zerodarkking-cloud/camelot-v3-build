import Foundation

/// Разбор ссылок `ss://` (SIP002 и старый формат) и `vless://` (Reality/TLS поверх TCP).
enum LinkParser {
    static func parse(_ raw: String) throws -> ProxyProfile {
        let text = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        let lower = text.lowercased()
        if lower.hasPrefix("ss://") {
            return try parseShadowsocks(String(text.dropFirst(5)))
        }
        if lower.hasPrefix("vless://") {
            return try parseVLESS(String(text.dropFirst(8)))
        }
        throw ProfileError("Поддерживаются ссылки ss:// и vless://")
    }

    // MARK: - Shadowsocks

    private static func parseShadowsocks(_ body: String) throws -> ProxyProfile {
        let (withoutName, name) = splitFragment(body)
        let (path, query) = splitQuery(withoutName)
        let main = path.hasSuffix("/") ? String(path.dropLast()) : path

        if let plugin = query["plugin"], !plugin.isEmpty {
            throw ProfileError("Плагины Shadowsocks (\(plugin)) не поддерживаются")
        }

        let userInfo: String
        let hostPort: String
        if let at = main.lastIndex(of: "@") {
            userInfo = String(main[..<at])
            hostPort = String(main[main.index(after: at)...])
        } else {
            // Старый формат: ss://base64(method:password@host:port)
            guard let decoded = decodeBase64(main), let at = decoded.lastIndex(of: "@") else {
                throw ProfileError("Не удалось разобрать ссылку ss://")
            }
            userInfo = String(decoded[..<at])
            hostPort = String(decoded[decoded.index(after: at)...])
        }

        // SIP002: userinfo = base64url(method:password); для SS-2022 — method:password в процентной кодировке.
        let credentials: String
        if let decoded = decodeBase64(userInfo), decoded.contains(":") {
            credentials = decoded
        } else {
            credentials = userInfo.removingPercentEncoding ?? userInfo
        }
        guard let colon = credentials.firstIndex(of: ":") else {
            throw ProfileError("В ссылке ss:// нет метода шифрования или пароля")
        }

        let (host, port) = try splitHostPort(hostPort)
        var profile = ProxyProfile()
        profile.proto = .shadowsocks
        profile.name = name ?? "Shadowsocks"
        profile.server = host
        profile.port = port
        profile.ssMethod = String(credentials[..<colon]).lowercased()
        profile.ssPassword = String(credentials[credentials.index(after: colon)...])
        if host.contains(":") { profile.serverIPPreference = .preferIPv6 }
        return profile
    }

    // MARK: - VLESS

    private static func parseVLESS(_ body: String) throws -> ProxyProfile {
        let (withoutName, name) = splitFragment(body)
        let (path, query) = splitQuery(withoutName)
        let main = path.hasSuffix("/") ? String(path.dropLast()) : path

        guard let at = main.lastIndex(of: "@") else {
            throw ProfileError("В ссылке vless:// нет UUID пользователя")
        }
        let uuid = String(main[..<at]).removingPercentEncoding ?? String(main[..<at])
        let (host, port) = try splitHostPort(String(main[main.index(after: at)...]))

        let transport = (query["type"] ?? "tcp").lowercased()
        guard transport == "tcp" || transport == "raw" else {
            throw ProfileError("Транспорт «\(transport)» не поддерживается, нужен TCP (Reality/TLS)")
        }

        var profile = ProxyProfile()
        profile.proto = .vless
        profile.name = name ?? "VLESS"
        profile.server = host
        profile.port = port
        profile.uuid = uuid
        profile.flow = query["flow"] ?? ""

        switch (query["security"] ?? "").lowercased() {
        case "reality":
            profile.security = .reality
            profile.realityPublicKey = query["pbk"] ?? ""
            profile.realityShortID = query["sid"] ?? ""
        case "tls":
            profile.security = .tls
        default:
            throw ProfileError("Поддерживается только VLESS с security=reality или security=tls")
        }
        profile.sni = query["sni"] ?? query["peer"] ?? ""
        if let fp = query["fp"]?.lowercased(), !fp.isEmpty {
            profile.fingerprint = ProxyProfile.fingerprints.contains(fp) ? fp : "chrome"
        }
        return profile
    }

    // MARK: - Помощники

    private static func splitFragment(_ text: String) -> (String, String?) {
        guard let hash = text.firstIndex(of: "#") else { return (text, nil) }
        let raw = String(text[text.index(after: hash)...])
        let name = (raw.removingPercentEncoding ?? raw).trimmingCharacters(in: .whitespaces)
        return (String(text[..<hash]), name.isEmpty ? nil : name)
    }

    private static func splitQuery(_ text: String) -> (String, [String: String]) {
        guard let mark = text.firstIndex(of: "?") else { return (text, [:]) }
        var params: [String: String] = [:]
        for pair in text[text.index(after: mark)...].split(separator: "&") {
            let parts = pair.split(separator: "=", maxSplits: 1).map(String.init)
            guard let key = parts.first, !key.isEmpty else { continue }
            let value = parts.count > 1 ? parts[1] : ""
            params[key.lowercased()] = value.removingPercentEncoding ?? value
        }
        return (String(text[..<mark]), params)
    }

    /// «host:port», «[2a01::1]:443».
    private static func splitHostPort(_ text: String) throws -> (String, Int) {
        let value = text.trimmingCharacters(in: .whitespaces)
        let host: String
        let portText: String
        if value.hasPrefix("[") {
            guard let close = value.firstIndex(of: "]") else {
                throw ProfileError("Неверный IPv6-адрес в ссылке")
            }
            host = String(value[value.index(after: value.startIndex) ..< close])
            let rest = value[value.index(after: close)...]
            guard rest.hasPrefix(":") else { throw ProfileError("В ссылке не указан порт") }
            portText = String(rest.dropFirst())
        } else {
            guard let colon = value.lastIndex(of: ":") else { throw ProfileError("В ссылке не указан порт") }
            host = String(value[..<colon])
            portText = String(value[value.index(after: colon)...])
        }
        guard !host.isEmpty else { throw ProfileError("В ссылке не указан адрес сервера") }
        guard let port = Int(portText), (1 ... 65535).contains(port) else {
            throw ProfileError("Неверный порт: «\(portText)»")
        }
        return (host, port)
    }

    /// base64 / base64url, с паддингом и без.
    private static func decodeBase64(_ text: String) -> String? {
        var value = text.replacingOccurrences(of: "-", with: "+").replacingOccurrences(of: "_", with: "/")
        let remainder = value.count % 4
        if remainder == 1 { return nil }
        if remainder > 0 { value += String(repeating: "=", count: 4 - remainder) }
        guard let data = Data(base64Encoded: value), let decoded = String(data: data, encoding: .utf8) else {
            return nil
        }
        return decoded
    }
}
