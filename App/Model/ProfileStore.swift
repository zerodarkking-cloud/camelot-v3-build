import Foundation

/// Список серверов и выбранный сервер. Хранится в общей UserDefaults App Group.
@MainActor
final class ProfileStore: ObservableObject {
    @Published private(set) var profiles: [ProxyProfile] = []
    @Published var selectedID: UUID? {
        didSet { persist() }
    }

    private let defaults = AppConstants.sharedDefaults
    private let profilesKey = "profiles.v1"
    private let selectedKey = "selectedProfile.v1"

    init() {
        if let data = defaults.data(forKey: profilesKey),
           let decoded = try? JSONDecoder().decode([ProxyProfile].self, from: data) {
            profiles = decoded
        }
        if let raw = defaults.string(forKey: selectedKey), let id = UUID(uuidString: raw) {
            selectedID = id
        }
    }

    var selected: ProxyProfile? {
        profiles.first { $0.id == selectedID } ?? profiles.first
    }

    func contains(_ profile: ProxyProfile) -> Bool {
        profiles.contains { $0.id == profile.id }
    }

    func upsert(_ profile: ProxyProfile) {
        if let index = profiles.firstIndex(where: { $0.id == profile.id }) {
            profiles[index] = profile
        } else {
            profiles.append(profile)
        }
        if selectedID == nil || !profiles.contains(where: { $0.id == selectedID }) {
            selectedID = profile.id
        }
        persist()
    }

    func delete(at offsets: IndexSet) {
        profiles.remove(atOffsets: offsets)
        if !profiles.contains(where: { $0.id == selectedID }) {
            selectedID = profiles.first?.id
        }
        persist()
    }

    private func persist() {
        if let data = try? JSONEncoder().encode(profiles) {
            defaults.set(data, forKey: profilesKey)
        }
        defaults.set(selectedID?.uuidString, forKey: selectedKey)
    }
}
