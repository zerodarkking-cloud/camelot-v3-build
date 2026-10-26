import Foundation
import Libbox

struct TrafficStats: Equatable {
    var uplink: Int64 = 0
    var downlink: Int64 = 0
    var uplinkTotal: Int64 = 0
    var downlinkTotal: Int64 = 0
    var memory: Int64 = 0
    var connections: Int32 = 0
    var trafficAvailable = false
}

/// Читает статистику ядра (скорость, трафик, память) через командный сервер Libbox,
/// который extension поднимает в общем контейнере App Group (command.sock).
@MainActor
final class TrafficMonitor: ObservableObject {
    @Published private(set) var stats: TrafficStats?

    private var client: LibboxCommandClient?
    private var isLibboxReady = false

    func start() {
        guard client == nil, prepareLibbox() else { return }
        let options = LibboxCommandClientOptions()
        options.addCommand(LibboxCommandStatus)
        options.statusInterval = Int64(NSEC_PER_SEC)
        guard let client = LibboxNewCommandClient(StatusHandler(monitor: self), options) else { return }
        self.client = client
        DispatchQueue.global(qos: .utility).async {
            do {
                try client.connect()
            } catch {
                NSLog("Camelot: статистика недоступна: %@", error.localizedDescription)
            }
        }
    }

    func stop() {
        guard let client else { return }
        self.client = nil
        stats = nil
        DispatchQueue.global(qos: .utility).async {
            try? client.disconnect()
        }
    }

    fileprivate func apply(_ newStats: TrafficStats) {
        guard client != nil else { return }
        stats = newStats
    }

    private func prepareLibbox() -> Bool {
        if isLibboxReady { return true }
        do {
            try AppConstants.prepareDirectories()
        } catch {
            return false
        }
        let options = LibboxSetupOptions()
        options.basePath = AppConstants.sharedDirectory.path
        options.workingPath = AppConstants.workingDirectory.path
        options.tempPath = AppConstants.cacheDirectory.path
        var error: NSError?
        LibboxSetup(options, &error)
        if let error {
            NSLog("Camelot: LibboxSetup: %@", error.localizedDescription)
            return false
        }
        isLibboxReady = true
        return true
    }
}

private final class StatusHandler: NSObject, LibboxCommandClientHandlerProtocol {
    private weak var monitor: TrafficMonitor?

    init(monitor: TrafficMonitor) {
        self.monitor = monitor
    }

    func writeStatus(_ message: LibboxStatusMessage?) {
        guard let message else { return }
        let stats = TrafficStats(
            uplink: message.uplink,
            downlink: message.downlink,
            uplinkTotal: message.uplinkTotal,
            downlinkTotal: message.downlinkTotal,
            memory: message.memory,
            connections: message.connectionsOut,
            trafficAvailable: message.trafficAvailable
        )
        let monitor = monitor
        Task { @MainActor in monitor?.apply(stats) }
    }

    func connected() {}

    func disconnected(_: String?) {}

    func setDefaultLogLevel(_: Int32) {}

    func clearLogs() {}

    func writeLogs(_: (any LibboxLogIteratorProtocol)?) {}

    func writeGroups(_: LibboxOutboundGroupIteratorProtocol?) {}

    func writeOutbounds(_: (any LibboxOutboundGroupItemIteratorProtocol)?) {}

    func initializeClashMode(_: LibboxStringIteratorProtocol?, currentMode _: String?) {}

    func updateClashMode(_: String?) {}

    func write(_: LibboxConnectionEvents?) {}
}
