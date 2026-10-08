#if !targetEnvironment(macCatalyst)
import AppIntents
import SwiftUI
import WidgetKit

// iOS 18 controls: Control Center, the Lock Screen and the Action button. The app writes each control's
// state as a document in the container (Plugin.Maui.Spine.Common.ControlState); this draws it and puts
// the Spine intents on it. The generated bundle wraps one ControlWidget per <SpineControl> item.

/// The same schema as Plugin.Maui.Spine.Common.ControlState.
struct ControlDocument: Decodable {
    var title: String
    var icon: String?
    var symbol: String?
    var isOn: Bool?
    var status: String?
    var tint: String?
}

extension Manifest {
    static func control(_ index: Int) -> Control {
        let controls = current.controls ?? []
        return controls.indices.contains(index)
            ? controls[index]
            : Control(kind: "spine.control.\(index)", displayName: "Control", description: "", type: "button", icon: nil)
    }
}

@available(iOS 18.0, *)
struct SpineControlValueProvider: ControlValueProvider {
    let entry: Manifest.Control

    /// The gallery's preview: what the app last stored, or the item's name and icon before it ever ran.
    var previewValue: ControlDocument {
        stored ?? ControlDocument(title: entry.displayName, icon: entry.icon, isOn: entry.type == "toggle" ? false : nil)
    }

    func currentValue() async throws -> ControlDocument { previewValue }

    private var stored: ControlDocument? {
        guard let data = ControlStore.read(kind: entry.kind) else { return nil }
        do { return try JSONDecoder().decode(ControlDocument.self, from: data) }
        catch { NSLog("[SpineWidgets] control \(entry.kind) failed to decode: \(error)"); return nil }
    }
}

enum ControlSymbols {
    /// iOS draws only symbols in a control, so a name without an SF Symbol of its own shows a question mark.
    static func name(_ document: ControlDocument, _ entry: Manifest.Control) -> String {
        document.symbol ?? document.icon ?? entry.icon ?? "questionmark"
    }
}

/// A toggle. iOS flips it the moment it is tapped and says On or Off itself, in the user's language.
@available(iOS 18.0, *)
func spineToggleConfiguration(index: Int) -> some ControlWidgetConfiguration {
    let entry = Manifest.control(index)
    return StaticControlConfiguration(kind: entry.kind, provider: SpineControlValueProvider(entry: entry)) { document in
        ControlWidgetToggle(isOn: document.isOn == true, action: SpineControlToggleIntent(kind: entry.kind)) {
            Label(document.title, systemImage: ControlSymbols.name(document, entry))
        }
        .tint(document.tint.map { Palette.color($0) })
    }
    .displayName(LocalizedStringResource(String.LocalizationValue(entry.displayName)))
    .description(LocalizedStringResource(String.LocalizationValue(entry.description)))
}

/// A button; its status, when the app gives one, is the second line.
@available(iOS 18.0, *)
func spineButtonConfiguration(index: Int) -> some ControlWidgetConfiguration {
    let entry = Manifest.control(index)
    return StaticControlConfiguration(kind: entry.kind, provider: SpineControlValueProvider(entry: entry)) { document in
        ControlWidgetButton(action: SpineControlButtonIntent(kind: entry.kind)) {
            Label {
                Text(document.title)
                if let status = document.status { Text(status) }
            } icon: {
                Image(systemName: ControlSymbols.name(document, entry))
            }
        }
        .tint(document.tint.map { Palette.color($0) })
    }
    .displayName(LocalizedStringResource(String.LocalizationValue(entry.displayName)))
    .description(LocalizedStringResource(String.LocalizationValue(entry.description)))
}
#endif
