import Foundation
import Libbox
import Network
import NetworkExtension

/// Мост между ядром sing-box и iOS.
///
/// Сигнатуры методов соответствуют `LibboxPlatformInterfaceProtocol` и
/// `LibboxCommandServerHandlerProtocol` из sing-box v1.14.x. Если соберёте Libbox
/// другой версии и Xcode скажет «does not conform to protocol», сверьте список методов
/// с experimental/libbox/platform.go нужной версии.
final class PlatformInterface: NSObject, LibboxPlatformInterfaceProtocol, LibboxCommandServerHandlerProtocol {
    private unowned let tunnel: PacketTunnelProvider
    private var networkSettings: NEPacketTunnelNetworkSettings?
    private var pathMonitor: NWPathMonitor?
    private var lastNetworkPath: String?

    init(tunnel: PacketTunnelProvider) {
        self.tunnel = tunnel
    }

    func reset() {
        networkSettings = nil
        pathMonitor?.cancel()
        pathMonitor = nil
        lastNetworkPath = nil
    }

    // MARK: - TUN (здесь применяются MTU и IPv6)

    func openTun(_ options: LibboxTunOptionsProtocol?, ret0_: UnsafeMutablePointer<Int32>?) throws {
        try runBlocking { [self] in
            try await openTunAsync(options, ret0_)
        }
    }

    private func openTunAsync(_ options: LibboxTunOptionsProtocol?, _ ret0_: UnsafeMutablePointer<Int32>?) async throws {
        guard let options else { throw platformError("Ядро не передало параметры TUN") }
        guard let ret0_ else { throw platformError("Нет указателя для дескриптора TUN") }

        let settings = NEPacketTunnelNetworkSettings(tunnelRemoteAddress: "127.0.0.1")

        if options.getAutoRoute() {
            // MTU берётся из конфига (по умолчанию 1280). Значение ниже 1280 iOS не примет для IPv6.
            let mtu = max(options.getMTU(), Int32(AppConstants.defaultMTU))
            settings.mtu = NSNumber(value: mtu)

            // DNS: адрес внутри туннеля, запросы перехватывает ядро (hijack-dns).
            var dnsSettings: NEDNSSettings?
            if options.getDNSMode()?.value != LibboxDNSModeDisabled {
                let iterator = try options.getDNSServerAddress()
                var servers: [String] = []
                while iterator.hasNext() {
                    servers.append(iterator.next())
                }
                if !servers.isEmpty {
                    dnsSettings = NEDNSSettings(servers: servers)
                }
            }

            // IPv4
            var ipv4Addresses: [String] = []
            var ipv4Masks: [String] = []
            if let iterator = options.getInet4Address() {
                while iterator.hasNext() {
                    guard let prefix = iterator.next() else { continue }
                    ipv4Addresses.append(prefix.address())
                    ipv4Masks.append(prefix.mask())
                }
            }
            var ipv4Routes: [NEIPv4Route] = []
            if let iterator = options.getInet4RouteAddress(), iterator.hasNext() {
                while iterator.hasNext() {
                    guard let prefix = iterator.next() else { continue }
                    ipv4Routes.append(NEIPv4Route(destinationAddress: prefix.address(), subnetMask: prefix.mask()))
                }
            } else {
                ipv4Routes.append(NEIPv4Route.default())
            }
            var ipv4Excluded: [NEIPv4Route] = []
            if let iterator = options.getInet4RouteExcludeAddress() {
                while iterator.hasNext() {
                    guard let prefix = iterator.next() else { continue }
                    ipv4Excluded.append(NEIPv4Route(destinationAddress: prefix.address(), subnetMask: prefix.mask()))
                }
            }
            if !ipv4Addresses.isEmpty {
                let ipv4 = NEIPv4Settings(addresses: ipv4Addresses, subnetMasks: ipv4Masks)
                ipv4.includedRoutes = ipv4Routes
                ipv4.excludedRoutes = ipv4Excluded
                settings.ipv4Settings = ipv4
            }

            // IPv6. Маршрут ::/0 в туннель обязателен: МТС выдаёт телефону IPv6,
            // и без него IPv6-трафик приложений ушёл бы мимо VPN.
            var ipv6Addresses: [String] = []
            var ipv6Prefixes: [NSNumber] = []
            if let iterator = options.getInet6Address() {
                while iterator.hasNext() {
                    guard let prefix = iterator.next() else { continue }
                    ipv6Addresses.append(prefix.address())
                    ipv6Prefixes.append(NSNumber(value: prefix.prefix()))
                }
            }
            var ipv6Routes: [NEIPv6Route] = []
            if let iterator = options.getInet6RouteAddress(), iterator.hasNext() {
                while iterator.hasNext() {
                    guard let prefix = iterator.next() else { continue }
                    ipv6Routes.append(NEIPv6Route(destinationAddress: prefix.address(), networkPrefixLength: NSNumber(value: prefix.prefix())))
                }
            } else {
                ipv6Routes.append(NEIPv6Route.default())
            }
            var ipv6Excluded: [NEIPv6Route] = []
            if let iterator = options.getInet6RouteExcludeAddress() {
                while iterator.hasNext() {
                    guard let prefix = iterator.next() else { continue }
                    ipv6Excluded.append(NEIPv6Route(destinationAddress: prefix.address(), networkPrefixLength: NSNumber(value: prefix.prefix())))
                }
            }
            if !ipv6Addresses.isEmpty {
                let ipv6 = NEIPv6Settings(addresses: ipv6Addresses, networkPrefixLengths: ipv6Prefixes)
                ipv6.includedRoutes = ipv6Routes
                ipv6.excludedRoutes = ipv6Excluded
                settings.ipv6Settings = ipv6
            }

            let hasDefaultRoute = ipv4Routes.contains {
                $0.destinationAddress == "0.0.0.0" && $0.destinationSubnetMask == "0.0.0.0"
            }
            if !hasDefaultRoute {
                dnsSettings?.matchDomains = [""]
                dnsSettings?.matchDomainsNoSearch = true
            }
            settings.dnsSettings = dnsSettings
        }

        networkSettings = settings
        try await tunnel.setTunnelNetworkSettings(settings)

        if let fd = tunnel.packetFlow.value(forKeyPath: "socket.fileDescriptor") as? Int32 {
            ret0_.pointee = fd
            return
        }
        let fd = LibboxGetTunnelFileDescriptor()
        guard fd != -1 else { throw platformError("Не найден файловый дескриптор utun") }
        ret0_.pointee = fd
    }

