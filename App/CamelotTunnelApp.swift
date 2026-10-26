import SwiftUI

@main
struct CamelotTunnelApp: App {
    @StateObject private var store = ProfileStore()
    @StateObject private var vpn = VPNManager()
    @StateObject private var traffic = TrafficMonitor()

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(store)
                .environmentObject(vpn)
                .environmentObject(traffic)
                .environment(\.locale, Locale(identifier: "ru_RU"))
        }
    }
}
