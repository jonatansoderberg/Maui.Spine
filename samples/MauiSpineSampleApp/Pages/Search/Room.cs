namespace MauiSpineSampleApp.Pages.Search;

/// <summary>What a search result stores to open a room: the id, not the room.</summary>
public sealed record RoomId(string Value);

public sealed record Room(RoomId Id, string Name, string Icon, string Summary, string[] Keywords)
{
    public static IReadOnlyList<Room> All { get; } =
    [
        new(new("kitchen"), "Kitchen", "kitchen", "21 °C · 3 lamps on", ["fridge", "stove", "dishwasher"]),
        new(new("living-room"), "Living room", "livingroom", "22 °C · TV on", ["sofa", "tv", "fireplace"]),
        new(new("bedroom"), "Bedroom", "bedroom", "19 °C · Blinds down", ["blinds", "sleep"]),
        new(new("office"), "Office", "office", "21 °C · Printer idle", ["desk", "printer"]),
        new(new("bathroom"), "Bathroom", "bathroom", "24 °C · Floor heating", ["floor heating", "towel rail"]),
        new(new("hallway"), "Hallway", "hallway", "20 °C · Door locked", ["door", "lock"]),
    ];

    public static Room? Find(RoomId id) => All.FirstOrDefault(r => r.Id == id);

    public SearchableItem ToSearchable() => new(
        Id: $"room-{Id.Value}",
        Title: Name,
        Target: NavigationTarget.To<RoomPage, RoomId>(Id),
        Description: Summary,
        Icon: Icon,
        Keywords: Keywords);
}
