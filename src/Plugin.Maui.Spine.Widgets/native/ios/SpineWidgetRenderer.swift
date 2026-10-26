import WidgetKit
import SwiftUI
#if !targetEnvironment(macCatalyst)
import ActivityKit
#endif

// MARK: - Manifest (written by the build from the <SpineWidget> items)

struct Manifest: Decodable {
    struct Entry: Decodable {
        var kind: String
        var displayName: String
        var description: String
        var families: [String]
    }
    /// A <SpineControl> item; `type` is "toggle" or "button".
    struct Control: Decodable {
        var kind: String
        var displayName: String
        var description: String
        var type: String
        var icon: String?
    }
    var appGroup: String
    var widgets: [Entry]
    var controls: [Control]?

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

struct TimelineDocument: Decodable, SurfaceFields {
    struct Entry: Decodable, SurfaceFields {
        var date: Date
        var trees: [String: Node]
        var background: String?
        var backgroundGradient: GradientSpec?
        var backgroundImage: String?
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

/// The three surface fields, which a document and each of its entries carry under the same names.
protocol SurfaceFields {
    var background: String? { get }
    var backgroundGradient: GradientSpec? { get }
    var backgroundImage: String? { get }
}

/// The widget's surface: a color or a gradient, and an image over it. The three travel together, so a remote
/// document's surface, and an entry's, is taken whole or not at all.
struct SurfaceBackground {
    var color: String?
    var gradient: GradientSpec?
    var image: String?

    init?<Fields: SurfaceFields>(_ fields: Fields?) {
        guard let fields, fields.background != nil || fields.backgroundGradient != nil || fields.backgroundImage != nil
        else { return nil }
        color = fields.background
        gradient = fields.backgroundGradient
        image = fields.backgroundImage
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
    var small: Node?
    var background: String?
    var systemBackground: Bool?
    var actionColor: String?
    var link: String?

    static func parse(_ layout: JSONValue) -> ActivityLayout {
        guard let data = try? JSONEncoder().encode(layout) else { return ActivityLayout() }
        return (try? Store.decoder.decode(ActivityLayout.self, from: data)) ?? ActivityLayout()
    }
}

/// One node of the tree the app wrote; the same schema as Plugin.Maui.Spine.Widgets.WidgetNode.
/// A class because a node nests optional nodes (button child, adaptive fallback), which a struct cannot.
final class Node: Decodable {
    var type: String
    var text: String?
    var font: String?
    var design: String?
    var size: Double?
    var bold: Bool?
    var color: String?
    var until: Date?
    var date: Date?
    var systemImage: String?
    var asset: String?
    var height: Double?
    var value: Double?
    var compact: Bool?
    var rounded: Bool?
    var ago: Bool?
    var prefix: String?
    var centered: Bool?
    var trailing: Bool?
    var singleLine: Double?
    var spacing: Double?
    var fill: Bool?
    var align: String?
    var distribute: String?
    var thickness: Double?
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
    var values: [Double?]?
    var from: Date?
    var to: Date?
    var step: Double?
    var low: Double?
    var high: Double?
    var min: Double?
    var max: Double?
    var aboveColor: String?
    var belowColor: String?
    var shade: Bool?
    var area: Bool?
    var dot: Bool?
}

/// The kind of the widget or activity being drawn, so a button knows whose action it sends.
struct SpineKindKey: EnvironmentKey { static let defaultValue = "" }
/// Set inside a button's label, where invalidatable content must not be applied.
struct SpineInsideButtonKey: EnvironmentKey { static let defaultValue = false }
/// Set for the children of an HStack and a button, which wrap their content on Android; see BoxModifier.
struct SpineInlineKey: EnvironmentKey { static let defaultValue = false }
/// The solid colour behind a Live Activity, where it is known: what a sparkline's area is blended onto, so it is drawn
/// opaque. Nil on widgets and on a translucent or system background.
struct SpineBackdropKey: EnvironmentKey { static let defaultValue: Color? = nil }
/// Set where the region clips at its edges, as the Dynamic Island's do: nothing may reach over them there.
struct SpineClippedKey: EnvironmentKey { static let defaultValue = false }
extension EnvironmentValues {
    var spineClipped: Bool {
        get { self[SpineClippedKey.self] }
        set { self[SpineClippedKey.self] = newValue }
    }
    var spineBackdrop: Color? {
        get { self[SpineBackdropKey.self] }
        set { self[SpineBackdropKey.self] = newValue }
    }
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
            let center = node.align == "center"
            VStack(alignment: center ? .center : .leading, spacing: node.spacing ?? 4) { children }
                .modifier(BoxModifier(node: node, alignment: center ? .center : .leading))
        case "hstack" where node.distribute == "evenly":
            // The same space at the ends as between the children, which keep their own width.
            HStack(spacing: 0) {
                Spacer(minLength: 0)
                ForEach(Array((node.children ?? []).enumerated()), id: \.offset) {
                    NodeView(node: $0.element).environment(\.spineInline, true)
                    Spacer(minLength: 0)
                }
            }.modifier(BoxModifier(node: node, alignment: .leading))
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
                prefixed(node, Text(timerInterval: Date.now...max(end, Date.now), countsDown: true))
                    .monospacedDigit()
                    .modifier(TextStyleModifier(node: node))
            }
        case "relative":
            if let date = node.date {
                prefixed(node, age(since: date))
                    .monospacedDigit()
                    .modifier(TextStyleModifier(node: node))
            }
        case "image":
            // The app rasterized the icon from an SVG as a white mask; an SF Symbol is the fallback.
            if let name = node.systemImage, let mask = Store.image(asset: "icons/\(name).png") {
                Image(uiImage: mask).renderingMode(.template).resizable().scaledToFit()
                    .frame(width: CGFloat(node.height ?? 18), height: CGFloat(node.height ?? 18))
                    .foregroundStyle(Palette.color(node.color))
            } else if let size = node.height {
                Image(systemName: node.systemImage ?? "questionmark").font(.system(size: CGFloat(size) * 0.8))
                    .frame(width: CGFloat(size), height: CGFloat(size))
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
        case "ring":
            let size = CGFloat(node.height ?? 56)
            let line = CGFloat(node.thickness ?? max(3, Double(size) / 10))
            let tint = node.color == nil ? Color.accentColor : Palette.color(node.color)
            ZStack {
                Circle().stroke(tint.opacity(0.22), lineWidth: line)
                if let value = node.value, value > 0 {
                    Circle().trim(from: 0, to: min(value, 1))
                        .stroke(tint, style: StrokeStyle(lineWidth: line, lineCap: .round))
                        .rotationEffect(.degrees(-90))
                }
            }
            .padding(line / 2)
            .frame(width: size, height: size)
        case "sparkline":
            SparklineView(node: node).frame(maxWidth: .infinity).frame(height: CGFloat(node.height ?? 48))
        case "spacer":
            Spacer(minLength: 0)
        case "divider":
            Divider()
        default:
            EmptyView()
        }
    }