    // MARK: - Сеть устройства

    func usePlatformAutoDetectControl() -> Bool {
        false
    }

    func autoDetectControl(_: Int32) throws {}

    func startDefaultInterfaceMonitor(_ listener: LibboxInterfaceUpdateListenerProtocol?) throws {
        guard let listener else { return }
        let monitor = NWPathMonitor()
        pathMonitor = monitor
        let semaphore = DispatchSemaphore(value: 0)
        monitor.pathUpdateHandler = { [weak self, weak monitor] path in
            self?.updateDefaultInterface(listener, path)
            semaphore.signal()
            monitor?.pathUpdateHandler = { [weak self] path in
                self?.updateDefaultInterface(listener, path)
            }
        }
        monitor.start(queue: DispatchQueue.global())
        semaphore.wait()
    }

    private func updateDefaultInterface(_ listener: LibboxInterfaceUpdateListenerProtocol, _ path: Network.NWPath) {
        let description = describe(path)
        listener.updateNetworkPath(description)
        if description == lastNetworkPath { return }
        lastNetworkPath = description
        guard path.status != .unsatisfied, let interface = path.availableInterfaces.first else {
            listener.updateDefaultInterface("", interfaceIndex: -1, isExpensive: false, isConstrained: false)
            return
        }
        listener.updateDefaultInterface(
            interface.name,
            interfaceIndex: Int32(interface.index),
            isExpensive: path.isExpensive,
            isConstrained: path.isConstrained
        )
    }

    private func describe(_ path: Network.NWPath) -> String {
        var parts: [String] = ["\(path.status)"]
        parts.append(path.availableInterfaces.map { "\($0.name)#\($0.index)" }.joined(separator: ","))
        if path.supportsIPv4 { parts.append("ipv4") }
        if path.supportsIPv6 { parts.append("ipv6") }
        if path.supportsDNS { parts.append("dns") }
        if path.isExpensive { parts.append("expensive") }
        if path.isConstrained { parts.append("constrained") }
        return parts.joined(separator: " ")
    }

    func closeDefaultInterfaceMonitor(_: LibboxInterfaceUpdateListenerProtocol?) throws {
        pathMonitor?.cancel()
        pathMonitor = nil
        lastNetworkPath = nil
    }

