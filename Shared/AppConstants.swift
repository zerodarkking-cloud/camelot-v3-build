import Foundation

/// Общие константы приложения и Packet Tunnel extension.
/// Идентификаторы задаются в Config.xcconfig и попадают сюда через Info.plist,
/// поэтому менять их нужно только в одном месте.
enum AppConstants {
    static let declaredAppGroupID: String =
        Bundle.main.object(forInfoDictionaryKey: "CamelotAppGroup") as? String
            ?? "group.com.camelot.browser.shared"

    /// Фактическая App Group. Программы сайдлоада (Impactor, Sideloadly, AltStore, SideStore)
    /// переименовывают группу в «<группа>.<TeamID>», поэтому берём ту, к которой у приложения
    /// реально есть доступ: из Info.plist, ALTAppGroups или из embedded.mobileprovision.
    static let appGroupID: String = {
        var candidates = [declaredAppGroupID]
        if let altGroups = Bundle.main.object(forInfoDictionaryKey: "ALTAppGroups") as? [String] {
            candidates += altGroups
        }
        // Сначала группа, производная от нашей (одинаковая у приложения и extension),
        // потом остальные, например автоматическая «group.<bundle id>.<TeamID>».
        let provisioned = provisionedAppGroups()
        candidates += provisioned.filter { $0.hasPrefix(declaredAppGroupID) }
        candidates += provisioned.filter { !$0.hasPrefix(declaredAppGroupID) }
        let manager = FileManager.default
        return candidates.first { manager.containerURL(forSecurityApplicationGroupIdentifier: $0) != nil }
            ?? declaredAppGroupID
    }()

    /// Bundle ID VPN-extension. Берётся из вложенного .appex, чтобы не зависеть от того,
    /// поменяла ли программа установки идентификаторы.
    static let tunnelBundleID: String = {
        if let plugins = Bundle.main.builtInPlugInsURL,
           let items = try? FileManager.default.contentsOfDirectory(at: plugins, includingPropertiesForKeys: nil) {
            for url in items where url.pathExtension == "appex" {
                guard let bundle = Bundle(url: url),
                      let ext = bundle.object(forInfoDictionaryKey: "NSExtension") as? [String: Any],
                      ext["NSExtensionPointIdentifier"] as? String == "com.apple.networkextension.packet-tunnel",
                      let id = bundle.bundleIdentifier else { continue }
                return id
            }
        }
        return Bundle.main.object(forInfoDictionaryKey: "CamelotTunnelBundleID") as? String
            ?? "com.camelot.browser.app.tunnel-extension"
    }()

    /// Группы из профиля подписи (его нет при установке через TrollStore — тогда пусто).
    private static func provisionedAppGroups() -> [String] {
        guard let url = Bundle.main.url(forResource: "embedded", withExtension: "mobileprovision"),
              let data = try? Data(contentsOf: url),
              let start = data.range(of: Data("<?xml".utf8)),
              let end = data.range(of: Data("</plist>".utf8), in: start.lowerBound ..< data.endIndex),
              let plist = try? PropertyListSerialization.propertyList(
                  from: data.subdata(in: start.lowerBound ..< end.upperBound), format: nil
              ) as? [String: Any],
              let entitlements = plist["Entitlements"] as? [String: Any],
              let groups = entitlements["com.apple.security.application-groups"] as? [String]
        else { return [] }
        return groups
    }

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