    /// The age of `date`, redrawn by the system. Ago on iOS 18: "now" for the first minute, then "2 minutes ago", the only
    /// live format that has a word for under a minute. Rounded on iOS 18: whole minutes, then hours and minutes, which iOS
    /// only draws with the units' full names ("1 hour, 5 minutes"). Compact: .timer, the same information as a clock,
    /// "18:35" rather than "18 min, 35 secs", for the regions that have no room for a sentence.
    private func age(since date: Date) -> Text {
        if node.ago == true, #available(iOS 18.0, *) {
            return Text(.currentDate, format: .reference(to: date, allowedFields: [.day, .hour, .minute], thresholdField: .year)
                .locale(AppLanguage.locale))
        }
        if node.rounded == true, #available(iOS 18.0, *) {
            return Text(.currentDate, format: .offset(to: date, allowedFields: [.hour, .minute], maxFieldCount: 2, sign: .never)
                .locale(AppLanguage.locale))
        }
        return Text(date, style: node.compact == true ? .timer : .relative)
    }

    @ViewBuilder private var children: some View {
        ForEach(Array((node.children ?? []).enumerated()), id: \.offset) {
            NodeView(node: $0.element).environment(\.spineInline, node.type == "hstack")
        }
    }
}

/// A line through a sparkline node's values: smooth without overshooting a point (monotone cubic), split where it
/// crosses the band's edges so each stretch takes its colour with a cut square to the line, a faint band, a fading
/// area and a dot on the last value when asked. RemoteViewsRenderer.Sparkline draws the same on Android.
struct SparklineView: View {
    @Environment(\.displayScale) private var displayScale
    @Environment(\.spineBackdrop) private var backdrop
    @Environment(\.spineClipped) private var clipped
    let node: Node

