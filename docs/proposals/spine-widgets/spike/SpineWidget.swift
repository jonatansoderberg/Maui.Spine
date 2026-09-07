import WidgetKit
import SwiftUI
import ActivityKit

// Spike: a generic, data-driven widget. The host app writes a JSON view tree to the
// App Group container; this extension interprets it with SwiftUI. Nothing app-specific here.

let appGroup = "group.com.companyname.mauibottomsheetpoc"
let widgetKind = "spine.sample"

struct Node: Decodable {
    var type: String
    var text: String?
    var systemImage: String?
    var value: Double?
    var font: String?
    var color: String?
    var bold: Bool?
    var spacing: Double?
    var until: String?
    var children: [Node]?
}

struct Entry: TimelineEntry {
    let date: Date
    let root: Node?
    let source: String
}

func loadTree() -> (Node?, String) {
    guard let url = FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: appGroup) else {
        return (nil, "no app group container")
    }
    let file = url.appendingPathComponent("spine-widgets/\(widgetKind).json")
    guard let data = try? Data(contentsOf: file) else { return (nil, "no data at \(file.lastPathComponent)") }
    do { return (try JSONDecoder().decode(Node.self, from: data), "app group") }
    catch { return (nil, "decode error: \(error)") }
}

struct Provider: TimelineProvider {
    func placeholder(in context: Context) -> Entry { Entry(date: .now, root: nil, source: "placeholder") }
    func getSnapshot(in context: Context, completion: @escaping (Entry) -> Void) {
        let (root, source) = loadTree()
        completion(Entry(date: .now, root: root, source: source))
    }
    func getTimeline(in context: Context, completion: @escaping (Timeline<Entry>) -> Void) {
        let (root, source) = loadTree()
        completion(Timeline(entries: [Entry(date: .now, root: root, source: source)], policy: .never))
    }
}

struct NodeView: View {
    let node: Node
    var body: some View {
        switch node.type {
        case "vstack":
            VStack(alignment: .leading, spacing: node.spacing ?? 4) { children }
        case "hstack":
            HStack(spacing: node.spacing ?? 4) { children }
        case "spacer":
            Spacer()
        case "text":
            Text(node.text ?? "")
                .font(font)
                .fontWeight(node.bold == true ? .bold : .regular)
                .foregroundStyle(color)
        case "image":
            Image(systemName: node.systemImage ?? "questionmark")
                .foregroundStyle(color)
        case "progress":
            ProgressView(value: node.value ?? 0).tint(color)
        case "timer":
            if let s = node.until, let end = ISO8601DateFormatter().date(from: s) {
                Text(timerInterval: Date.now...max(end, Date.now), countsDown: true)
                    .font(font).fontWeight(node.bold == true ? .bold : .regular).foregroundStyle(color).monospacedDigit()
            } else { Text("--:--") }
        default:
            Text("?\(node.type)")
        }
    }
    @ViewBuilder var children: some View {
        ForEach(Array((node.children ?? []).enumerated()), id: \.offset) { NodeView(node: $0.element) }
    }
    var font: Font {
        switch node.font {
        case "title": .title2
        case "headline": .headline
        case "caption": .caption
        default: .body
        }
    }
    var color: Color {
        switch node.color {
        case "accent": .accentColor
        case "secondary": .secondary
        case "green": .green
        case "red": .red
        default: .primary
        }
    }
}

struct SpineWidgetView: View {
    let entry: Entry
    var body: some View {
        if let root = entry.root {
            NodeView(node: root).containerBackground(.background, for: .widget)
        } else {
            VStack(alignment: .leading) {
                Text("Spine widget").font(.headline)
                Text(entry.source).font(.caption).foregroundStyle(.secondary)
            }.containerBackground(.background, for: .widget)
        }
    }
}

struct SpineWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: widgetKind, provider: Provider()) { SpineWidgetView(entry: $0) }
            .configurationDisplayName("Spine sample")
            .description("Data-driven widget rendered from JSON written by the .NET app.")
            .supportedFamilies([.systemSmall, .systemMedium])
    }
}

// MARK: - Live Activity (generic, data-driven)

struct SpineActivityAttributes: ActivityAttributes {
    struct ContentState: Codable, Hashable { var json: String }
    var kind: String
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

    static func parse(_ json: String) -> ActivityLayout {
        (try? JSONDecoder().decode(ActivityLayout.self, from: Data(json.utf8))) ?? ActivityLayout()
    }
}

struct Slot: View {
    let node: Node?
    var body: some View {
        if let node { NodeView(node: node) } else { EmptyView() }
    }
}

struct SpineLiveActivity: Widget {
    var body: some WidgetConfiguration {
        ActivityConfiguration(for: SpineActivityAttributes.self) { context in
            let l = ActivityLayout.parse(context.state.json)
            Slot(node: l.lockScreen).padding().activityBackgroundTint(.black.opacity(0.6))
        } dynamicIsland: { context in
            let l = ActivityLayout.parse(context.state.json)
            return DynamicIsland {
                DynamicIslandExpandedRegion(.leading) { Slot(node: l.expandedLeading) }
                DynamicIslandExpandedRegion(.trailing) { Slot(node: l.expandedTrailing) }
                DynamicIslandExpandedRegion(.center) { Slot(node: l.expandedCenter) }
                DynamicIslandExpandedRegion(.bottom) { Slot(node: l.expandedBottom) }
            } compactLeading: {
                Slot(node: l.compactLeading)
            } compactTrailing: {
                Slot(node: l.compactTrailing)
            } minimal: {
                Slot(node: l.minimal)
            }
        }
    }
}

@main
struct SpineWidgetBundle: WidgetBundle {
    var body: some Widget {
        SpineWidget()
        SpineLiveActivity()
    }
}
