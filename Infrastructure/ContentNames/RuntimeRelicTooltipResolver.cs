using System.Reflection;
using System.Text.RegularExpressions;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Infrastructure.ContentNames;

/// <summary>
/// Main-thread-only adapter for the audited runtime chain:
/// ModelKey -> ModelId -> ModelDb.GetByIdOrNull&lt;RelicModel&gt; -> RelicModel.HoverTip.
/// It never creates mutable relics and never invokes the game's native hover-tip UI.
/// </summary>
internal sealed class RuntimeRelicTooltipResolver : IRelicTooltipResolver
{
    private static readonly Regex ImageTag = new(
        @"\[img(?:=[^\]]*)?\].*?\[/img\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex AnyBbCodeTag = new(
        @"\[(?:/?)[a-z][^\]]*\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly Type? _modelIdType;
    private readonly Type? _relicModelType;
    private readonly ConstructorInfo? _modelIdConstructor;
    private readonly MethodInfo? _getRelicById;
    private readonly PropertyInfo? _hoverTipProperty;
    private readonly PropertyInfo? _hoverTipTitleProperty;
    private readonly PropertyInfo? _hoverTipDescriptionProperty;

    public RuntimeRelicTooltipResolver()
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
            _relicModelType = gameAssembly?.GetType(
                "MegaCrit.Sts2.Core.Models.RelicModel",
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
            if (genericGetById is not null && _relicModelType is not null)
            {
                _getRelicById = genericGetById.MakeGenericMethod(_relicModelType);
            }

            _hoverTipProperty = _relicModelType?.GetProperty(
                "HoverTip",
                BindingFlags.Public | BindingFlags.Instance);
            Type? hoverTipType = _hoverTipProperty?.PropertyType;
            _hoverTipTitleProperty = hoverTipType?.GetProperty(
                "Title",
                BindingFlags.Public | BindingFlags.Instance);
            _hoverTipDescriptionProperty = hoverTipType?.GetProperty(
                "Description",
                BindingFlags.Public | BindingFlags.Instance);
        }
        catch
        {
            // A missing or incompatible runtime ABI degrades to a name-only tooltip.
        }
    }

    public RelicTooltipSnapshot Resolve(ModelKey relicKey, string fallbackTitle)
    {
        string title = string.IsNullOrWhiteSpace(fallbackTitle) ? relicKey.Entry : fallbackTitle.Trim();
        if (!RuntimeSnapshotThreadGuard.IsMainThread)
        {
            return Missing(relicKey, title, "RuntimeRelicTooltipRequiresMainThread");
        }
        if (!relicKey.IsValid)
        {
            return Missing(relicKey, title, "RuntimeRelicTooltipInvalidModelKey");
        }

        if (_modelIdType is null ||
            _relicModelType is null ||
            _modelIdConstructor is null ||
            _getRelicById is null ||
            _hoverTipProperty is null ||
            _hoverTipTitleProperty is null ||
            _hoverTipDescriptionProperty is null)
        {
            return Missing(relicKey, title, "RuntimeRelicTooltipAbiUnavailable");
        }

        try
        {
            object modelId = _modelIdConstructor.Invoke(new object[] { relicKey.Category, relicKey.Entry });
            object? relic = _getRelicById.Invoke(null, new[] { modelId });
            if (relic is null || !_relicModelType.IsInstanceOfType(relic))
            {
                return Missing(relicKey, title, "RuntimeRelicTooltipModelNotFound");
            }

            object? hoverTip = _hoverTipProperty.GetValue(relic);
            if (hoverTip is null)
            {
                return Missing(relicKey, title, "RuntimeRelicTooltipHoverTipMissing");
            }

            string? runtimeTitle = _hoverTipTitleProperty.GetValue(hoverTip) as string;
            string? runtimeDescription = _hoverTipDescriptionProperty.GetValue(hoverTip) as string;
            if (!string.IsNullOrWhiteSpace(runtimeTitle))
            {
                string plainTitle = ToPlainText(runtimeTitle).Trim();
                if (plainTitle.Length > 0)
                {
                    title = plainTitle;
                }
            }

            string description = ToPlainText(runtimeDescription ?? string.Empty).Trim();
            return new RelicTooltipSnapshot(
                relicKey,
                title,
                description,
                description.Length > 0,
                description.Length > 0
                    ? "RuntimeRelicTooltipHoverTipResolved"
                    : "RuntimeRelicTooltipDescriptionEmpty");
        }
        catch (Exception ex)
        {
            return Missing(relicKey, title, "RuntimeRelicTooltipResolveFailed:" + ex.GetType().Name);
        }
    }

    private static RelicTooltipSnapshot Missing(ModelKey key, string title, string evidence) =>
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
