import SwiftUI

/// Главный экран: кнопка включения, статус, выбранный сервер и статистика.
struct ContentView: View {
    @EnvironmentObject private var store: ProfileStore
    @EnvironmentObject private var vpn: VPNManager
    @EnvironmentObject private var traffic: TrafficMonitor

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(spacing: 24) {
                    PowerButton(state: vpn.state, action: toggle)
                        .padding(.top, 20)

                    VStack(spacing: 4) {
                        Text(vpn.state.title)
                            .font(.title2.weight(.semibold))
                        if vpn.state == .connected, let since = vpn.connectedSince {
                            Text(since, style: .timer)
                                .font(.subheadline.monospacedDigit())
                                .foregroundStyle(.secondary)
                        }
                    }

                    if let error = vpn.errorMessage {
                        ErrorBanner(text: error) { vpn.errorMessage = nil }
                    }

                    NavigationLink {
                        ServerListView()
                    } label: {
                        ServerCard(profile: store.selected)
                    }
                    .buttonStyle(.plain)

                    if vpn.state == .connected {
                        TrafficCard(stats: traffic.stats)
                    }

                    if let profile = store.selected {
                        NetworkSummary(profile: profile)
                    }
                }
                .padding(.horizontal, 20)
                .padding(.bottom, 32)
            }
            .background(Color(.systemGroupedBackground).ignoresSafeArea())
            .navigationTitle("Camelot")
        }
        .onReceive(vpn.$state) { state in
            if state == .connected {
                traffic.start()
            } else {
                traffic.stop()
            }
        }
    }

    private func toggle() {
        switch vpn.state {
        case .connected, .connecting, .reasserting:
            vpn.disconnect()
        case .disconnected, .invalid:
            guard let profile = store.selected else {
                vpn.errorMessage = "Сначала добавьте сервер"
                return
            }
            Task { await vpn.connect(profile: profile) }
        case .disconnecting:
            break
        }
    }
}

// MARK: - Компоненты главного экрана

struct PowerButton: View {
    let state: TunnelState
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            ZStack {
                Circle()
                    .fill(gradient)
                    .frame(width: 168, height: 168)
                    .shadow(color: glow.opacity(state == .connected ? 0.5 : 0.15), radius: state == .connected ? 28 : 10)
                if state.isBusy {
                    ProgressView()
                        .controlSize(.large)
                        .tint(.white)
                } else {
                    Image(systemName: "power")
                        .font(.system(size: 62, weight: .semibold))
                        .foregroundStyle(.white)
                }
            }
        }
        .buttonStyle(.plain)
        .disabled(state == .disconnecting)
        .animation(.easeInOut(duration: 0.25), value: state)
        .accessibilityLabel(state.isActive ? "Отключить VPN" : "Подключить VPN")
    }

    private var glow: Color {
        switch state {
        case .connected: return .green
        case .connecting, .reasserting: return .orange
        default: return .gray
        }
    }

    private var gradient: LinearGradient {
        let colors: [Color]
        switch state {
        case .connected:
            colors = [Color(red: 0.20, green: 0.78, blue: 0.47), Color(red: 0.05, green: 0.55, blue: 0.40)]
        case .connecting, .reasserting:
            colors = [Color(red: 1.0, green: 0.68, blue: 0.25), Color(red: 0.93, green: 0.45, blue: 0.13)]
        default:
            colors = [Color(red: 0.42, green: 0.45, blue: 0.53), Color(red: 0.25, green: 0.27, blue: 0.34)]
        }
        return LinearGradient(colors: colors, startPoint: .topLeading, endPoint: .bottomTrailing)
    }
}

struct ServerCard: View {
    let profile: ProxyProfile?

