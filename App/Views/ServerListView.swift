import SwiftUI
import UIKit

/// Список серверов: выбор, добавление вручную или по ссылке, редактирование, удаление.
struct ServerListView: View {
    @EnvironmentObject private var store: ProfileStore
    @EnvironmentObject private var vpn: VPNManager

    @State private var editing: ProxyProfile?
    @State private var importError: String?
    @State private var importInfo: String?

    var body: some View {
        List {
            if store.profiles.isEmpty {
                Section {
                    Text("Прокси пока нет. Скопируйте строку прокси, например socks5://host:port:логин:пароль, и нажмите «+» → «Вставить из буфера». Можно вставить сразу список, по одному прокси в строке.")
                        .foregroundStyle(.secondary)
                }
            }

            Section {
                ForEach(store.profiles) { profile in
                    HStack(spacing: 12) {
                        Button {
                            select(profile)
                        } label: {
                            HStack(spacing: 12) {
                                Image(systemName: profile.id == store.selected?.id ? "checkmark.circle.fill" : "circle")
                                    .font(.title3)
                                    .foregroundStyle(profile.id == store.selected?.id ? Color.accentColor : Color.secondary)
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(profile.name)
                                        .foregroundStyle(.primary)
                                    Text(profile.displayAddress)
                                        .font(.caption.monospaced())
                                        .foregroundStyle(.secondary)
                                        .lineLimit(1)
                                        .truncationMode(.middle)
                                }
                                Spacer(minLength: 0)
                            }
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)

                        Button {
                            editing = profile
                        } label: {
                            Image(systemName: "slider.horizontal.3")
                        }
                        .buttonStyle(.borderless)
                        .accessibilityLabel("Настроить")
                    }
                }
                .onDelete { store.delete(at: $0) }
            }
        }
        .navigationTitle("Прокси")
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                Menu {
                    Button {
                        importFromClipboard()
                    } label: {
                        Label("Вставить из буфера", systemImage: "doc.on.clipboard")
                    }
                    Button {
                        editing = ProxyProfile()
                    } label: {
                        Label("Ввести вручную", systemImage: "square.and.pencil")
                    }
                } label: {
                    Image(systemName: "plus")
                }
                .accessibilityLabel("Добавить сервер")
            }
        }
        .sheet(item: $editing) { profile in
            NavigationStack {
                ProfileEditorView(profile: profile, isNew: !store.contains(profile)) { saved in
                    store.upsert(saved)
                    if saved.id == store.selected?.id {
                        Task { await vpn.apply(profile: saved) }
                    }
                }
            }
        }
        .alert("Не удалось импортировать", isPresented: Binding(
            get: { importError != nil },
            set: { if !$0 { importError = nil } }
        )) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(importError ?? "")
        }
        .alert("Импорт", isPresented: Binding(
            get: { importInfo != nil },
            set: { if !$0 { importInfo = nil } }
        )) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(importInfo ?? "")
        }
    }

    private func select(_ profile: ProxyProfile) {
        guard profile.id != store.selected?.id else { return }
        store.selectedID = profile.id
        Task { await vpn.apply(profile: profile) }
    }

    private func importFromClipboard() {
        guard let text = UIPasteboard.general.string, !text.isEmpty else {
            importError = "Буфер обмена пуст. Скопируйте строку прокси (socks5://, http://, ss:// или vless://) и попробуйте снова."
            return
        }
        let result = LinkParser.parseMany(text)
        switch result.profiles.count {
        case 0:
            importError = result.errors.first ?? "Не удалось распознать прокси в буфере обмена."
        case 1 where result.errors.isEmpty:
            // Один прокси — открываем редактор, чтобы проверить данные перед сохранением.
            editing = result.profiles[0]
        default:
            for profile in result.profiles {
                store.upsert(profile)
            }
            var message = "Добавлено прокси: \(result.profiles.count)."
            if !result.errors.isEmpty {
                message += " Не распознано строк: \(result.errors.count) (\(result.errors[0]))."
            }
            importInfo = message
        }
    }
}
