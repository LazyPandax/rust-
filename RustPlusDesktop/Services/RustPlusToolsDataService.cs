using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace RustPlusDesk.Services;

public sealed class RustPlusToolsDataService
{
    private readonly List<RustPlusToolEntry> _entries = new();
    private readonly Dictionary<string, RustPlusToolEntry> _itemsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, JsonElement> _cctvByMonument = new(StringComparer.OrdinalIgnoreCase);

    private JsonDocument? _itemsDoc;
    private JsonDocument? _craftDoc;
    private JsonDocument? _recycleDoc;
    private JsonDocument? _researchDoc;
    private JsonDocument? _decayDoc;
    private JsonDocument? _upkeepDoc;
    private JsonDocument? _buildingBlocksDoc;
    private JsonDocument? _otherDoc;
    private JsonDocument? _cctvDoc;

    public IReadOnlyList<string> CctvMonuments { get; private set; } = Array.Empty<string>();
    public bool IsLoaded { get; private set; }

    public void Load()
    {
        if (IsLoaded) return;

        _itemsDoc = ReadJson("items.json");
        _craftDoc = ReadJson("rustlabsCraftData.json");
        _recycleDoc = ReadJson("rustlabsRecycleData.json");
        _researchDoc = ReadJson("rustlabsResearchData.json");
        _decayDoc = ReadJson("rustlabsDecayData.json");
        _upkeepDoc = ReadJson("rustlabsUpkeepData.json");
        _buildingBlocksDoc = ReadJson("rustlabsBuildingBlocks.json");
        _otherDoc = ReadJson("rustlabsOther.json");
        _cctvDoc = ReadJson("cctv.json");

        LoadItems();
        LoadNamedEntries(_buildingBlocksDoc, RustPlusToolEntryKind.BuildingBlock);
        LoadNamedEntries(_otherDoc, RustPlusToolEntryKind.Other);
        LoadCctv();

        IsLoaded = true;
    }

    public IReadOnlyList<RustPlusToolEntry> Search(string query, int take = 80)
    {
        Load();
        query = (query ?? "").Trim();

        if (query.Length == 0)
            return _entries
                .OrderBy(e => e.Kind)
                .ThenBy(e => e.Name)
                .Take(take)
                .ToList();

        return _entries
            .Select(e => new { Entry = e, Score = Score(e, query) })
            .Where(x => x.Score < 1000)
            .OrderBy(x => x.Score)
            .ThenBy(x => x.Entry.Name)
            .Take(take)
            .Select(x => x.Entry)
            .ToList();
    }

    public string FormatItemDetails(RustPlusToolEntry? entry)
    {
        if (entry is null) return "Search for an item, building block, or Rust entity.";

        var sb = new StringBuilder();
        sb.AppendLine(entry.Name);
        sb.AppendLine($"Type: {entry.KindLabel}");
        if (!string.IsNullOrWhiteSpace(entry.Id))
            sb.AppendLine($"Item ID: {entry.Id}");
        if (!string.IsNullOrWhiteSpace(entry.ShortName))
            sb.AppendLine($"Short name: {entry.ShortName}");
        if (!string.IsNullOrWhiteSpace(entry.Description))
        {
            sb.AppendLine();
            sb.AppendLine(entry.Description);
        }
        return sb.ToString().TrimEnd();
    }

