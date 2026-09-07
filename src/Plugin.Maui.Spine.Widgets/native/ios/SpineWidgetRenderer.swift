import WidgetKit
import SwiftUI
import ActivityKit

// MARK: - Manifest (written by the build from the <SpineWidget> items)

struct Manifest: Decodable {
    struct Entry: Decodable {
        var kind: String
        var displayName: String
        var description: String
        var families: [String]
    }
    var appGroup: String
    var widgets: [Entry]

    static let current: Manifest = {
        guard let url = Bundle.main.url(forResource: "spine-widgets", withExtension: "json"),
              let data = try? Data(contentsOf: url),
              let manifest = try? JSONDecoder().decode(Manifest.self, from: data)
        else { return Manifest(appGroup: "", widgets: []) }
        return manifest
    }()
}

// MARK: - Storage shared with the app

enum Store {
    static var root: URL? {
        FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: Manifest.current.appGroup)?
            .appendingPathComponent("spine-widgets")
    }

    static func timeline(kind: String) -> TimelineDocument? {
        guard let url = root?.appendingPathComponent("\(kind).json"),
              let data = try? Data(contentsOf: url) else { return nil }
        do { return try decoder.decode(TimelineDocument.self, from: data) }
        catch { NSLog("[SpineWidgets] \(kind).json failed to decode: \(error)"); return nil }
    }

    static func image(asset: String) -> UIImage? {
        guard let url = root?.appendingPathComponent("assets").appendingPathComponent(asset) else { return nil }
        return UIImage(contentsOfFile: url.path)
    }

    static let decoder: JSONDecoder = {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .custom { decoder in
            let value = try decoder.singleValueContainer().decode(String.self)
            if let date = Dates.parse(value) { return date }
            throw DecodingError.dataCorrupted(.init(codingPath: decoder.codingPath, debugDescription: "Not an ISO 8601 date: \(value)"))
        }
        return decoder
    }()
}

enum Dates {
    private static let fractional: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter(); f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]; return f
    }()
    private static let whole: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter(); f.formatOptions = [.withInternetDateTime]; return f
    }()

    // System.Text.Json writes fractional seconds only when the value has them.
    static func parse(_ value: String) -> Date? { fractional.date(from: value) ?? whole.date(from: value) }
}

// MARK: - Documents

struct TimelineDocument: Decodable {
    struct Entry: Decodable {
        var date: Date
        var trees: [String: Node]
    }
    var link: String?
    var refreshAfterSeconds: Double?
    var entries: [Entry]
}

struct ActivityLayout: Decodable {
    var lockScreen: Node?
    var expandedLeading: Node?
    var expandedTrailing: Node?
    var expandedCenter: Node?
    var expandedBottom: Node?
    var compactLeading: Node?
    var compactTrailing: Node?
    var minimal: Node?
    var link: String?

    static func parse(_ json: String) -> ActivityLayout {
        (try? Store.decoder.decode(ActivityLayout.self, from: Data(json.utf8))) ?? ActivityLayout()
    }
}

/// One node of the tree the app wrote; the same schema as Plugin.Maui.Spine.Widgets.WidgetNode.
struct Node: Decodable {
    var type: String
    var text: String?
    var font: String?
    var bold: Bool?
    var color: String?
    var until: Date?
    var date: Date?
    var systemImage: String?
    var asset: String?
    var height: Double?
    var value: Double?
    var spacing: Double?
    var children: [Node]?
}

// MARK: - Rendering

struct NodeView: View {
    let node: Node