    var body: some View {
        GeometryReader { geometry in
            let plot = SparklinePlot(node: node, size: geometry.size, clipped: clipped)
            ZStack {
                if let band = plot.band {
                    Path(band).fill(plot.inRange.opacity(0.14))
                }
                if node.area == true, let area = SparklineArea.image(plot, size: geometry.size, scale: displayScale, backdrop: backdrop, island: clipped) {
                    Image(uiImage: area).resizable()
                        .frame(width: geometry.size.width + SparklineArea.edge, height: geometry.size.height)
                        .position(x: (geometry.size.width + SparklineArea.edge) / 2, y: geometry.size.height / 2)
                }
                // Butt ends where one colour meets the next, so the cut is square to the line; round ends only
                // where the line itself starts and stops.
                ForEach(plot.runs.indices, id: \.self) { i in
                    plot.runs[i].path.stroke(plot.runs[i].color, style: StrokeStyle(lineWidth: plot.lineWidth, lineCap: .butt, lineJoin: .round))
                }
                ForEach(plot.ends.indices, id: \.self) { i in
                    Circle().fill(plot.ends[i].color).frame(width: plot.lineWidth, height: plot.lineWidth).position(plot.ends[i].point)
                }
                if node.dot == true, let last = plot.last {
                    Circle().fill(last.color.opacity(0.4)).frame(width: plot.halo * 2, height: plot.halo * 2).position(last.point)
                    Circle().fill(last.color).frame(width: plot.dot * 2, height: plot.dot * 2).position(last.point)
                }
            }
        }
    }
}

/// The area under a sparkline, drawn by the extension into a picture: from the line down in the line's colour, strongest
/// at the line's highest level and fading to nothing at the bottom, as one gradient across the graph. At the line's two ends
/// it fades in over `fade` points, reaching `edge` points past them; the picture is `edge` wider than the graph, reaching
/// over its trailing side as the last value's dot does. Where the line changes colour, the area's colour runs from one to
/// the other over a few points either side.
///
/// The iPhone's Lock Screen (iOS 27) draws nothing of a gradient mask, nor of a gradient whose colours are all translucent,
/// and thins out the translucent parts of a picture until only a sliver under the line is left; the Dynamic Island and the
/// simulator draw all of them. Where the colour behind the activity is known (`spineBackdrop`), the area is therefore blended
/// onto it and drawn opaque; elsewhere it is translucent. The lock screen still darkens the dim shades of that picture,
/// showing a share k of the line's colour as about k^1.6 (measured on an iPhone with iOS 27), and the island does not, so in
/// the island the area takes that curve itself and the two look alike.
struct SparklineArea: View {
    /// How far the area reaches past the line's two ends.
    static let edge: CGFloat = 2
    /// How wide the fade at each end is, mostly under the line.
    static let fade: CGFloat = 4
    /// How strong the area is at the line's highest level.
    static let strength: CGFloat = 0.5
    /// How the lock screen darkens the area's shades, which the island draws as they are.
    static let lockScreenTone = 1.6
    let plot: SparklinePlot
    let size: CGSize
    let scale: CGFloat
    let backdrop: Color?
    let island: Bool

