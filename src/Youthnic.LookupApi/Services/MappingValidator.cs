using System.Text.RegularExpressions;
using Youthnic.LookupApi.Options;

namespace Youthnic.LookupApi.Services;

/// <summary>
/// Strict allowlist validation for table/column identifiers.
/// Values are NEVER identifiers in this API — only $1/$2 parameters — so the only
/// injection surface is identifiers, and this regex is the gate for all of them.
/// </summary>
public static partial class MappingValidator
{
    // Strict allowlist: lowercase identifier, optionally schema-qualified with a single dot.
    private const string IdentifierPattern = "^[a-z_][a-z0-9_]*(\\.[a-z_][a-z0-9_]*)?$";
    private const int MaxIdentifierLength = 128;

    [GeneratedRegex(IdentifierPattern, RegexOptions.Compiled)]
    private static partial Regex IdentifierRegex();

    public static bool IsValidIdentifier(string? value)
        => !string.IsNullOrWhiteSpace(value)
        && value.Length <= MaxIdentifierLength
        && IdentifierRegex().IsMatch(value);

    public static void EnsureValid(string name, string? value)
    {
        if (!IsValidIdentifier(value))
            throw new InvalidOperationException($"Invalid identifier configured for {name}: '{value}'. Only lowercase letters, digits and underscores are allowed (optional schema.table form).");
    }

    /// <summary>Field names that must be resolved for the mapping to be usable.</summary>
    public static readonly string[] RequiredFields =
    [
        nameof(ForwardScanMapping.OrdersTable),
        nameof(ForwardScanMapping.ColTracking),
        nameof(ForwardScanMapping.OrdersItemsTable),
        nameof(ForwardScanMapping.OrderItemsOrderJoinColumn),
        nameof(ForwardScanMapping.OrderItemsSkuColumn),
    ];

    /// <summary>
    /// The subset of <see cref="RequiredFields"/> that is only required in items-table mode.
    /// When <see cref="ForwardScanMapping.ItemsJsonColumn"/> is set, line items come from the
    /// JSON column on the orders table instead, and these fields may stay empty.
    /// </summary>
    private static readonly string[] ItemsTableOnlyRequiredFields =
    [
        nameof(ForwardScanMapping.OrdersItemsTable),
        nameof(ForwardScanMapping.OrderItemsOrderJoinColumn),
        nameof(ForwardScanMapping.OrderItemsSkuColumn),
    ];

    /// <summary>Every mapping field, required first, used to validate anything non-empty.</summary>
    public static readonly string[] AllFields =
    [
        .. RequiredFields,
        nameof(ForwardScanMapping.SchemaOrders),
        nameof(ForwardScanMapping.ColMarketplace),
        nameof(ForwardScanMapping.ColSeller),
        nameof(ForwardScanMapping.SkuMapTable),
        nameof(ForwardScanMapping.ColSku),
        nameof(ForwardScanMapping.ColSkuName),
        nameof(ForwardScanMapping.ItemsJsonColumn),
        nameof(ForwardScanMapping.ImageTable),
        nameof(ForwardScanMapping.ImageSkuJoinColumn),
        nameof(ForwardScanMapping.ColImageUrl),
    ];

    public static string? GetField(ForwardScanMapping mapping, string fieldName)
        => fieldName switch
        {
            nameof(ForwardScanMapping.SchemaOrders) => mapping.SchemaOrders,
            nameof(ForwardScanMapping.OrdersTable) => mapping.OrdersTable,
            nameof(ForwardScanMapping.ColTracking) => mapping.ColTracking,
            nameof(ForwardScanMapping.ColMarketplace) => mapping.ColMarketplace,
            nameof(ForwardScanMapping.ColSeller) => mapping.ColSeller,
            nameof(ForwardScanMapping.OrdersItemsTable) => mapping.OrdersItemsTable,
            nameof(ForwardScanMapping.OrderItemsOrderJoinColumn) => mapping.OrderItemsOrderJoinColumn,
            nameof(ForwardScanMapping.OrderItemsSkuColumn) => mapping.OrderItemsSkuColumn,
            nameof(ForwardScanMapping.ItemsJsonColumn) => mapping.ItemsJsonColumn,
            nameof(ForwardScanMapping.SkuMapTable) => mapping.SkuMapTable,
            nameof(ForwardScanMapping.ColSku) => mapping.ColSku,
            nameof(ForwardScanMapping.ColSkuName) => mapping.ColSkuName,
            nameof(ForwardScanMapping.ImageTable) => mapping.ImageTable,
            nameof(ForwardScanMapping.ImageSkuJoinColumn) => mapping.ImageSkuJoinColumn,
            nameof(ForwardScanMapping.ColImageUrl) => mapping.ColImageUrl,
            _ => throw new ArgumentOutOfRangeException(nameof(fieldName), fieldName, null),
        };

    /// <summary>
    /// Returns the names of required fields that are still empty. In items-JSON mode
    /// (<see cref="ForwardScanMapping.ItemsJsonColumn"/> set) the order-items join fields
    /// are not required.
    /// </summary>
    public static IReadOnlyList<string> FindMissingRequired(ForwardScanMapping mapping)
    {
        var missing = RequiredFields.Where(f => string.IsNullOrWhiteSpace(GetField(mapping, f))).ToList();
        if (!string.IsNullOrWhiteSpace(mapping.ItemsJsonColumn))
            missing.RemoveAll(ItemsTableOnlyRequiredFields.Contains);
        return missing;
    }

    /// <summary>
    /// Validates the mapping: required fields present and every provided identifier
    /// passes the allowlist. Throws <see cref="InvalidOperationException"/> with a full gap report.
    /// </summary>
    public static void ValidateMapping(ForwardScanMapping mapping)
    {
        var problems = new List<string>();

        foreach (var field in AllFields)
        {
            var value = GetField(mapping, field);
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (!IsValidIdentifier(value))
                problems.Add($"{field}='{value}' fails the identifier allowlist");
        }

        var missing = FindMissingRequired(mapping);
        if (missing.Count > 0)
            problems.Add($"unresolved required mapping fields: {string.Join(", ", missing)}");

        if (problems.Count > 0)
            throw new InvalidOperationException(string.Join("; ", problems));
    }
}
