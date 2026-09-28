using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Infrastructure.ContentNames;

/// <summary>
/// Main-thread-only adapter for the runtime potion display chain:
/// ModelKey -> ModelId -> ModelDb.GetByIdOrNull&lt;PotionModel&gt; -> HoverTip/HoverTips.
/// It never opens the native hover-tip UI and never mutates discovery state.
/// </summary>
internal sealed class RuntimePotionTooltipResolver : IPotionTooltipResolver
{
    private static readonly Regex ImageTag = new(
        @"\[img(?:=[^\]]*)?\].*?\[/img\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex AnyBbCodeTag = new(
        @"\[(?:/?)[a-z][^\]]*\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly Type? _modelIdType;
    private readonly Type? _potionModelType;
    private readonly ConstructorInfo? _modelIdConstructor;
    private readonly MethodInfo? _getPotionById;
    private readonly PropertyInfo? _hoverTipProperty;
    private readonly PropertyInfo? _hoverTipsProperty;

    public RuntimePotionTooltipResolver()
    {
        try
        {
            Assembly? gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => string.Equals(
                    assembly.GetName().Name,
                    "sts2",
                    StringComparison.OrdinalIgnoreCase));
            Type? modelDbType = gameAssembly?.GetType(
                "MegaCrit.Sts2.Core.Models.ModelDb",
                throwOnError: false,
                ignoreCase: false);
            _modelIdType = gameAssembly?.GetType(
                "MegaCrit.Sts2.Core.Models.ModelId",
                throwOnError: false,
                ignoreCase: false);
            _potionModelType = gameAssembly?.GetType(
                "MegaCrit.Sts2.Core.Models.PotionModel",
                throwOnError: false,
                ignoreCase: false);
            _modelIdConstructor = _modelIdType?.GetConstructor(new[] { typeof(string), typeof(string) });

            MethodInfo? genericGetById = modelDbType?
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method =>
                {
                    if (!string.Equals(method.Name, "GetByIdOrNull", StringComparison.Ordinal) ||
                        !method.IsGenericMethodDefinition)
                    {
                        return false;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    return parameters.Length == 1 && parameters[0].ParameterType == _modelIdType;
                })
                .OrderBy(method => method.MetadataToken)
                .FirstOrDefault();
            if (genericGetById is not null && _potionModelType is not null)
            {
                _getPotionById = genericGetById.MakeGenericMethod(_potionModelType);
            }

            _hoverTipProperty = _potionModelType?.GetProperty(
                "HoverTip",
                BindingFlags.Public | BindingFlags.Instance);
            _hoverTipsProperty = _potionModelType?.GetProperty(
                "HoverTips",
                BindingFlags.Public | BindingFlags.Instance);
        }
        catch
        {
            // A missing or incompatible runtime ABI degrades to a name-only tooltip.
        }
    }

    public PotionTooltipSnapshot Resolve(ModelKey potionKey, string fallbackTitle)
    {
        string title = string.IsNullOrWhiteSpace(fallbackTitle) ? potionKey.Entry : fallbackTitle.Trim();
        if (!RuntimeSnapshotThreadGuard.IsMainThread)
        {
            return Missing(potionKey, title, "RuntimePotionTooltipRequiresMainThread");
        }
        if (!potionKey.IsValid)
        {
            return Missing(potionKey, title, "RuntimePotionTooltipInvalidModelKey");
        }

        if (_modelIdType is null ||
            _potionModelType is null ||
            _modelIdConstructor is null ||
            _getPotionById is null ||
            (_hoverTipProperty is null && _hoverTipsProperty is null))
        {
            return Missing(potionKey, title, "RuntimePotionTooltipAbiUnavailable");
        }

        try
        {
            object modelId = _modelIdConstructor.Invoke(new object[] { potionKey.Category, potionKey.Entry });
            object? potion = _getPotionById.Invoke(null, new[] { modelId });
            if (potion is null || !_potionModelType.IsInstanceOfType(potion))
            {
                return Missing(potionKey, title, "RuntimePotionTooltipModelNotFound");
            }

            object? hoverTip = _hoverTipProperty?.GetValue(potion);
            if (hoverTip is null && _hoverTipsProperty?.GetValue(potion) is IEnumerable hoverTips)
            {
                hoverTip = hoverTips.Cast<object?>().FirstOrDefault(value => value is not null);
            }
            if (hoverTip is null)
            {
                return Missing(potionKey, title, "RuntimePotionTooltipHoverTipMissing");
            }

            Type hoverTipType = hoverTip.GetType();
            PropertyInfo? titleProperty = hoverTipType.GetProperty(
                "Title",
                BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo? descriptionProperty = hoverTipType.GetProperty(
                "Description",
                BindingFlags.Public | BindingFlags.Instance);
            if (titleProperty is null || descriptionProperty is null)
            {
                return Missing(potionKey, title, "RuntimePotionTooltipContractUnavailable");
            }

            string? runtimeTitle = titleProperty.GetValue(hoverTip) as string;
            string? runtimeDescription = descriptionProperty.GetValue(hoverTip) as string;
            if (!string.IsNullOrWhiteSpace(runtimeTitle))
            {
                string plainTitle = ToPlainText(runtimeTitle).Trim();
                if (plainTitle.Length > 0)
                {
                    title = plainTitle;
                }
            }

            string description = ToPlainText(runtimeDescription ?? string.Empty).Trim();
            return new PotionTooltipSnapshot(
                potionKey,
                title,
                description,
                description.Length > 0,
                description.Length > 0
                    ? "RuntimePotionTooltipHoverTipResolved"
                    : "RuntimePotionTooltipDescriptionEmpty");
        }
        catch (Exception ex)
        {
            return Missing(potionKey, title, "RuntimePotionTooltipResolveFailed:" + ex.GetType().Name);
        }
    }

    private static PotionTooltipSnapshot Missing(ModelKey key, string title, string evidence) =>
        new(key, title, string.Empty, false, evidence);

    private static string ToPlainText(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        string text = ImageTag.Replace(value, string.Empty);
        text = AnyBbCodeTag.Replace(text, string.Empty);
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        return text;
    }
}