    var body: some View {
        Canvas { context, canvas in
            let all = plot.samples.flatMap { $0 }
            guard let first = all.first, let last = all.last, last.point.x > first.point.x,
                  let top = all.map(\.point.y).min(), size.height > top else { return }
            let edge = Self.edge
            func eased(_ t: CGFloat) -> CGFloat { let t = min(max(t, 0), 1); return t * t * (3 - 2 * t) }
            func ends(_ x: CGFloat) -> CGFloat {
                eased((x - first.point.x + edge) / Self.fade) * eased((last.point.x + edge - x) / Self.fade)
            }
            func column(_ x: CGFloat) -> (y: CGFloat, color: Color, ends: CGFloat)? {
                if x < first.point.x { return (first.point.y, first.color, ends(x)) }
                if x > last.point.x { return (last.point.y, last.color, ends(x)) }
                for segment in plot.samples where segment.count > 1 {
                    guard x >= segment[0].point.x, x <= segment[segment.count - 1].point.x,
                          let i = segment.lastIndex(where: { $0.point.x <= x }) else { continue }
                    let (a, b) = (segment[i], segment[min(i + 1, segment.count - 1)])
                    let t = b.point.x > a.point.x ? (x - a.point.x) / (b.point.x - a.point.x) : 0
                    return (a.point.y + (b.point.y - a.point.y) * t, a.color, ends(x))
                }
                return nil
            }
            func rgb(_ color: Color) -> SIMD3<Double> {
                var (r, g, b, a): (CGFloat, CGFloat, CGFloat, CGFloat) = (0, 0, 0, 0)
                UIColor(color).getRed(&r, green: &g, blue: &b, alpha: &a)
                return SIMD3(Double(r), Double(g), Double(b))
            }
            // Where the line changes colour the area's colour runs from one to the other over 10 points either side,
            // weighted towards the middle so the change has no visible steps.
            var tints: [Int: SIMD3<Double>] = [:]
            func tint(_ x: CGFloat) -> SIMD3<Double> {
                var (sum, weights) = (SIMD3<Double>(), 0.0)
                for d in stride(from: -10.0, through: 10.0, by: 0.5) {
                    let at = min(max(x + CGFloat(d), first.point.x), last.point.x)
                    let key = Int((at * 2).rounded())
                    let value = tints[key] ?? column(at).map { rgb($0.color) }
                    guard let value else { continue }
                    tints[key] = value
                    let w = 10.5 - abs(d)
                    (sum, weights) = (sum + value * w, weights + w)
                }
                return weights > 0 ? sum / weights : SIMD3<Double>()
            }
            let behind = backdrop.map(rgb)
            let step = 1 / scale
            var x = ((first.point.x - edge) * scale).rounded(.down) / scale
            while x < min(last.point.x + edge, canvas.width) {
                if let (y, _, ends) = column(x), ends > 0 {
                    // The one gradient across the graph, from the line's highest level to the bottom, met where the line is.
                    // It darkens slowly at first: the iPhone's Lock Screen turns the darkest shades black, which cut a
                    // straight fade short a little way under the line.
                    let c = tint(x)
                    let stops = stride(from: 0.0, through: 1.0, by: 0.125).map { f -> Gradient.Stop in
                        let below = Double(size.height - y) * (1 - f)
                        let share = Double(Self.strength * ends) * pow(below / Double(size.height - top), 0.4)
                        let k = island ? pow(share, Self.lockScreenTone) : share
                        let color = behind.map { b in
                            let m = b + (c - b) * k
                            return Color(red: m.x, green: m.y, blue: m.z)
                        } ?? Color(red: c.x, green: c.y, blue: c.z).opacity(k)
                        return .init(color: color, location: f)
                    }
                    context.fill(Path(CGRect(x: x, y: y, width: step, height: max(0, size.height - y))),
                                 with: .linearGradient(Gradient(stops: stops), startPoint: CGPoint(x: x, y: y),
                                                       endPoint: CGPoint(x: x, y: size.height)))
                }
                x += step
            }
        }
        .frame(width: size.width + Self.edge, height: size.height)
    }

    @MainActor static func image(_ plot: SparklinePlot, size: CGSize, scale: CGFloat, backdrop: Color?, island: Bool = false) -> UIImage? {
        guard size.width > 0, size.height > 0, !plot.samples.isEmpty else { return nil }
        let renderer = ImageRenderer(content: SparklineArea(plot: plot, size: size, scale: scale, backdrop: backdrop, island: island))
        renderer.scale = scale
        return renderer.uiImage
    }
}

/// One cubic of a sparkline, in view points.
struct SparkPiece {
    var p0: CGPoint, c1: CGPoint, c2: CGPoint, p3: CGPoint

    func point(_ t: CGFloat) -> CGPoint {
        let u = 1 - t
        let (a, b, c, d) = (u * u * u, 3 * u * u * t, 3 * u * t * t, t * t * t)
        return CGPoint(x: a * p0.x + b * c1.x + c * c2.x + d * p3.x, y: a * p0.y + b * c1.y + c * c2.y + d * p3.y)
    }

    func y(_ t: CGFloat) -> CGFloat {
        let u = 1 - t
        return u * u * u * p0.y + 3 * u * u * t * c1.y + 3 * u * t * t * c2.y + t * t * t * p3.y
    }

    /// The two halves at `t` (de Casteljau), which together are exactly this piece.
    func split(_ t: CGFloat) -> (SparkPiece, SparkPiece) {
        func lerp(_ a: CGPoint, _ b: CGPoint) -> CGPoint { CGPoint(x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t) }
        let (a, b, c) = (lerp(p0, c1), lerp(c1, c2), lerp(c2, p3))
        let (d, e) = (lerp(a, b), lerp(b, c))
        let f = lerp(d, e)
        return (SparkPiece(p0: p0, c1: a, c2: d, p3: f), SparkPiece(p0: f, c1: e, c2: c, p3: p3))
    }