    public string FormatCraft(RustPlusToolEntry? entry, int quantity)
    {
        if (entry is null) return "Select an item first.";
        if (entry.Kind != RustPlusToolEntryKind.Item) return "Craft data is only available for Rust items.";
        if (!TryGetById(_craftDoc, entry.Id, out var craft)) return $"No craft data found for {entry.Name}.";

        quantity = Math.Max(1, quantity);
        var sb = new StringBuilder();
        sb.AppendLine($"{entry.Name} x{quantity}");
        AppendJsonString(sb, craft, "timeString", "Craft time");
        if (TryGetString(craft, "workbench", out var workbench) && !string.IsNullOrWhiteSpace(workbench))
            sb.AppendLine($"Workbench: {NameForId(workbench)}");
        else
            sb.AppendLine("Workbench: none");

        sb.AppendLine();
        sb.AppendLine("Ingredients:");
        if (craft.TryGetProperty("ingredients", out var ingredients) && ingredients.ValueKind == JsonValueKind.Array)
        {
            foreach (var ingredient in ingredients.EnumerateArray())
            {
                var id = GetString(ingredient, "id");
                var itemQuantity = GetDouble(ingredient, "quantity") * quantity;
                sb.AppendLine($"- {FormatQuantity(itemQuantity)} {NameForId(id)}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    public string FormatRecycle(RustPlusToolEntry? entry, int quantity, string recyclerType)
    {
        if (entry is null) return "Select an item first.";
        if (entry.Kind != RustPlusToolEntryKind.Item) return "Recycle data is only available for Rust items.";
        if (!TryGetById(_recycleDoc, entry.Id, out var recycle)) return $"No recycle data found for {entry.Name}.";

        recyclerType = string.IsNullOrWhiteSpace(recyclerType) ? "recycler" : recyclerType;
        if (!recycle.TryGetProperty(recyclerType, out var mode) || mode.ValueKind != JsonValueKind.Object)
            return $"No {recyclerType} data found for {entry.Name}.";

        quantity = Math.Max(1, quantity);
        var sb = new StringBuilder();
        sb.AppendLine($"{entry.Name} x{quantity}");
        sb.AppendLine($"Recycler type: {recyclerType}");
        AppendJsonString(sb, mode, "efficiency", "Efficiency");
        sb.AppendLine();
        sb.AppendLine("Yield:");

        if (!mode.TryGetProperty("yield", out var yields) || yields.ValueKind != JsonValueKind.Array || yields.GetArrayLength() == 0)
        {
            sb.AppendLine("- none");
            return sb.ToString().TrimEnd();
        }

        foreach (var item in yields.EnumerateArray())
        {
            var id = GetString(item, "id");
            var itemQuantity = GetDouble(item, "quantity") * quantity;
            var probability = GetDouble(item, "probability");
            var chance = probability > 0 && probability < 1
                ? $" ({probability * 100:0.#}% chance)"
                : "";
            sb.AppendLine($"- {FormatQuantity(itemQuantity)} {NameForId(id)}{chance}");
        }

        return sb.ToString().TrimEnd();
    }

    public string FormatResearch(RustPlusToolEntry? entry)
    {
        if (entry is null) return "Select an item first.";
        if (entry.Kind != RustPlusToolEntryKind.Item) return "Research data is only available for Rust items.";
        if (!TryGetById(_researchDoc, entry.Id, out var research)) return $"No research data found for {entry.Name}.";

        var sb = new StringBuilder();
        sb.AppendLine(entry.Name);
        AppendJsonString(sb, research, "researchTable", "Research table scrap");

        if (research.TryGetProperty("workbench", out var workbench) && workbench.ValueKind == JsonValueKind.Object)
        {
            sb.AppendLine();
            sb.AppendLine("Workbench path:");
            sb.AppendLine($"- Bench: {NameForId(GetString(workbench, "type"))}");
            AppendJsonString(sb, workbench, "scrap", "Workbench scrap");
            AppendJsonString(sb, workbench, "totalScrap", "Total scrap");
        }
        else
        {
            sb.AppendLine("Workbench path: none");
        }

        return sb.ToString().TrimEnd();
    }

    public string FormatDecay(RustPlusToolEntry? entry)
    {
        if (entry is null) return "Select an item, building block, or entity first.";
        if (!TryGetSectionValue(_decayDoc, entry, out var decay)) return $"No decay data found for {entry.Name}.";

        var sb = new StringBuilder();
        sb.AppendLine(entry.Name);
        AppendJsonString(sb, decay, "hpString", "HP");
        AppendJsonString(sb, decay, "decayString", "Decay");
        AppendJsonString(sb, decay, "decayOutsideString", "Outside");
        AppendJsonString(sb, decay, "decayInsideString", "Inside");
        AppendJsonString(sb, decay, "decayUnderwaterString", "Underwater");
        return sb.ToString().TrimEnd();
    }

    public string FormatUpkeep(RustPlusToolEntry? entry)
    {
        if (entry is null) return "Select an item or building block first.";
        if (!TryGetSectionValue(_upkeepDoc, entry, out var upkeep)) return $"No upkeep data found for {entry.Name}.";
        if (upkeep.ValueKind != JsonValueKind.Array || upkeep.GetArrayLength() == 0) return $"No upkeep cost found for {entry.Name}.";

        var sb = new StringBuilder();
        sb.AppendLine(entry.Name);
        sb.AppendLine("Upkeep:");
        foreach (var item in upkeep.EnumerateArray())
        {
            var id = GetString(item, "id");
            var quantity = GetString(item, "quantity");
            sb.AppendLine($"- {quantity} {NameForId(id)}");
        }
        return sb.ToString().TrimEnd();
    }

    public string FormatCctv(string? monument)
    {
        Load();
        monument = (monument ?? "").Trim();
        if (monument.Length == 0) return "Select a monument.";
        if (!_cctvByMonument.TryGetValue(monument, out var info)) return $"No CCTV data found for {monument}.";

        var sb = new StringBuilder();
        sb.AppendLine(monument);
        if (TryGetBool(info, "dynamic", out var dynamic) && dynamic)
            sb.AppendLine("Dynamic code: replace * with the server generated digits.");
        sb.AppendLine();
        sb.AppendLine("Codes:");
        if (info.TryGetProperty("codes", out var codes) && codes.ValueKind == JsonValueKind.Array)
        {
            foreach (var code in codes.EnumerateArray())
                sb.AppendLine($"- {CleanCode(code.GetString())}");
        }
        return sb.ToString().TrimEnd();
    }

    private void LoadItems()
    {
        if (_itemsDoc is null) return;
        foreach (var item in _itemsDoc.RootElement.EnumerateObject())
        {
            var value = item.Value;
            var entry = new RustPlusToolEntry(
                RustPlusToolEntryKind.Item,
                item.Name,
                GetString(value, "name"),
                GetString(value, "shortname"),
                GetString(value, "description"),
                item.Name);
            _entries.Add(entry);
            _itemsById[item.Name] = entry;
        }
    }

    private void LoadNamedEntries(JsonDocument? doc, RustPlusToolEntryKind kind)
    {
        if (doc is null) return;
        foreach (var item in doc.RootElement.EnumerateObject())
        {
            _entries.Add(new RustPlusToolEntry(kind, "", item.Name, item.Value.GetString() ?? "", "", item.Name));
        }
    }

    private void LoadCctv()
    {
        if (_cctvDoc is null) return;
        foreach (var item in _cctvDoc.RootElement.EnumerateObject())
            _cctvByMonument[item.Name] = item.Value;

        CctvMonuments = _cctvByMonument.Keys.OrderBy(x => x).ToList();
    }

    private static JsonDocument ReadJson(string fileName)
    {
        foreach (var path in CandidatePaths(fileName))
        {
            if (File.Exists(path))
                return JsonDocument.Parse(File.ReadAllText(path));
        }

        throw new FileNotFoundException($"Rust++ tool data file was not found: {fileName}");
    }

    private static IEnumerable<string> CandidatePaths(string fileName)
    {
        var rel = Path.Combine("Assets", "Data", "RustPlusTools", fileName);
        yield return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, rel);
        yield return Path.Combine(Environment.CurrentDirectory, rel);
        yield return Path.Combine(AppContext.BaseDirectory, rel);
    }

    private int Score(RustPlusToolEntry entry, string query)
    {
        var q = query.ToLowerInvariant();
        var name = entry.Name.ToLowerInvariant();
        var shortName = entry.ShortName.ToLowerInvariant();
        var id = entry.Id.ToLowerInvariant();

        if (name.Equals(q, StringComparison.Ordinal)) return 0;
        if (shortName.Equals(q, StringComparison.Ordinal)) return 1;
        if (id.Equals(q, StringComparison.Ordinal)) return 2;
        if (name.StartsWith(q, StringComparison.Ordinal)) return 10;
        if (shortName.StartsWith(q, StringComparison.Ordinal)) return 15;
        if (name.Contains(q, StringComparison.Ordinal)) return 50;
        if (shortName.Contains(q, StringComparison.Ordinal)) return 60;
        if (id.Contains(q, StringComparison.Ordinal)) return 70;
        return 1000;
    }

    private string NameForId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "none";
        return _itemsById.TryGetValue(id, out var item) ? item.Name : $"Item #{id}";
    }

    private static bool TryGetById(JsonDocument? doc, string id, out JsonElement value)
    {
        value = default;
        return doc is not null && !string.IsNullOrWhiteSpace(id) &&
               doc.RootElement.TryGetProperty(id, out value);
    }

    private static bool TryGetSectionValue(JsonDocument? doc, RustPlusToolEntry entry, out JsonElement value)
    {
        value = default;
        if (doc is null) return false;

        var section = entry.Kind switch
        {
            RustPlusToolEntryKind.Item => "items",
            RustPlusToolEntryKind.BuildingBlock => "buildingBlocks",
            RustPlusToolEntryKind.Other => "other",
            _ => ""
        };

        var key = entry.Kind == RustPlusToolEntryKind.Item ? entry.Id : entry.Name;
        return doc.RootElement.TryGetProperty(section, out var root) &&
               root.ValueKind == JsonValueKind.Object &&
               root.TryGetProperty(key, out value);
    }

    private static string GetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return "";
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => ""
        };
    }

