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
    var remote: String?
    var refreshAfterSeconds: Double?
    var background: String?
    var backgroundGradient: GradientSpec?
    var backgroundImage: String?
    var entries: [Entry]
}

struct GradientSpec: Decodable {
    var colors: [String]
    var direction: String?
}

/// The widget's surface: a color or a gradient, and an image over it. The three travel together, so a remote
/// document's surface is taken whole or not at all.
struct SurfaceBackground {
    var color: String?
    var gradient: GradientSpec?
    var image: String?

    init?(_ document: TimelineDocument?) {
        guard let document, document.background != nil || document.backgroundGradient != nil || document.backgroundImage != nil
        else { return nil }
        color = document.background
        gradient = document.backgroundGradient
        image = document.backgroundImage
    }
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
    var background: String?
    var systemBackground: Bool?
    var actionColor: String?
    var link: String?

    static func parse(_ json: String) -> ActivityLayout {
        (try? Store.decoder.decode(ActivityLayout.self, from: Data(json.utf8))) ?? ActivityLayout()
    }
}

/// One node of the tree the app wrote; the same schema as Plugin.Maui.Spine.Widgets.WidgetNode.
/// A class because a node nests optional nodes (button child, adaptive fallback), which a struct cannot.
final class Node: Decodable {
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
    var compact: Bool?
    var spacing: Double?
    var padding: Double?
    var background: String?
    var cornerRadius: Double?
    var children: [Node]?
    var actionId: String?
    var child: Node?
    var fallback: Node?
    var trees: [String: Node]?
    var pending: Bool?
    var accented: Bool?
    var fullColor: Bool?
}

/// The kind of the widget or activity being drawn, so a button knows whose action it sends.
struct SpineKindKey: EnvironmentKey { static let defaultValue = "" }
/// Set inside a button's label, where invalidatable content must not be applied.
struct SpineInsideButtonKey: EnvironmentKey { static let defaultValue = false }
/// Set for the children of an HStack and a button, which wrap their content on Android; see BoxModifier.
struct SpineInlineKey: EnvironmentKey { static let defaultValue = false }
extension EnvironmentValues {
    var spineInline: Bool {
        get { self[SpineInlineKey.self] }
        set { self[SpineInlineKey.self] = newValue }
    }
    var spineKind: String {
        get { self[SpineKindKey.self] }
        set { self[SpineKindKey.self] = newValue }
    }
    var spineInsideButton: Bool {
        get { self[SpineInsideButtonKey.self] }
        set { self[SpineInsideButtonKey.self] = newValue }
    }
}

// MARK: - Rendering

struct NodeView: View {
    @Environment(\.widgetFamily) private var family
    @Environment(\.spineKind) private var kind
    @Environment(\.spineInsideButton) private var insideButton
    let node: Node

    // Marked nodes are dimmed by the system from a tap until the next reload; see W.Pending. The
    // modifier is applied only where asked: even invalidatableContent(false) on or inside a Button
    // stops WidgetKit from routing the tap to the intent, and it opens the app instead.
    var body: some View {
        if node.pending == true && !insideButton && node.type != "button" {
            content.invalidatableContent().widgetAccentable(node.accented == true)
        } else {
            content.widgetAccentable(node.accented == true)
        }
    }

