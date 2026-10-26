import SwiftUI
import UIKit

/// Список серверов: выбор, добавление вручную или по ссылке, редактирование, удаление.
struct ServerListView: View {
    @EnvironmentObject private var store: ProfileStore
    @EnvironmentObject private var vpn: VPNManager

    @State private var editing: ProxyProfile?
    @State private var importError: String?

    var body: some View {
        List {
            if store.profiles.isEmpty {
                Section {
                    Text("Серверов пока нет. Нажмите «+» и введите данные вручную или вставьте ссылку ss:// или vless://, скопированную после настройки VPS.")
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
        .navigationTitle("Серверы")
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                Menu {
                    Button {
                        importFromClipboard()
                    } label: {
                        Label("Вставить ссылку из буфера", systemImage: "doc.on.clipboard")
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
    }

    private func select(_ profile: ProxyProfile) {
        guard profile.id != store.selected?.id else { return }
        store.selectedID = profile.id
        Task { await vpn.apply(profile: profile) }
    }

    private func importFromClipboard() {
        guard let text = UIPasteboard.general.string, !text.isEmpty else {
            importError = "Буфер обмена пуст. Скопируйте ссылку ss:// или vless:// и попробуйте снова."
            return
        }
        do {
            // Открываем редактор, чтобы проверить данные перед сохранением.
            editing = try LinkParser.parse(text)
        } catch {
            importError = error.localizedDescription
        }
    }
}
