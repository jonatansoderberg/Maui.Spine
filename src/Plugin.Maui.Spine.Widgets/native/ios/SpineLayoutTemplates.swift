import Foundation

/// Fills in a Live Activity template with a push's values: the same steps as LayoutTemplates in C#
/// (Plugin.Maui.Spine.Common), which the tests hold this to. A placeholder {"$":"key"} takes the value, or
/// leaves its property out when there is none; a "$if" node takes its branch; a "$each" among children repeats
/// its nodes for each item, the item's values before the outer ones; a "$" node is a whole node from the data.
/// The values come formatted by the app's server, so nothing here formats or computes.
enum LayoutTemplates {
    static func render(_ template: JSONValue, data: JSONValue) -> JSONValue {
        guard case .object(let values) = data, case .object(var layout)? = fill(template, [values]) else { return .null }
        if case .object(let shared)? = layout["shared"], shared.isEmpty { layout.removeValue(forKey: "shared") }
        return .object(layout)
    }

    static func fill(_ node: JSONValue, _ scopes: [[String: JSONValue]]) -> JSONValue? {
        switch node {
        case .object(let fields):
            if fields.count == 1, case .string(let key)? = fields["$"] { return lookup(key, scopes) }
            switch text(fields, "type") {
            case "$if":
                let branch = truthy(lookup(text(fields, "test") ?? "", scopes)) ? fields["then"] : fields["else"]
                return branch.flatMap { fill($0, scopes) }
            case "$":
                return lookup(text(fields, "key") ?? "", scopes)
            default:
                var filled: [String: JSONValue] = [:]
                for (name, value) in fields {
                    if let child = fill(value, scopes) { filled[name] = child }
                }
                return .object(filled)
            }
        case .array(let items):
            var filled: [JSONValue] = []
            for item in items {
                if case .object(let each) = item, text(each, "type") == "$each" {
                    if case .array(let list)? = lookup(text(each, "items") ?? "", scopes), !list.isEmpty {
                        for case .object(let entry) in list {
                            if let nodes = each["do"], case .array(let more)? = fill(nodes, [entry] + scopes) { filled += more }
                        }
                    } else if let nodes = each["empty"], case .array(let more)? = fill(nodes, scopes) {
                        filled += more
                    }
                    continue
                }
                if let child = fill(item, scopes) { filled.append(child) }
            }
            return .array(filled)
        default:
            return node
        }
    }

    private static func lookup(_ key: String, _ scopes: [[String: JSONValue]]) -> JSONValue? {
        for scope in scopes {
            if let value = scope[key] { return value == .null ? nil : value }
        }
        return nil
    }

    private static func truthy(_ value: JSONValue?) -> Bool {
        if case .bool(true)? = value { return true }
        return false
    }

    private static func text(_ fields: [String: JSONValue], _ key: String) -> String? {
        if case .string(let value)? = fields[key] { return value }
        return nil
    }
}

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
