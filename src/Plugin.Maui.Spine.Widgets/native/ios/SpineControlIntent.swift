#if !targetEnvironment(macCatalyst)
import AppIntents
import Foundation

// Compiled into both the widget extension and the bridge framework, like SpineWidgetIntent: the
// extension puts the intents on its controls, and the app's own Metadata.appintents declares them,
// which is what makes iOS run perform() in the app's process. Both are LiveActivityIntent for that
// reason; iOS launches the app in the background when it is not running.

/// The intent behind every toggle control. The new value is written into the control's document
/// before the tap is relayed, so Control Center shows it even when the intent ran in the extension and
/// the app handles the tap only at its next launch.
@available(iOS 18.0, *)
struct SpineControlToggleIntent: SetValueIntent, LiveActivityIntent {
    static var title: LocalizedStringResource = "Spine control"
    static var isDiscoverable = false

    @Parameter(title: "Kind") var kind: String
    @Parameter(title: "Value") var value: Bool

    init() {}
    init(kind: String) { self.kind = kind }

    func perform() async throws -> some IntentResult {
        ControlStore.setOn(kind: kind, value)
        await ActionRelay.relay(kind: kind, actionId: "toggle", control: true, isOn: value)
        return .result()
    }
}

/// The intent behind every button control.
@available(iOS 18.0, *)
struct SpineControlButtonIntent: LiveActivityIntent {
    static var title: LocalizedStringResource = "Spine control"
    static var isDiscoverable = false

    @Parameter(title: "Kind") var kind: String

    init() {}
    init(kind: String) { self.kind = kind }

    func perform() async throws -> some IntentResult {
        await ActionRelay.relay(kind: kind, actionId: "button", control: true, isOn: nil)
        return .result()
    }
}

/// The documents the app writes per control, `spine-widgets/controls/<kind>.json` in the App Group
/// container; the extension's value provider reads them.
enum ControlStore {
    static func url(kind: String) -> URL? {
        FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: ActionLog.appGroup)?
            .appendingPathComponent("spine-widgets/controls/\(kind).json")
    }

    static func read(kind: String) -> Data? {
        url(kind: kind).flatMap { try? Data(contentsOf: $0) }
    }

    /// Sets `isOn` in the stored document and keeps the rest; a control the app never wrote is left alone.
    static func setOn(kind: String, _ isOn: Bool) {
        guard let url = url(kind: kind), let data = try? Data(contentsOf: url),
              var document = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return }
        document["isOn"] = isOn
        if let updated = try? JSONSerialization.data(withJSONObject: document) {
            try? updated.write(to: url, options: .atomic)
        }
    }
}
#endif