    func getInterfaces() throws -> LibboxNetworkInterfaceIteratorProtocol {
        guard let pathMonitor else { throw platformError("Мониторинг сети не запущен") }
        let path = pathMonitor.currentPath
        guard path.status != .unsatisfied else { return InterfaceIterator([]) }
        let interfaces = path.availableInterfaces.map { item -> LibboxNetworkInterface in
            let interface = LibboxNetworkInterface()
            interface.name = item.name
            interface.index = Int32(item.index)
            switch item.type {
            case .wifi: interface.type = LibboxInterfaceTypeWIFI
            case .cellular: interface.type = LibboxInterfaceTypeCellular
            case .wiredEthernet: interface.type = LibboxInterfaceTypeEthernet
            default: interface.type = LibboxInterfaceTypeOther
            }
            return interface
        }
        return InterfaceIterator(interfaces)
    }

    func underNetworkExtension() -> Bool {
        true
    }

    func includeAllNetworks() -> Bool {
        false
    }

    func clearDNSCache() {
        guard let networkSettings else { return }
        runBlocking { [self] in
            tunnel.reasserting = true
            defer { tunnel.reasserting = false }
            try? await tunnel.setTunnelNetworkSettings(nil)
            try? await tunnel.setTunnelNetworkSettings(networkSettings)
        }
    }

    func readWIFIState() -> LibboxWIFIState? {
        nil
    }

    func localDNSTransport() -> (any LibboxLocalDNSTransportProtocol)? {
        nil
    }

    func findConnectionOwner(_: Int32, sourceAddress _: String?, sourcePort _: Int32, destinationAddress _: String?, destinationPort _: Int32) throws -> LibboxConnectionOwner {
        throw platformError("Не поддерживается на iOS")
    }

    // MARK: - Уведомления и соседи (не используются)

    func send(_: LibboxNotification?) throws {}

    func cancelNotification(_: String?, typeID _: Int32) throws {}

    func startNeighborMonitor(_: LibboxNeighborUpdateListenerProtocol?) throws {}

    func closeNeighborMonitor(_: LibboxNeighborUpdateListenerProtocol?) throws {}

    func registerMyInterface(_: String?) {}

    // MARK: - SSH / Tailscale / bridge (не поддерживаются на iOS)

    func usePlatformShell() -> Bool {
        false
    }

    func checkPlatformShell() throws {
        throw platformError("SSH-сервер не поддерживается")
    }

    func openShellSession(_: LibboxPlatformUser?, command _: String?, environ _: (any LibboxStringIteratorProtocol)?, term _: String?, rows _: Int32, cols _: Int32) throws -> any LibboxShellSessionProtocol {
        throw platformError("SSH-сервер не поддерживается")
    }

    func readSystemSSHHostKey(_ error: NSErrorPointer) -> String {
        error?.pointee = platformError("Не поддерживается на iOS")
        return ""
    }

    func lookupSFTPServer(_ error: NSErrorPointer) -> String {
        error?.pointee = platformError("Не поддерживается на iOS")
        return ""
    }

    func lookupUser(_: String?) throws -> LibboxPlatformUser {
        throw platformError("Не поддерживается на iOS")
    }

    func tailscaleHostname() -> String {
        "camelot-ios"
    }

    func usePlatformBridge() -> Bool {
        false
    }

    func createBridge(_: LibboxBridgeOptions?) throws -> any LibboxBridgeSessionProtocol {
        throw platformError("Не поддерживается на iOS")
    }

    // MARK: - LibboxCommandServerHandlerProtocol

    func serviceStop() throws {
        tunnel.cancelTunnelWithError(nil)
    }

    func serviceReload() throws {
        try tunnel.reload()
    }

    func getSystemProxyStatus() throws -> LibboxSystemProxyStatus {
        LibboxSystemProxyStatus()
    }

    func setSystemProxyEnabled(_: Bool) throws {}

    func triggerNativeCrash() throws {}

    func writeDebugMessage(_ message: String?) {
        guard let message else { return }
        NSLog("Camelot[core]: %@", message)
    }

    func connectSSHAgent(_: UnsafeMutablePointer<Int32>?) throws {
        throw platformError("SSH-агент не поддерживается")
    }

    // MARK: - Вспомогательное

    private func platformError(_ message: String) -> NSError {
        NSError(domain: "Camelot.PlatformInterface", code: 0, userInfo: [NSLocalizedDescriptionKey: message])
    }
}

private final class InterfaceIterator: NSObject, LibboxNetworkInterfaceIteratorProtocol {
    private var iterator: IndexingIterator<[LibboxNetworkInterface]>
    private var current: LibboxNetworkInterface?

    init(_ items: [LibboxNetworkInterface]) {
        iterator = items.makeIterator()
    }

    func hasNext() -> Bool {
        current = iterator.next()
        return current != nil
    }

    func next() -> LibboxNetworkInterface? {
        current
    }
}