    var body: some View {
        switch node.type {
        case "vstack":
            VStack(alignment: .leading, spacing: node.spacing ?? 4) { children }
        case "hstack":
            HStack(spacing: node.spacing ?? 4) { children }
        case "zstack":
            ZStack { children }
        case "text":
            Text(node.text ?? "").modifier(TextStyleModifier(node: node))
        case "timer":
            if let end = node.until {
                Text(timerInterval: Date.now...max(end, Date.now), countsDown: true)
                    .monospacedDigit()
                    .modifier(TextStyleModifier(node: node))
            }
        case "relative":
            if let date = node.date {
                Text(date, style: .relative).modifier(TextStyleModifier(node: node))
            }
        case "image":
            // The app rasterized the icon from an SVG as a white mask; an SF Symbol is the fallback.
            if let name = node.systemImage, let mask = Store.image(asset: "icons/\(name).png") {
                Image(uiImage: mask).renderingMode(.template).resizable().scaledToFit()
                    .frame(width: 18, height: 18)
                    .foregroundStyle(Palette.color(node.color))
            } else {
                Image(systemName: node.systemImage ?? "questionmark").foregroundStyle(Palette.color(node.color))
            }
        case "asset":
            if let asset = node.asset, let image = Store.image(asset: asset) {
                Image(uiImage: image).resizable().scaledToFit().frame(height: node.height.map { CGFloat($0) })
            }
        case "progress":
            ProgressView(value: min(max(node.value ?? 0, 0), 1)).tint(Palette.color(node.color))
        case "spacer":
            Spacer(minLength: 0)
        case "divider":
            Divider()
        default:
            EmptyView()
        }
    }

    @ViewBuilder private var children: some View {
        ForEach(Array((node.children ?? []).enumerated()), id: \.offset) { NodeView(node: $0.element) }
    }
}

struct TextStyleModifier: ViewModifier {
    let node: Node

    func body(content: Content) -> some View {
        content
            .font(Palette.font(node.font))
            .fontWeight(node.bold == true ? .bold : .regular)
            .foregroundStyle(Palette.color(node.color))
    }
}

enum Palette {
    static func font(_ role: String?) -> Font {
        switch role?.lowercased() {
        case "title": .title2
        case "headline": .headline
        case "caption": .caption
        default: .body
        }
    }

    static func color(_ value: String?) -> Color {
        guard let value else { return .primary }
        switch value {
        case "primary": return .primary
        case "secondary": return .secondary
        case "accent": return .accentColor
        case "green": return .green
        case "red": return .red
        case "orange": return .orange
        case "yellow": return .yellow
        case "blue": return .blue
        default: return hex(value) ?? .primary
        }
    }

    private static func hex(_ value: String) -> Color? {
        let digits = value.hasPrefix("#") ? String(value.dropFirst()) : value
        guard digits.count == 6 || digits.count == 8, let number = UInt64(digits, radix: 16) else { return nil }
        let a = digits.count == 8 ? Double((number >> 24) & 0xFF) / 255 : 1
        let r = Double((number >> 16) & 0xFF) / 255
        let g = Double((number >> 8) & 0xFF) / 255
        let b = Double(number & 0xFF) / 255
        return Color(.sRGB, red: r, green: g, blue: b, opacity: a)
    }
}

// MARK: - Home-screen and Lock Screen widgets

struct Entry: TimelineEntry {
    let date: Date
    let trees: [String: Node]
    let link: String?

    static let placeholder = Entry(date: .now, trees: [:], link: nil)
}

struct Provider: TimelineProvider {
    let kind: String

    func placeholder(in context: Context) -> Entry { .placeholder }

    func getSnapshot(in context: Context, completion: @escaping (Entry) -> Void) {
        completion(entries().first ?? .placeholder)
    }

    func getTimeline(in context: Context, completion: @escaping (Timeline<Entry>) -> Void) {
        let document = Store.timeline(kind: kind)
        let entries = entries(from: document)
        let policy: TimelineReloadPolicy
        if let seconds = document?.refreshAfterSeconds, let last = entries.last {
            policy = .after(last.date.addingTimeInterval(seconds))
        } else {
            policy = .atEnd
        }
        completion(Timeline(entries: entries.isEmpty ? [.placeholder] : entries, policy: policy))
    }

