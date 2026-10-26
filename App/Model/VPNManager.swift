import Foundation
import NetworkExtension

enum TunnelState: Equatable {
    case invalid
    case disconnected
    case connecting
    case connected
    case reasserting
    case disconnecting

    init(_ status: NEVPNStatus) {
        switch status {
        case .invalid: self = .invalid
        case .disconnected: self = .disconnected
        case .connecting: self = .connecting
        case .connected: self = .connected
        case .reasserting: self = .reasserting
        case .disconnecting: self = .disconnecting
        @unknown default: self = .invalid
        }
    }

    var title: String {
        switch self {
        case .invalid, .disconnected: return "Отключено"
        case .connecting: return "Подключение…"
        case .connected: return "Подключено"
        case .reasserting: return "Переподключение…"
        case .disconnecting: return "Отключение…"
        }
    }

    var isBusy: Bool {
        self == .connecting || self == .reasserting || self == .disconnecting
    }

    var isActive: Bool {
        self == .connected || self == .connecting || self == .reasserting
    }
}

/// Управление VPN-профилем iOS (NETunnelProviderManager) и запуском extension.
@MainActor
final class VPNManager: ObservableObject {
    @Published private(set) var state: TunnelState = .invalid
    @Published private(set) var connectedSince: Date?
    @Published var errorMessage: String?

    private var manager: NETunnelProviderManager?
    private var statusObserver: NSObjectProtocol?
    private var isStarting = false

    init() {
        Task { await loadManager() }
    }

    func loadManager() async {
        do {
            let managers = try await NETunnelProviderManager.loadAllFromPreferences()
            let mine = managers.first {
                ($0.protocolConfiguration as? NETunnelProviderProtocol)?.providerBundleIdentifier == AppConstants.tunnelBundleID
            }
            if let mine {
                attach(mine)
            } else {
                state = .disconnected
            }
        } catch {
            errorMessage = "Не удалось прочитать настройки VPN: \(Self.describe(error))"
        }
    }

    func connect(profile: ProxyProfile) async {
        errorMessage = nil
        do {
            let config = try ConfigBuilder.build(profile)
            try ConfigBuilder.save(config)
            AppConstants.sharedDefaults.removeObject(forKey: AppConstants.lastErrorKey)
            let manager = try await preparedManager()
            isStarting = true
            try manager.connection.startVPNTunnel(options: [AppConstants.configOptionKey: config as NSString])
        } catch {
            isStarting = false
            errorMessage = Self.describe(error)
        }
    }

    func disconnect() {
        isStarting = false
        manager?.connection.stopVPNTunnel()
    }

    /// Применить изменённый или другой профиль, не выключая VPN.
    func apply(profile: ProxyProfile) async {
        guard state == .connected, let session = manager?.connection as? NETunnelProviderSession else { return }
        do {
            let config = try ConfigBuilder.build(profile)
            try ConfigBuilder.save(config)
            try session.sendProviderMessage(Data(config.utf8)) { [weak self] response in
                guard let response, !response.isEmpty else { return }
                let text = String(decoding: response, as: UTF8.self)
                Task { @MainActor in
                    self?.errorMessage = "Не удалось применить настройки: \(text)"
                }
            }
        } catch {
            errorMessage = Self.describe(error)
        }
    }

    // MARK: - Профиль VPN в iOS

    private func preparedManager() async throws -> NETunnelProviderManager {
        let manager = self.manager ?? NETunnelProviderManager()
        let proto = (manager.protocolConfiguration as? NETunnelProviderProtocol) ?? NETunnelProviderProtocol()
        proto.providerBundleIdentifier = AppConstants.tunnelBundleID
        proto.serverAddress = "Camelot · sing-box"
        proto.disconnectOnSleep = false
        manager.protocolConfiguration = proto
        manager.localizedDescription = "Camelot"
        manager.isEnabled = true
        // При первом сохранении iOS спросит разрешение на добавление VPN-конфигурации.
        try await manager.saveToPreferences()
        // Без повторной загрузки первый запуск после создания профиля завершается ошибкой.
        try await manager.loadFromPreferences()
        attach(manager)
        return manager
    }

    private func attach(_ manager: NETunnelProviderManager) {
        self.manager = manager
        if let statusObserver {
            NotificationCenter.default.removeObserver(statusObserver)
        }
        statusObserver = NotificationCenter.default.addObserver(
            forName: .NEVPNStatusDidChange,
            object: manager.connection,
            queue: .main
        ) { [weak self] _ in
            Task { @MainActor in self?.refreshStatus() }
        }
        refreshStatus()
    }

    private func refreshStatus() {
        guard let connection = manager?.connection else {
            state = .disconnected
            return
        }
        let newState = TunnelState(connection.status)
        state = newState
        connectedSince = newState == .connected ? connection.connectedDate : nil

        if newState == .connected {
            isStarting = false
        } else if newState == .disconnected, isStarting {
            isStarting = false
            reportStartFailure(connection)
        }
    }

    private func reportStartFailure(_ connection: NEVPNConnection) {
        if let message = AppConstants.sharedDefaults.string(forKey: AppConstants.lastErrorKey), !message.isEmpty {
            errorMessage = message
            return
        }
        connection.fetchLastDisconnectError { [weak self] error in
            let text = error.map { Self.describe($0) }
                ?? "Туннель не запустился. Проверьте адрес и порт сервера."
            Task { @MainActor in self?.errorMessage = text }
        }
    }

    nonisolated static func describe(_ error: Error) -> String {
        if let vpnError = error as? NEVPNError {
            switch vpnError.code {
            case .configurationReadWriteFailed:
                return "iOS не сохранила VPN-профиль. Разрешите добавление конфигурации VPN; если запрос не появляется — проверьте подпись и entitlements."
            case .configurationInvalid:
                return "Некорректный VPN-профиль. Проверьте TUNNEL_BUNDLE_ID в Config.xcconfig."
            case .configurationDisabled:
                return "VPN-профиль Camelot выключен в Настройках."
            case .configurationStale:
                return "VPN-профиль изменился, попробуйте ещё раз."
            case .connectionFailed:
                return "Не удалось подключиться."
            case .configurationUnknown:
                return "Неизвестная ошибка конфигурации VPN."
            @unknown default:
                break
            }
        }
        return error.localizedDescription
    }
}