    /// Where the piece crosses the horizontal line at `level`.
    func crossings(_ level: CGFloat) -> [CGFloat] {
        var found: [CGFloat] = []
        var (t0, v0) = (CGFloat(0), y(0) - level)
        for i in 1...24 {
            let t1 = CGFloat(i) / 24
            let v1 = y(t1) - level
            if v0 * v1 < 0 {
                var (low, high) = (t0, t1)
                for _ in 0..<24 {
                    let mid = (low + high) / 2
                    if (y(low) - level) * (y(mid) - level) <= 0 { high = mid } else { low = mid }
                }
                found.append((low + high) / 2)
            }
            (t0, v0) = (t1, v1)
        }
        return found
    }

    /// Fritsch–Carlson monotone cubic through the points: smooth, and never above or below the values around it.
    static func smooth(_ p: [CGPoint]) -> [SparkPiece] {
        let n = p.count
        guard n > 1 else { return [] }
        guard n > 2 else {
            let third = CGPoint(x: (p[1].x - p[0].x) / 3, y: (p[1].y - p[0].y) / 3)
            return [SparkPiece(p0: p[0], c1: CGPoint(x: p[0].x + third.x, y: p[0].y + third.y),
                               c2: CGPoint(x: p[1].x - third.x, y: p[1].y - third.y), p3: p[1])]
        }
        var d = [CGFloat](repeating: 0, count: n - 1)
        for i in 0..<n - 1 { d[i] = (p[i + 1].y - p[i].y) / max(p[i + 1].x - p[i].x, 0.001) }
        var m = [CGFloat](repeating: 0, count: n)
        m[0] = d[0]
        m[n - 1] = d[n - 2]
        for i in 1..<n - 1 { m[i] = d[i - 1] * d[i] <= 0 ? 0 : (d[i - 1] + d[i]) / 2 }
        for i in 0..<n - 1 {
            if d[i] == 0 { m[i] = 0; m[i + 1] = 0; continue }
            let (a, b) = (m[i] / d[i], m[i + 1] / d[i])
            let s = a * a + b * b
            if s > 9 { let t = 3 / s.squareRoot(); m[i] = t * a * d[i]; m[i + 1] = t * b * d[i] }
        }
        return (0..<n - 1).map { i in
            let h = (p[i + 1].x - p[i].x) / 3
            return SparkPiece(p0: p[i], c1: CGPoint(x: p[i].x + h, y: p[i].y + m[i] * h),
                              c2: CGPoint(x: p[i + 1].x - h, y: p[i + 1].y - m[i + 1] * h), p3: p[i + 1])
        }
    }
}

struct SparklinePlot {
    let lineWidth: CGFloat
    let dot: CGFloat
    let halo: CGFloat
    let inRange: Color
    var runs: [(path: Path, color: Color)] = []
    /// Points along the drawn curve, per unbroken stretch, with the line's colour there: what the area hangs from.
    var samples: [[(point: CGPoint, color: Color)]] = []
    var ends: [(point: CGPoint, color: Color)] = []
    var band: CGRect?
    var last: (point: CGPoint, color: Color)?