    private func entries() -> [Entry] { entries(from: Store.timeline(kind: kind)) }

    private func entries(from document: TimelineDocument?) -> [Entry] {
        guard let document else { return [] }
        return document.entries
            .sorted { $0.date < $1.date }
            .map { Entry(date: $0.date, trees: $0.trees, link: document.link) }
    }
}

struct SpineWidgetView: View {
    @Environment(\.widgetFamily) private var family
    let entry: Entry

    var body: some View {
        content
            .containerBackground(.background, for: .widget)
            .widgetURL(entry.link.flatMap(URL.init(string:)))
    }

    @ViewBuilder private var content: some View {
        if let tree = entry.trees[Families.key(family)] ?? entry.trees["default"] {
            NodeView(node: tree)
        } else {
            Text("—").foregroundStyle(.secondary)
        }
    }
}

enum Families {
    static func key(_ family: WidgetFamily) -> String {
        switch family {
        case .systemSmall: "small"
        case .systemMedium: "medium"
        case .systemLarge: "large"
        case .systemExtraLarge: "extraLarge"
        case .accessoryCircular: "accessoryCircular"
        case .accessoryRectangular: "accessoryRectangular"
        case .accessoryInline: "accessoryInline"
        @unknown default: "default"
        }
    }

    static func family(_ key: String) -> WidgetFamily? {
        switch key.lowercased() {
        case "small": .systemSmall
        case "medium": .systemMedium
        case "large": .systemLarge
        case "extralarge": .systemExtraLarge
        case "accessorycircular": .accessoryCircular
        case "accessoryrectangular": .accessoryRectangular
        case "accessoryinline": .accessoryInline
        default: nil
        }
    }
}

/// The configuration for the widget at `index` in the manifest; the generated bundle wraps one per widget.
func spineWidgetConfiguration(index: Int) -> some WidgetConfiguration {
    let entry = Manifest.current.widgets.indices.contains(index)
        ? Manifest.current.widgets[index]
        : Manifest.Entry(kind: "spine.widget.\(index)", displayName: "Widget", description: "", families: ["small"])
    let families = entry.families.compactMap(Families.family)
    return StaticConfiguration(kind: entry.kind, provider: Provider(kind: entry.kind)) { SpineWidgetView(entry: $0) }
        .configurationDisplayName(entry.displayName)
        .description(entry.description)
        .supportedFamilies(families.isEmpty ? [.systemSmall] : families)
}

// MARK: - Live Activity

struct Slot: View {
    let node: Node?
    var body: some View {
        if let node { NodeView(node: node) } else { EmptyView() }
    }
}

struct SpineLiveActivity: Widget {
    var body: some WidgetConfiguration {
        ActivityConfiguration(for: SpineActivityAttributes.self) { context in
            let layout = ActivityLayout.parse(context.state.json)
            Slot(node: layout.lockScreen)
                .padding()
                .opacity(context.isStale ? 0.5 : 1)
                .activityBackgroundTint(.black.opacity(0.6))
                .widgetURL(layout.link.flatMap(URL.init(string:)))
        } dynamicIsland: { context in
            let layout = ActivityLayout.parse(context.state.json)
            return DynamicIsland {
                DynamicIslandExpandedRegion(.leading) { Slot(node: layout.expandedLeading) }
                DynamicIslandExpandedRegion(.trailing) { Slot(node: layout.expandedTrailing) }
                DynamicIslandExpandedRegion(.center) { Slot(node: layout.expandedCenter) }
                DynamicIslandExpandedRegion(.bottom) { Slot(node: layout.expandedBottom) }
            } compactLeading: {
                Slot(node: layout.compactLeading)
            } compactTrailing: {
                Slot(node: layout.compactTrailing)
            } minimal: {
                Slot(node: layout.minimal)
            }
            .widgetURL(layout.link.flatMap(URL.init(string:)))
        }
    }
}
