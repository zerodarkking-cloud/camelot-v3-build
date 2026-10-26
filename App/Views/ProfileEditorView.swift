import SwiftUI

/// Редактор сервера.
struct ProfileEditorView: View {
    @Environment(\.dismiss) private var dismiss

    @State private var profile: ProxyProfile
    private let isNew: Bool
    private let onSave: (ProxyProfile) -> Void

    @State private var showSecret = false
    @State private var validationError: String?

    init(profile: ProxyProfile, isNew: Bool, onSave: @escaping (ProxyProfile) -> Void) {
        _profile = State(initialValue: profile)
        self.isNew = isNew
        self.onSave = onSave
    }

    var body: some View {
        Form {
            Section("Основное") {
                TextField("Название", text: $profile.name)
                Picker("Протокол", selection: $profile.proto) {
                    ForEach(ProxyProtocol.allCases) { item in
                        Text(item.title).tag(item)
                    }
                }
            }

            Section {
                TextField("IPv6, IPv4 или домен", text: $profile.server)
                    .keyboardType(.URL)
                    .textInputAutocapitalization(.never)
                    .autocorrectionDisabled()
                    .font(.body.monospaced())
                HStack {
                    Text("Порт")
                    Spacer()
                    TextField("443", value: $profile.port, format: .number.grouping(.never))
                        .keyboardType(.numberPad)
                        .multilineTextAlignment(.trailing)
                        .frame(maxWidth: 100)
                }
            } header: {
                Text("Сервер")
            } footer: {
                Text("Для МТС укажите IPv6-адрес VPS или домен с AAAA-записью. На VPS inbound должен слушать «::», иначе подключения по IPv6 не принимаются.")
            }

            switch profile.proto {
            case .shadowsocks:
                shadowsocksSection
            case .vless:
                vlessSection
            }

            networkSection
        }
        .navigationTitle(isNew ? "Новый сервер" : "Сервер")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .cancellationAction) {
                Button("Отмена") { dismiss() }
            }
            ToolbarItem(placement: .confirmationAction) {
                Button("Сохранить", action: save)
                    .fontWeight(.semibold)
            }
        }
        .alert("Проверьте настройки", isPresented: Binding(
            get: { validationError != nil },
            set: { if !$0 { validationError = nil } }
        )) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(validationError ?? "")
        }
    }

    // MARK: - Секции

    private var shadowsocksSection: some View {
        Section {
            Picker("Шифрование", selection: $profile.ssMethod) {
                ForEach(ProxyProfile.ssMethods, id: \.self) { method in
                    Text(method).tag(method)
                }
            }
            secretField("Пароль", text: $profile.ssPassword)
        } header: {
            Text("Shadowsocks")
        } footer: {
            if let length = ProxyProfile.ss2022KeyLength(profile.ssMethod) {
                Text("Для этого метода пароль — ключ base64 длиной \(length) байт. Сгенерировать: sing-box generate rand --base64 \(length)")
            }
        }
    }

    private var vlessSection: some View {
        Section {
            TextField("UUID", text: $profile.uuid)
                .textInputAutocapitalization(.never)
                .autocorrectionDisabled()
                .font(.body.monospaced())
            Toggle("XTLS Vision (flow)", isOn: Binding(
                get: { profile.flow == "xtls-rprx-vision" },
                set: { profile.flow = $0 ? "xtls-rprx-vision" : "" }
            ))
            Picker("Безопасность", selection: $profile.security) {
                ForEach(VLESSSecurity.allCases) { item in
                    Text(item.title).tag(item)
                }
            }
            TextField("SNI (server name)", text: $profile.sni)
                .textInputAutocapitalization(.never)
                .autocorrectionDisabled()
            Picker("Отпечаток TLS", selection: $profile.fingerprint) {
                ForEach(ProxyProfile.fingerprints, id: \.self) { item in
                    Text(item).tag(item)
                }
            }
            if profile.security == .reality {
                secretField("Публичный ключ (pbk)", text: $profile.realityPublicKey)
                TextField("Short ID (sid)", text: $profile.realityShortID)
                    .textInputAutocapitalization(.never)
                    .autocorrectionDisabled()
                    .font(.body.monospaced())
            }
        } header: {
            Text("VLESS")
        }
    }

    private var networkSection: some View {
        Section {
            Stepper(value: $profile.mtu, in: ProxyProfile.mtuRange, step: 20) {
                HStack {
                    Text("MTU")
                    Spacer()
                    Text("\(profile.mtu)")
                        .monospacedDigit()
                        .foregroundStyle(profile.mtu == AppConstants.defaultMTU ? Color.secondary : Color.orange)
                }
            }
            Picker("Подключение к VPS", selection: $profile.serverIPPreference) {
                ForEach(ServerIPPreference.allCases) { item in
                    Text(item.title).tag(item)
                }
            }
            Toggle("IPv6 для сайтов", isOn: $profile.tunnelIPv6)
            Toggle("Блокировать QUIC (UDP 443)", isOn: $profile.blockQUIC)
        } header: {
            Text("Сеть (МТС)")
        } footer: {
            Text("MTU 1280 — минимальный для IPv6, крупные пакеты не теряются в сотовой сети. «Подключение к VPS» действует, если адрес сервера — домен. «IPv6 для сайтов» включайте, только если у VPS есть исходящий IPv6. Блокировка QUIC переводит приложения на TCP — помогает, если видео грузится рывками.")
        }
    }

    private func secretField(_ title: String, text: Binding<String>) -> some View {
        HStack {
            Group {
                if showSecret {
                    TextField(title, text: text)
                } else {
                    SecureField(title, text: text)
                }
            }
            .textInputAutocapitalization(.never)
            .autocorrectionDisabled()
            .font(.body.monospaced())
            Button {
                showSecret.toggle()
            } label: {
                Image(systemName: showSecret ? "eye.slash" : "eye")
                    .foregroundStyle(.secondary)
            }
            .buttonStyle(.borderless)
            .accessibilityLabel(showSecret ? "Скрыть" : "Показать")
        }
    }

    private func save() {
        var result = profile
        result.server = result.normalizedServer
        result.name = result.name.trimmingCharacters(in: .whitespaces)
        if result.name.isEmpty { result.name = result.normalizedServer }
        do {
            try result.validate()
            onSave(result)
            dismiss()
        } catch {
            validationError = error.localizedDescription
        }
    }
}