    private static bool TryGetString(JsonElement element, string property, out string value)
    {
        value = GetString(element, property);
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetBool(JsonElement element, string property, out bool value)
    {
        value = false;
        if (!element.TryGetProperty(property, out var prop) ||
            prop.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return false;

        value = prop.GetBoolean();
        return true;
    }

    private static double GetDouble(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var d)) return d;
        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
        return 0;
    }

    private static void AppendJsonString(StringBuilder sb, JsonElement element, string property, string label)
    {
        var value = GetString(element, property);
        if (!string.IsNullOrWhiteSpace(value))
            sb.AppendLine($"{label}: {value}");
    }

    private static string FormatQuantity(double quantity)
    {
        return Math.Abs(quantity - Math.Round(quantity)) < 0.001
            ? ((int)Math.Round(quantity)).ToString(CultureInfo.InvariantCulture)
            : quantity.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string CleanCode(string? code) => (code ?? "").Replace("\\*", "*");
}

public enum RustPlusToolEntryKind
{
    Item,
    BuildingBlock,
    Other
}

public sealed record RustPlusToolEntry(
    RustPlusToolEntryKind Kind,
    string Id,
    string Name,
    string ShortName,
    string Description,
    string Key)
{
    public string KindLabel => Kind switch
    {
        RustPlusToolEntryKind.Item => "Item",
        RustPlusToolEntryKind.BuildingBlock => "Building block",
        RustPlusToolEntryKind.Other => "Entity",
        _ => "Entry"
    };

    public override string ToString()
    {
        var extra = Kind == RustPlusToolEntryKind.Item && !string.IsNullOrWhiteSpace(ShortName)
            ? $"  ({ShortName})"
            : "";
        return $"{Name}{extra}  [{KindLabel}]";
    }
}