    @ViewBuilder private var content: some View {
        switch node.type {
        case "adaptive":
            if let tree = node.trees?[Families.key(family)] ?? node.fallback { NodeView(node: tree) }
        case "button":
            if let child = node.child, let actionId = node.actionId {
                // Nothing inside the label may be invalidatable: WidgetKit then no longer routes the tap to
                // the intent, and it falls through to widgetURL and opens the app. Verified on iOS 26.
                Button(intent: SpineWidgetIntent(kind: kind, actionId: actionId)) {
                    NodeView(node: child).environment(\.spineInsideButton, true).environment(\.spineInline, true)
                }
                .buttonStyle(.plain)
            }
        case "vstack":
            VStack(alignment: .leading, spacing: node.spacing ?? 4) { children }.modifier(BoxModifier(node: node, alignment: .leading))
        case "hstack":
            HStack(spacing: node.spacing ?? 4) { children }.modifier(BoxModifier(node: node, alignment: .leading))
        case "zstack":
            ZStack { children }.modifier(BoxModifier(node: node, alignment: .center))
        case "text":
            Text(node.text ?? "").modifier(TextStyleModifier(node: node))
        // Self-updating text takes every point offered to it inside a Live Activity — a long-standing
        // SwiftUI bug that plain Text does not share, and the reason a Dynamic Island holding nothing
        // but a clock spans the screen. fixedSize was tried and made it worse: the text rendered as
        // nothing and the width did not change. Put a clock where there is room for one.
        // https://developer.apple.com/forums/thread/723316
        case "timer":
            if let end = node.until {
                Text(timerInterval: Date.now...max(end, Date.now), countsDown: true)
                    .monospacedDigit()
                    .modifier(TextStyleModifier(node: node))
            }
        case "relative":
            if let date = node.date {
                // .timer is the same information as a clock — "18:35" rather than "18 min, 35 secs"
                // — for the regions that have no room for a sentence.
                Text(date, style: node.compact == true ? .timer : .relative)
                    .monospacedDigit()
                    .modifier(TextStyleModifier(node: node))
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
                Palette.picture(image, fullColor: node.fullColor == true)
                    .scaledToFit().frame(height: node.height.map { CGFloat($0) })
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
        ForEach(Array((node.children ?? []).enumerated()), id: \.offset) {
            NodeView(node: $0.element).environment(\.spineInline, node.type == "hstack")
        }
    }
}

/// A stack's padding, background and corner radius. With a background the stack fills the width it is
/// offered, as every stack outside an HStack does on Android, so one tree lays out the same on both. A
/// stack with none of the three is left exactly as SwiftUI sizes it.
struct BoxModifier: ViewModifier {
    @Environment(\.spineInline) private var inline
    @Environment(\.widgetRenderingMode) private var renderingMode
    let node: Node
    let alignment: Alignment

    @ViewBuilder func body(content: Content) -> some View {
        if node.padding == nil && node.background == nil && node.cornerRadius == nil {
            content
        } else {
            let shape = RoundedRectangle(cornerRadius: CGFloat(node.cornerRadius ?? 0))
            content
                .padding(CGFloat(node.padding ?? 0))
                .frame(maxWidth: node.background != nil && !inline ? .infinity : nil, alignment: alignment)
                .background(fill, in: shape)
                .clipShape(shape)
        }
    }

    // In the Clear and Tinted appearances the system draws everything in one tint, so a box filled at full
    // strength swallows the text on it. A faint fill keeps the shape and leaves the text legible.
    private var fill: Color {
        guard let background = node.background else { return .clear }
        return Palette.color(background).opacity(renderingMode == .fullColor ? 1 : 0.25)
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
        case "surface": return Color(uiColor: .systemBackground)
        case "onAccent": return .white
        case "green": return .green
        case "red": return .red
        case "orange": return .orange
        case "yellow": return .yellow
        case "blue": return .blue
        default: return hex(value) ?? .primary
        }
    }

    /// A stored picture, resizable. With fullColor it keeps its own colors when iOS tints the widget; see
    /// W.FullColor. The modifier returns a view, so it goes after resizable and before any sizing.
    @ViewBuilder static func picture(_ image: UIImage, fullColor: Bool) -> some View {
        if fullColor {
            if #available(iOS 18.0, *) {
                Image(uiImage: image).resizable().widgetAccentedRenderingMode(.fullColor)
            } else {
                Image(uiImage: image).resizable()
            }
        } else {
            Image(uiImage: image).resizable()
        }
    }

    /// The Lock Screen tint: the layout's color; the system's own material (nil) when it asks for that; and
    /// otherwise the translucent black Spine has always drawn, so an activity that sets neither looks the same.
    static func lockScreenTint(_ layout: ActivityLayout) -> Color? {
        if let background = layout.background {
            if layout.systemBackground == true {
                NSLog("[SpineWidgets] Live Activity sets both background and systemBackground; background wins")
            }
            return color(background)
        }
        return layout.systemBackground == true ? nil : .black.opacity(0.6)
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
    var kind: String = ""
    var background: SurfaceBackground? = nil

    static let placeholder = Entry(date: .now, trees: [:], link: nil)
}

struct Provider: TimelineProvider {
    let kind: String

    func placeholder(in context: Context) -> Entry { .placeholder }

    func getSnapshot(in context: Context, completion: @escaping (Entry) -> Void) {
        completion(entries().first ?? .placeholder)
    }

    func getTimeline(in context: Context, completion: @escaping (Timeline<Entry>) -> Void) {
        let local = Store.timeline(kind: kind)
        guard let remote = local?.remote.flatMap(URL.init(string:)) else {
            completion(timeline(from: local, fallback: nil))
            return
        }
        // A remote source: the widget fetches its own content while the app sleeps, and the app's
        // entries stand in when the fetch fails. The pace is the document's refresh, 15 minutes by default.
        var request = URLRequest(url: remote)
        request.timeoutInterval = 15
        URLSession.shared.dataTask(with: request) { data, _, error in
            var fetched: TimelineDocument?
            if let data { fetched = try? Store.decoder.decode(TimelineDocument.self, from: data) }
            if fetched == nil { NSLog("[SpineWidgets] remote source for \(self.kind) failed: \(error?.localizedDescription ?? "not a timeline document")") }
            completion(self.timeline(from: fetched, fallback: local))
        }.resume()
    }