    var body: some View {
        HStack(spacing: 14) {
            Image(systemName: profile == nil ? "plus.circle.fill" : "server.rack")
                .font(.title2)
                .foregroundStyle(Color.accentColor)
                .frame(width: 36)
            VStack(alignment: .leading, spacing: 3) {
                if let profile {
                    Text(profile.name)
                        .font(.headline)
                    Text(profile.displayAddress)
                        .font(.subheadline.monospaced())
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                        .truncationMode(.middle)
                    Text(profile.protocolTitle)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                } else {
                    Text("Добавить сервер")
                        .font(.headline)
                    Text("Вручную или по ссылке ss:// / vless://")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                }
            }
            Spacer(minLength: 0)
            Image(systemName: "chevron.right")
                .font(.footnote.weight(.semibold))
                .foregroundStyle(.tertiary)
        }
        .padding(16)
        .frame(maxWidth: .infinity)
        .background(Color(.secondarySystemGroupedBackground), in: RoundedRectangle(cornerRadius: 16, style: .continuous))
    }
}

struct TrafficCard: View {
    let stats: TrafficStats?

    var body: some View {
        VStack(spacing: 12) {
            HStack(spacing: 12) {
                StatTile(icon: "arrow.down", title: "Загрузка", value: stats.map { ByteFormat.speed($0.downlink) } ?? "—")
                StatTile(icon: "arrow.up", title: "Отдача", value: stats.map { ByteFormat.speed($0.uplink) } ?? "—")
            }
            HStack(spacing: 12) {
                StatTile(
                    icon: "chart.bar",
                    title: "Всего ↓ / ↑",
                    value: stats.map { "\(ByteFormat.bytes($0.downlinkTotal)) / \(ByteFormat.bytes($0.uplinkTotal))" } ?? "—"
                )
                StatTile(icon: "memorychip", title: "Память ядра", value: stats.map { ByteFormat.bytes($0.memory) } ?? "—")
            }
        }
    }
}

struct StatTile: View {
    let icon: String
    let title: String
    let value: String

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Label(title, systemImage: icon)
                .font(.caption)
                .foregroundStyle(.secondary)
            Text(value)
                .font(.callout.weight(.semibold).monospacedDigit())
                .lineLimit(1)
                .minimumScaleFactor(0.7)
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color(.secondarySystemGroupedBackground), in: RoundedRectangle(cornerRadius: 14, style: .continuous))
    }
}

struct NetworkSummary: View {
    let profile: ProxyProfile

    var body: some View {
        Text("MTU \(profile.mtu) · к VPS: \(profile.serverIPPreference.title.lowercased()) · IPv6 для сайтов: \(profile.tunnelIPv6 ? "вкл" : "выкл")")
            .font(.footnote)
            .foregroundStyle(.secondary)
            .multilineTextAlignment(.center)
    }
}

struct ErrorBanner: View {
    let text: String
    let onClose: () -> Void

    var body: some View {
        HStack(alignment: .top, spacing: 10) {
            Image(systemName: "exclamationmark.triangle.fill")
                .foregroundStyle(.orange)
            Text(text)
                .font(.subheadline)
                .frame(maxWidth: .infinity, alignment: .leading)
            Button(action: onClose) {
                Image(systemName: "xmark")
                    .font(.footnote.weight(.semibold))
                    .foregroundStyle(.secondary)
            }
            .accessibilityLabel("Закрыть")
        }
        .padding(14)
        .background(Color.orange.opacity(0.12), in: RoundedRectangle(cornerRadius: 14, style: .continuous))
    }
}

/// Форматирование байтов по-русски, независимо от региона устройства.
enum ByteFormat {
    static func bytes(_ value: Int64) -> String {
        let units = ["Б", "КБ", "МБ", "ГБ", "ТБ"]
        var amount = Double(max(value, 0))
        var unit = 0
        while amount >= 1024, unit < units.count - 1 {
            amount /= 1024
            unit += 1
        }
        if unit == 0 {
            return "\(Int(amount)) \(units[unit])"
        }
        let text = amount >= 100 ? String(format: "%.0f", amount) : String(format: "%.1f", amount)
        return "\(text.replacingOccurrences(of: ".", with: ",")) \(units[unit])"
    }

    static func speed(_ bytesPerSecond: Int64) -> String {
        "\(bytes(bytesPerSecond))/с"
    }
}
