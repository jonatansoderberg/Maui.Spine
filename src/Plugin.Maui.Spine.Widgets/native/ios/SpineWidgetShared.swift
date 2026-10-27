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
            layout = (try? JSONDecoder().decode(JSONValue.self, from: Data(json.utf8))) ?? .null
        }

        private enum CodingKeys: String, CodingKey { case layout, json }

        public init(from decoder: Decoder) throws {
            let container = try decoder.container(keyedBy: CodingKeys.self)
            if let layout = try container.decodeIfPresent(JSONValue.self, forKey: .layout) {
                self.layout = layout
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
#endif

/// Any JSON, kept as it came: the layout travels through ActivityKit in this shape and is decoded by the renderer.
public enum JSONValue: Codable, Hashable {
    case null
    case bool(Bool)
    case number(Double)
    case string(String)
    case array([JSONValue])
    case object([String: JSONValue])

    public init(from decoder: Decoder) throws {
        let container = try decoder.singleValueContainer()
        if container.decodeNil() { self = .null }
        else if let value = try? container.decode(Bool.self) { self = .bool(value) }
        else if let value = try? container.decode(Double.self) { self = .number(value) }
        else if let value = try? container.decode(String.self) { self = .string(value) }
        else if let value = try? container.decode([JSONValue].self) { self = .array(value) }
        else { self = .object(try container.decode([String: JSONValue].self)) }
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.singleValueContainer()
        switch self {
        case .null: try container.encodeNil()
        case .bool(let value): try container.encode(value)
        case .number(let value): try container.encode(value)
        case .string(let value): try container.encode(value)
        case .array(let value): try container.encode(value)
        case .object(let value): try container.encode(value)
        }
    }
}
