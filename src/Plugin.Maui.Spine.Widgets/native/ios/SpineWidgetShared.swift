import ActivityKit

// Compiled into both the widget extension and the bridge framework. ActivityKit matches the
// attributes type by name across the two processes, so this file must be identical in both —
// which the build guarantees by compiling the same source twice.
public struct SpineActivityAttributes: ActivityAttributes {
    public struct ContentState: Codable, Hashable {
        public var json: String
        public init(json: String) { self.json = json }
    }

    public var kind: String
    public init(kind: String) { self.kind = kind }
}