    init(node: Node, size: CGSize, clipped: Bool = false) {
        lineWidth = max(1.5, size.height / 20)
        dot = lineWidth * 2
        halo = lineWidth * 3
        let inRange = node.color == nil ? Color.accentColor : Palette.color(node.color)
        self.inRange = inRange
        let above = node.aboveColor.map { Palette.color($0) } ?? inRange
        let below = node.belowColor.map { Palette.color($0) } ?? inRange

        let values = node.values ?? []
        let known = values.compactMap { $0 }
        guard let lowest = known.min(), let highest = known.max(), size.width > 0, size.height > 0 else { return }
        let pad = highest > lowest ? (highest - lowest) * 0.15 : max(abs(highest) * 0.1, 1)
        let bottom = node.min.map { Swift.min($0, lowest) } ?? lowest - pad
        let top = node.max.map { Swift.max($0, highest) } ?? highest + pad

        let marked = node.dot == true
        // The last value sits on the trailing edge, its dot reaching over by its radius, so the graph ends where text
        // aligned to the same edge does; where the region clips its edges (the Dynamic Island) the dot stays inside. The first is in by the little the area reaches past it.
        let inset = UIEdgeInsets(top: marked ? halo : lineWidth / 2, left: lineWidth / 2 + (node.area == true ? SparklineArea.edge : 0),
                                 bottom: marked ? halo : lineWidth / 2, right: marked ? (clipped ? halo : 0) : lineWidth / 2)
        let plot = CGRect(origin: .zero, size: size).inset(by: inset)
        let span = node.from.flatMap { from in node.to.map { $0.timeIntervalSince(from) } } ?? 0
        func x(_ i: Int) -> CGFloat {
            if let step = node.step, span > 0 { return plot.minX + CGFloat(Double(i) * step / span) * plot.width }
            return values.count > 1 ? plot.minX + CGFloat(i) / CGFloat(values.count - 1) * plot.width : plot.maxX
        }
        func y(_ v: Double) -> CGFloat { plot.maxY - CGFloat((v - bottom) / (top - bottom)) * plot.height }

        // The band's edges in view points; the line is coloured by which side of them it is on.
        let upper = node.high.map { y($0) }
        let lower = node.low.map { y($0) }
        func color(atY level: CGFloat) -> Color {
            if let upper, level < upper { return above }
            if let lower, level > lower { return below }
            return inRange
        }
        if node.shade == true, let upper, let lower {
            let (top, bottom) = (min(max(upper, 0), size.height), min(max(lower, 0), size.height))
            if bottom > top { band = CGRect(x: 0, y: top, width: size.width, height: bottom - top) }
        }

        // A short run of missing values is bridged, so a reading that came a little early or late leaves no hole;
        // a long one, such as a sensor that was off, breaks the line. Same rule as RemoteViewsRenderer.Sparkline.
        let bridged = max(2, values.count / 10)
        var segments: [[CGPoint]] = [[]]
        var missing = 0
        for (i, value) in values.enumerated() {
            guard let value else { missing += 1; continue }
            if missing > bridged, !segments[segments.count - 1].isEmpty { segments.append([]) }
            missing = 0
            let point = CGPoint(x: x(i), y: y(value))
            if point.x > plot.maxX + 0.5 { break }
            segments[segments.count - 1].append(point)
        }

        for segment in segments where !segment.isEmpty {
            let pieces = SparkPiece.smooth(segment)
            if pieces.isEmpty {
                ends.append((segment[0], color(atY: segment[0].y)))
                continue
            }
            var run: (path: Path, color: Color)?
            var along: [(point: CGPoint, color: Color)] = [(segment[0], color(atY: pieces[0].y(0.02)))]
            for piece in pieces {
                let cuts = ([upper, lower].compactMap { $0 }.flatMap { piece.crossings($0) }).sorted()
                var rest = piece
                var done: CGFloat = 0
                var parts: [SparkPiece] = []
                for cut in cuts {
                    let (head, tail) = rest.split((cut - done) / (1 - done))
                    parts.append(head)
                    (rest, done) = (tail, cut)
                }
                parts.append(rest)
                for part in parts {
                    let tint = color(atY: part.y(0.5))
                    if run?.color != tint {
                        if let finished = run { runs.append(finished) }
                        var path = Path()
                        path.move(to: part.p0)
                        run = (path, tint)
                    }
                    run?.path.addCurve(to: part.p3, control1: part.c1, control2: part.c2)
                    for step in 1...8 { along.append((part.point(CGFloat(step) / 8), tint)) }
                }
            }
            if let run { runs.append(run) }
            samples.append(along)
            ends.append((segment[0], color(atY: pieces[0].y(0.02))))
            ends.append((segment[segment.count - 1], color(atY: pieces[pieces.count - 1].y(0.98))))
        }

        if let final = ends.last { last = (final.point, color(atY: final.point.y)) }
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
        if node.fill == true {
            // An equal share of the parent's axis: three of them in a row are three equal columns.
            boxed(content).frame(maxWidth: inline ? .infinity : nil, maxHeight: inline ? nil : .infinity, alignment: alignment)
        } else if node.padding == nil && node.background == nil && node.cornerRadius == nil {
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

    @ViewBuilder private func boxed(_ content: Content) -> some View {
        if node.padding == nil && node.background == nil && node.cornerRadius == nil {
            content
        } else {
            let shape = RoundedRectangle(cornerRadius: CGFloat(node.cornerRadius ?? 0))
            content
                .padding(CGFloat(node.padding ?? 0))
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

/// A self-updating time with its prefix as one Text, so centring treats them as one line: beside it in
/// a stack, a label cannot be centred with a time that takes all the width it is offered.
func prefixed(_ node: Node, _ time: Text) -> Text {
    if let prefix = node.prefix { return Text(prefix) + time }
    return time
}

struct TextStyleModifier: ViewModifier {
    let node: Node

    // Alignment only, no full-width frame: a timer already takes the whole width and is centred in it,
    // and plain text keeps its size, so a centred Dynamic Island region does not squeeze its neighbours.
    func body(content: Content) -> some View {
        content
            .font(Palette.font(node))
            .fontWeight(node.bold == true ? .bold : .regular)
            .foregroundStyle(Palette.color(node.color))
            .multilineTextAlignment(node.centered == true ? .center : node.trailing == true ? .trailing : .leading)
            .lineLimit(node.singleLine == nil ? nil : 1)
            .minimumScaleFactor(CGFloat(node.singleLine ?? 1))
    }
}

/// The language the app shows, for what the system writes in a widget or an activity: "2 min, 5 secs", a rounded age.
/// The extension is built with no localizations of its own, so iOS would write those in English whatever the app
/// shows. The app writes its language (ISpineStrings.Culture, which can differ from the phone's) to the container;
/// before it has, the app's CFBundleLocalizations are matched against the user's order, as iOS picks the app's own.
/// The user's region still decides the formats.
enum AppLanguage {
    static var locale: Locale {
        if let url = Store.root?.appendingPathComponent("language"),
           let name = try? String(contentsOf: url, encoding: .utf8), !name.isEmpty {
            return locale(language: name)
        }
        return declared
    }

    private static let declared: Locale = {
        var url = Bundle.main.bundleURL
        while url.pathExtension != "app", url.pathComponents.count > 1 { url = url.deletingLastPathComponent() }
        let app = Bundle(url: url)
        let languages = (app?.object(forInfoDictionaryKey: "CFBundleLocalizations") as? [String])
            ?? app?.localizations.filter { $0 != "Base" } ?? []
        guard !languages.isEmpty,
              let language = Bundle.preferredLocalizations(from: languages, forPreferences: Locale.preferredLanguages).first
        else { return .autoupdatingCurrent }
        return locale(language: language)
    }()

    private static func locale(language: String) -> Locale {
        var components = Locale.Components(locale: .current)
        components.languageComponents = Locale.Language.Components(identifier: language)
        return Locale(components: components)
    }
}

enum Palette {
    /// The node's text style in the system font's design (rounded for SF Pro Rounded), or a fixed size in points.
    static func font(_ node: Node) -> Font {
        let design: Font.Design = switch node.design?.lowercased() {
        case "rounded": .rounded
        case "monospaced": .monospaced
        case "serif": .serif
        default: .default
        }
        if let size = node.size { return .system(size: CGFloat(size), design: design) }
        let style: Font.TextStyle = switch node.font?.lowercased() {
        case "largetitle": .largeTitle
        case "title": .title2
        case "headline": .headline
        case "caption": .caption
        default: .body
        }
        return .system(style, design: design)
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
    /// The colour behind the Lock Screen presentation (and CarPlay's and the watch's, which share it) when it is one
    /// opaque colour; nil for the system's material or Spine's translucent black.
    static func backdrop(_ layout: ActivityLayout) -> Color? {
        guard let background = layout.background else { return nil }
        let solid = color(background)
        return UIColor(solid).cgColor.alpha >= 1 ? solid : nil
    }

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

    /// An entry with a surface of its own is drawn on it; the rest on `background`, the document's.
    private func entries(from document: TimelineDocument?, link: String?, background: SurfaceBackground?) -> [Entry] {
        guard let document else { return [] }
        return document.entries
            .sorted { $0.date < $1.date }
            .map { Entry(date: $0.date, trees: $0.trees, link: link, kind: kind, background: SurfaceBackground($0) ?? background) }
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
            NodeView(node: tree).environment(\.spineKind, entry.kind).environment(\.locale, AppLanguage.locale)
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
    /// The solid colour the region is drawn on, when known; see SpineBackdropKey.
    var backdrop: Color?
    /// Whether the region clips at its edges; see SpineClippedKey.
    var clipped = false
    var body: some View {
        if let node {
            NodeView(node: node).environment(\.spineKind, kind).environment(\.locale, AppLanguage.locale)
                .environment(\.spineBackdrop, backdrop).environment(\.spineClipped, clipped)
        } else {
            EmptyView()
        }
    }
}

// ActivityKit does not exist on Mac Catalyst; the build leaves the activity out of the bundle there.
#if !targetEnvironment(macCatalyst)
struct SpineLiveActivity: Widget {
    var body: some WidgetConfiguration { spineActivityConfiguration() }
}

/// The activity with the small family declared, which the build picks when the project sets
/// SpineWidgetsLiveActivitySmall: CarPlay and the Apple Watch Smart Stack then draw the banner view with
/// `activityFamily` set to `.small`, instead of the compact Dynamic Island they show otherwise.
struct SpineSmallLiveActivity: Widget {
    var body: some WidgetConfiguration {
        // Unlike a WidgetBundle's, this if #available can have an else: an opaque result type may
        // differ by OS version (SE-0360), so the extension still runs on iOS 17.
        if #available(iOS 18.0, *) {
            return spineActivityConfiguration().supplementalActivityFamilies([.small])
        } else {
            return spineActivityConfiguration()
        }
    }
}

private func spineActivityConfiguration() -> some WidgetConfiguration {
    ActivityConfiguration(for: SpineActivityAttributes.self) { context in
        let layout = ActivityLayout.parse(context.state.layout)
        ActivityBanner(layout: layout, kind: context.attributes.kind)
            .environment(\.spineBackdrop, Palette.backdrop(layout))
            .opacity(context.isStale ? 0.5 : 1)
            .activityBackgroundTint(Palette.lockScreenTint(layout))
            .activitySystemActionForegroundColor(layout.actionColor.map { Palette.color($0) })
            .widgetURL(layout.link.flatMap(URL.init(string:)))
    } dynamicIsland: { context in
        let layout = ActivityLayout.parse(context.state.layout)
        let kind = context.attributes.kind
        // The Dynamic Island is always black.
        return DynamicIsland {
            // Beside a taller centre the side regions would sit at the top, level with the camera;
            // filling their height centres them on it instead.
            DynamicIslandExpandedRegion(.leading) { Slot(node: layout.expandedLeading, kind: kind, backdrop: .black, clipped: true).frame(maxHeight: .infinity) }
            DynamicIslandExpandedRegion(.trailing) { Slot(node: layout.expandedTrailing, kind: kind, backdrop: .black, clipped: true).frame(maxHeight: .infinity) }
            DynamicIslandExpandedRegion(.center) { Slot(node: layout.expandedCenter, kind: kind, backdrop: .black, clipped: true) }
            DynamicIslandExpandedRegion(.bottom) { Slot(node: layout.expandedBottom, kind: kind, backdrop: .black, clipped: true) }
        } compactLeading: {
            Slot(node: layout.compactLeading, kind: kind, backdrop: .black, clipped: true)
        } compactTrailing: {
            Slot(node: layout.compactTrailing, kind: kind, backdrop: .black, clipped: true)
        } minimal: {
            Slot(node: layout.minimal, kind: kind, backdrop: .black, clipped: true)
        }
        .widgetURL(layout.link.flatMap(URL.init(string:)))
    }
}

/// The Lock Screen banner; on iOS 18 also the small family, which is drawn by the same view.
struct ActivityBanner: View {
    let layout: ActivityLayout
    let kind: String

    var body: some View {
        if #available(iOS 18.0, *) {
            FamilyBanner(layout: layout, kind: kind)
        } else {
            Slot(node: layout.lockScreen, kind: kind).padding()
        }
    }
}

@available(iOS 18.0, *)
private struct FamilyBanner: View {
    @Environment(\.activityFamily) private var family
    let layout: ActivityLayout
    let kind: String

    var body: some View {
        if family == .small {
            if let small = layout.small {
                Slot(node: small, kind: kind).padding(10)
            } else {
                // Another kind in the same app may have no small tree; it gets what the system shows
                // when the family is not declared.
                HStack {
                    Slot(node: layout.compactLeading, kind: kind)
                    Spacer(minLength: 8)
                    Slot(node: layout.compactTrailing, kind: kind)
                }
                .padding(10)
            }
        } else {
            Slot(node: layout.lockScreen, kind: kind).padding()
        }
    }
}
#endif
