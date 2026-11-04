import Foundation
#if !targetEnvironment(macCatalyst)
import ActivityKit

// Compiled into both the widget extension and the bridge framework. ActivityKit matches the
// attributes type by name across the two processes, so this file must be identical in both —
// which the build guarantees by compiling the same source twice.
public struct SpineActivityAttributes: ActivityAttributes {
    /// The layout as JSON. A push carries it as an object under "layout", whose quotes need no escaping; before 0.1.27
    /// it was a string under "json", which an activity started then still holds, so both are read and "layout" written.
    public struct ContentState: Codable, Hashable {
        public var layout: JSONValue

        public init(json: String) {
            let value = (try? JSONDecoder().decode(JSONValue.self, from: Data(json.utf8))) ?? .null
            // A template's values, as a push carries them, fill the template in here too.
            if case .object(let fields) = value, case .string(let id)? = fields["t"] {
                layout = InstalledTemplates.template(id).map { LayoutTemplates.render($0, data: fields["d"] ?? .object([:])) } ?? .null
            } else {
                layout = value
            }
        }

        // A push to a device that has the activity's template carries its id under "t" and the values under "d" instead
        // of the layout (LiveActivityTemplate in C#): the template is filled in here, from the App Group, where the app
        // installed it. Stored again, the state holds the layout, so the template is read once per push.
        private enum CodingKeys: String, CodingKey { case layout, json, t, d }

        public init(from decoder: Decoder) throws {
            let container = try decoder.container(keyedBy: CodingKeys.self)
            if let layout = try container.decodeIfPresent(JSONValue.self, forKey: .layout) {
                self.layout = layout
            } else if let id = try container.decodeIfPresent(String.self, forKey: .t) {
                let data = try container.decodeIfPresent(JSONValue.self, forKey: .d) ?? .object([:])
                layout = InstalledTemplates.template(id).map { LayoutTemplates.render($0, data: data) } ?? .null
                if case .null = layout { NSLog("[SpineWidgets] no template \(id) is installed; the push draws nothing") }
            } else {
                self = ContentState(json: try container.decode(String.self, forKey: .json))
            }
        }

        public func encode(to encoder: Encoder) throws {
            var container = encoder.container(keyedBy: CodingKeys.self)
            try container.encode(layout, forKey: .layout)
        }
    }

    public var kind: String
    public init(kind: String) { self.kind = kind }
}

/// The templates the app installed in the App Group (spine-widgets/templates/<id>.json), each read once.
enum InstalledTemplates {
    private static let lock = NSLock()
    nonisolated(unsafe) private static var cache: [String: JSONValue] = [:]

    static func template(_ id: String) -> JSONValue? {
        lock.lock(); defer { lock.unlock() }
        if let known = cache[id] { return known }
        guard !id.contains("/"),
              let url = FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: ActionLog.appGroup)?
                .appendingPathComponent("spine-widgets/templates/\(id).json"),
              let data = try? Data(contentsOf: url),
              let template = try? JSONDecoder().decode(JSONValue.self, from: data) else { return nil }
        cache[id] = template
        return template
    }
}
#endif