    private func timeline(from document: TimelineDocument?, fallback: TimelineDocument?) -> Timeline<Entry> {
        let source = document?.entries.isEmpty == false ? document : fallback
        let entries = entries(from: source, link: document?.link ?? fallback?.link, background: SurfaceBackground(document) ?? SurfaceBackground(fallback))
        let policy: TimelineReloadPolicy
        if let seconds = source?.refreshAfterSeconds ?? fallback?.refreshAfterSeconds, let last = entries.last {
            policy = .after(last.date.addingTimeInterval(seconds))
        } else if fallback != nil {
            policy = .after(Date.now.addingTimeInterval(15 * 60))
        } else {
            policy = .atEnd
        }
        return Timeline(entries: entries.isEmpty ? [.placeholder] : entries, policy: policy)
    }

    private func entries() -> [Entry] {
        let document = Store.timeline(kind: kind)
        return entries(from: document, link: document?.link, background: SurfaceBackground(document))
    }

    private func entries(from document: TimelineDocument?, link: String?, background: SurfaceBackground?) -> [Entry] {
        guard let document else { return [] }
        return document.entries
            .sorted { $0.date < $1.date }
            .map { Entry(date: $0.date, trees: $0.trees, link: link, kind: kind, background: background) }
    }
}

/// The widget's surface. A clear color gets WidgetKit's own opaque background, not the wallpaper; neither a
/// material nor glassEffect changes that on iOS 26. Glass is the system's: the Clear and Tinted appearances
/// remove this view and draw it themselves.
struct SurfaceView: View {
    let background: SurfaceBackground?

    var body: some View {
        ZStack {
            base
            if let asset = background?.image, let image = Store.image(asset: asset) {
                Image(uiImage: image).resizable().scaledToFill()
            }
        }
    }

    @ViewBuilder private var base: some View {
        if let gradient = background?.gradient, gradient.colors.count > 1 {
            let (start, end) = Self.points(gradient.direction)
            LinearGradient(colors: gradient.colors.map { Palette.color($0) }, startPoint: start, endPoint: end)
        } else if let color = background?.color {
            Palette.color(color)
        } else {
            Rectangle().fill(.background)
        }
    }

    private static func points(_ direction: String?) -> (UnitPoint, UnitPoint) {
        switch direction?.lowercased() {
        case "horizontal": (.leading, .trailing)
        case "diagonal": (.topLeading, .bottomTrailing)
        default: (.top, .bottom)
        }
    }
}

struct SpineWidgetView: View {
    @Environment(\.widgetFamily) private var family
    let entry: Entry

    var body: some View {
        content
            .containerBackground(for: .widget) { SurfaceView(background: entry.background) }
            .widgetURL(entry.link.flatMap(URL.init(string:)))
    }

    @ViewBuilder private var content: some View {
        if let tree = entry.trees[Families.key(family)] ?? entry.trees["default"] {
            NodeView(node: tree).environment(\.spineKind, entry.kind)
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
    var kind: String = ""
    var body: some View {
        if let node { NodeView(node: node).environment(\.spineKind, kind) } else { EmptyView() }
    }
}

struct SpineLiveActivity: Widget {
    var body: some WidgetConfiguration {
        ActivityConfiguration(for: SpineActivityAttributes.self) { context in
            let layout = ActivityLayout.parse(context.state.json)
            Slot(node: layout.lockScreen, kind: context.attributes.kind)
                .padding()
                .opacity(context.isStale ? 0.5 : 1)
                .activityBackgroundTint(Palette.lockScreenTint(layout))
                .activitySystemActionForegroundColor(layout.actionColor.map { Palette.color($0) })
                .widgetURL(layout.link.flatMap(URL.init(string:)))
        } dynamicIsland: { context in
            let layout = ActivityLayout.parse(context.state.json)
            let kind = context.attributes.kind
            return DynamicIsland {
                DynamicIslandExpandedRegion(.leading) { Slot(node: layout.expandedLeading, kind: kind) }
                DynamicIslandExpandedRegion(.trailing) { Slot(node: layout.expandedTrailing, kind: kind) }
                DynamicIslandExpandedRegion(.center) { Slot(node: layout.expandedCenter, kind: kind) }
                DynamicIslandExpandedRegion(.bottom) { Slot(node: layout.expandedBottom, kind: kind) }
            } compactLeading: {
                Slot(node: layout.compactLeading, kind: kind)
            } compactTrailing: {
                Slot(node: layout.compactTrailing, kind: kind)
            } minimal: {
                Slot(node: layout.minimal, kind: kind)
            }
            .widgetURL(layout.link.flatMap(URL.init(string:)))
        }
    }
}
