import Foundation

/// Общие константы приложения и Packet Tunnel extension.
/// Идентификаторы задаются в Config.xcconfig и попадают сюда через Info.plist,
/// поэтому менять их нужно только в одном месте.
enum AppConstants {
    static let appGroupID: String =
        Bundle.main.object(forInfoDictionaryKey: "CamelotAppGroup") as? String
            ?? "group.com.camelot.browser.shared"

    static let tunnelBundleID: String =
        Bundle.main.object(forInfoDictionaryKey: "CamelotTunnelBundleID") as? String
            ?? "com.camelot.browser.app.tunnel-extension"

    /// 1280 — минимальный MTU для IPv6 (RFC 8200). Пакеты такого размера
    /// гарантированно проходят по сотовой сети МТС без фрагментации.
    static let defaultMTU = 1280

    /// Ключ, под которым приложение передаёт JSON-конфиг sing-box в extension.
    static let configOptionKey = "configContent"

    /// Ключ общей UserDefaults, куда extension пишет причину неудачного запуска.
    static let lastErrorKey = "lastTunnelError"

    static var sharedDirectory: URL {
        guard let url = FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: appGroupID) else {
            fatalError("App Group «\(appGroupID)» недоступна. Проверьте entitlements и Config.xcconfig.")
        }
        return url
    }

    static var cacheDirectory: URL {
        sharedDirectory.appendingPathComponent("Library/Caches", isDirectory: true)
    }

    static var workingDirectory: URL {
        cacheDirectory.appendingPathComponent("Working", isDirectory: true)
    }

    /// Последний применённый конфиг. Extension читает его, если VPN включили
    /// не из приложения (Настройки → VPN, Пункт управления).
    static var configURL: URL {
        sharedDirectory.appendingPathComponent("config.json")
    }

    static var sharedDefaults: UserDefaults {
        UserDefaults(suiteName: appGroupID) ?? .standard
    }

    static func prepareDirectories() throws {
        try FileManager.default.createDirectory(at: workingDirectory, withIntermediateDirectories: true)
    }
}
