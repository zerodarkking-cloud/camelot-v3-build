import Foundation
import Libbox
import NetworkExtension
import os.log

/// Packet Tunnel extension. Всю работу с протоколами делает ядро sing-box (Libbox).
///
/// Порядок запуска:
/// 1. берём JSON-конфиг (из параметров запуска или из общего контейнера App Group);
/// 2. инициализируем Libbox и командный сервер (через него приложение читает статистику);
/// 3. запускаем сервис — ядро само вызывает `PlatformInterface.openTun`, где мы применяем
///    сетевые настройки iOS: MTU 1280, IPv4 + IPv6 маршруты, DNS.
final class PacketTunnelProvider: NEPacketTunnelProvider {
    private let logger = Logger(subsystem: "camelot.tunnel", category: "PacketTunnel")
    private var commandServer: LibboxCommandServer?
    private lazy var platform = PlatformInterface(tunnel: self)
    private var currentConfig: String?

    override func startTunnel(options: [String: NSObject]?) async throws {
        AppConstants.sharedDefaults.removeObject(forKey: AppConstants.lastErrorKey)
        do {
            try startCore(options: options)
            logger.info("Туннель запущен")
        } catch {
            let message = error.localizedDescription
            logger.error("Ошибка запуска: \(message, privacy: .public)")
            AppConstants.sharedDefaults.set(message, forKey: AppConstants.lastErrorKey)
            await stopCore()
            throw error
        }
    }

    override func stopTunnel(with reason: NEProviderStopReason) async {
        logger.info("Остановка туннеля, причина: \(reason.rawValue)")
        await stopCore()
    }

    /// Приложение присылает новый конфиг, если пользователь сменил сервер при включённом VPN.
    /// Ответ `nil` — успех, иначе текст ошибки.
    override func handleAppMessage(_ messageData: Data) async -> Data? {
        guard let config = String(data: messageData, encoding: .utf8), !config.isEmpty else {
            return "Пустой конфиг".data(using: .utf8)
        }
        do {
            try reload(config: config)
            return nil
        } catch {
            return error.localizedDescription.data(using: .utf8)
        }
    }

    override func sleep() async {
        commandServer?.pause()
    }

    override func wake() {
        commandServer?.wake()
    }

    // MARK: - Ядро

    private func startCore(options: [String: NSObject]?) throws {
        try AppConstants.prepareDirectories()
        let config = try loadConfig(options)
        currentConfig = config

        let setup = LibboxSetupOptions()
        setup.basePath = AppConstants.sharedDirectory.path
        setup.workingPath = AppConstants.workingDirectory.path
        setup.tempPath = AppConstants.cacheDirectory.path
        setup.logMaxLines = 500
        // Ядро само следит за памятью и сбрасывает соединения до лимита iOS (~50 МБ на extension).
        setup.oomKillerEnabled = true

        var setupError: NSError?
        LibboxSetup(setup, &setupError)
        if let setupError {
            throw TunnelError("Ошибка инициализации ядра: \(setupError.localizedDescription)")
        }

        var serverError: NSError?
        commandServer = LibboxNewCommandServer(platform, platform, &serverError)
        if let serverError {
            throw TunnelError("Не удалось создать командный сервер ядра: \(serverError.localizedDescription)")
        }
        guard let server = commandServer else {
            throw TunnelError("Ядро sing-box не инициализировалось")
        }

        do {
            try server.start()
        } catch {
            throw TunnelError("Не удалось запустить командный сервер: \(error.localizedDescription)")
        }

        do {
            try server.startOrReloadService(config, options: LibboxOverrideOptions())
        } catch {
            throw TunnelError("sing-box не запустился: \(error.localizedDescription)")
        }
    }

    private func stopCore() async {
        guard let server = commandServer else { return }
        do {
            try server.closeService()
        } catch {
            logger.error("closeService: \(error.localizedDescription, privacy: .public)")
        }
        platform.reset()
        // Даём клиентам командного сервера получить последний статус.
        try? await Task.sleep(nanoseconds: 100_000_000)
        server.close()
        commandServer = nil
    }

    /// Перезапуск сервиса без разрыва VPN (смена сервера, запрос ядра).
    func reload(config newConfig: String? = nil) throws {
        guard let server = commandServer else {
            throw TunnelError("Ядро не запущено")
        }
        guard let config = newConfig ?? currentConfig else {
            throw TunnelError("Нет конфигурации")
        }
        reasserting = true
        defer { reasserting = false }
        try server.startOrReloadService(config, options: LibboxOverrideOptions())
        currentConfig = config
    }

    private func loadConfig(_ options: [String: NSObject]?) throws -> String {
        if let config = options?[AppConstants.configOptionKey] as? String, !config.isEmpty {
            return config
        }
        // VPN включили не из приложения — используем последний сохранённый конфиг.
        if let data = try? Data(contentsOf: AppConstants.configURL),
           let config = String(data: data, encoding: .utf8), !config.isEmpty {
            return config
        }
        throw TunnelError("Нет настроек сервера. Откройте Camelot и выберите сервер.")
    }
}

struct TunnelError: LocalizedError {
    let message: String
    init(_ message: String) { self.message = message }
    var errorDescription: String? { message }
}
