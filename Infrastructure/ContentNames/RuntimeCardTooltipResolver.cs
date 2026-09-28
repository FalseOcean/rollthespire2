using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Infrastructure.ContentNames;

/// <summary>
/// Main-thread-only controlled adapter for canonical CardModel tooltip text.
/// The card title comes from CardModel.Title and rules text comes exclusively
/// from CardModel.GetDescriptionForPile(PileType.None, null). HoverTip and
/// HoverTips are deliberately not reflected or read because they contain
/// glossary/keyword explanations rather than the card's rules text.
/// </summary>
internal sealed class RuntimeCardTooltipResolver : ICardTooltipResolver
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;
    private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;
    private const string RulesTextFormatPlain = "PlainTextFromOfficialRules";

    private static readonly Regex PairedImageTag = new(
        @"\[img(?:=[^\]]*)?\].*?\[/img\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex StandaloneImageTag = new(
        @"\[img(?:=[^\]]*)?\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex BreakTag = new(
        @"\[(?:br|p)\s*/?\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AnyBbCodeTag = new(
        @"\[(?:/?)[a-z][^\]]*\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ExcessBlankLines = new(
        @"\n{3,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Assembly? _gameAssembly;
    private readonly Type? _modelIdType;
    private readonly Type? _cardModelType;
    private readonly Type? _pileType;
    private readonly Type? _creatureType;
    private readonly Type? _characterModelType;
    private readonly Type? _cardPoolModelType;
    private readonly ConstructorInfo? _modelIdConstructor;
    private readonly MethodInfo? _getCardById;
    private readonly MethodInfo? _getDescriptionForPile;
    private readonly MethodInfo? _cardTypeToLocString;
    private readonly MethodInfo? _cardRarityToLocString;
    private readonly PropertyInfo? _titleProperty;
    private readonly PropertyInfo? _cardTypeProperty;
    private readonly PropertyInfo? _rarityProperty;
    private readonly PropertyInfo? _poolProperty;
    private readonly PropertyInfo? _allCharactersProperty;
    private readonly PropertyInfo? _characterCardPoolProperty;
    private readonly PropertyInfo? _characterTitleProperty;
    private readonly PropertyInfo? _poolIsColorlessProperty;
    private readonly PropertyInfo? _poolTitleProperty;
    private readonly object? _pileTypeNone;
    private readonly HashSet<string> _loggedDiagnostics = new(StringComparer.Ordinal);

    public RuntimeCardTooltipResolver()
    {
        try
        {
            _gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .SingleOrDefault(assembly => string.Equals(
                    assembly.GetName().Name,
                    "sts2",
                    StringComparison.OrdinalIgnoreCase));
            Type? modelDbType = _gameAssembly?.GetType(
                "MegaCrit.Sts2.Core.Models.ModelDb",
                throwOnError: false,
                ignoreCase: false);
            _modelIdType = _gameAssembly?.GetType(
                "MegaCrit.Sts2.Core.Models.ModelId",
                throwOnError: false,
                ignoreCase: false);
            _cardModelType = _gameAssembly?.GetType(
                "MegaCrit.Sts2.Core.Models.CardModel",
                throwOnError: false,
                ignoreCase: false);
            _pileType = _gameAssembly?.GetType(
                "MegaCrit.Sts2.Core.Entities.Cards.PileType",
                throwOnError: false,
                ignoreCase: false);
            _creatureType = _gameAssembly?.GetType(
                "MegaCrit.Sts2.Core.Entities.Creatures.Creature",
                throwOnError: false,
                ignoreCase: false);
            _characterModelType = _gameAssembly?.GetType(
                "MegaCrit.Sts2.Core.Models.CharacterModel",
                throwOnError: false,
                ignoreCase: false);
            _cardPoolModelType = _gameAssembly?.GetType(
                "MegaCrit.Sts2.Core.Models.CardPoolModel",
                throwOnError: false,
                ignoreCase: false);

            _modelIdConstructor = _modelIdType?.GetConstructor(new[] { typeof(string), typeof(string) });
            _getCardById = FindExactGenericModelLookup(modelDbType, _modelIdType, _cardModelType);

            _titleProperty = _cardModelType?.GetProperty("Title", PublicInstance);
            _cardTypeProperty = _cardModelType?.GetProperty("Type", PublicInstance);
            _rarityProperty = _cardModelType?.GetProperty("Rarity", PublicInstance);
            _poolProperty = _cardModelType?.GetProperty("Pool", PublicInstance);

            if (_cardModelType is not null && _pileType is not null && _creatureType is not null)
            {
                _getDescriptionForPile = FindExactInstanceMethod(
                    _cardModelType,
                    "GetDescriptionForPile",
                    typeof(string),
                    _pileType,
                    _creatureType);
            }

            if (_pileType?.IsEnum == true && Enum.GetNames(_pileType).Contains("None", StringComparer.Ordinal))
            {
                _pileTypeNone = Enum.Parse(_pileType, "None", ignoreCase: false);
            }

            if (_cardTypeProperty is not null)
            {
                Type? extensions = _gameAssembly?.GetType(
                    "MegaCrit.Sts2.Core.Entities.Cards.CardTypeExtensions",
                    throwOnError: false,
                    ignoreCase: false);
                _cardTypeToLocString = FindExactStaticMethod(
                    extensions,
                    "ToLocString",
                    _cardTypeProperty.PropertyType);
            }

            if (_rarityProperty is not null)
            {
                Type? extensions = _gameAssembly?.GetType(
                    "MegaCrit.Sts2.Core.Entities.Cards.CardRarityExtensions",
                    throwOnError: false,
                    ignoreCase: false);
                _cardRarityToLocString = FindExactStaticMethod(
                    extensions,
                    "ToLocString",
                    _rarityProperty.PropertyType);
            }

            _allCharactersProperty = modelDbType?.GetProperty("AllCharacters", PublicStatic);
            _characterCardPoolProperty = _characterModelType?.GetProperty("CardPool", PublicInstance);
            _characterTitleProperty = _characterModelType?.GetProperty("Title", PublicInstance);
            _poolIsColorlessProperty = _cardPoolModelType?.GetProperty("IsColorless", PublicInstance);
            _poolTitleProperty = _cardPoolModelType?.GetProperty("Title", PublicInstance);
        }
        catch (Exception ex)
        {
            LogDiagnosticOnce("resolver-init", "CardRulesTextAbiUnavailable:" + ex.GetType().Name);
        }
    }

    public CardTooltipSnapshot Resolve(ModelKey cardKey, string fallbackTitle)
    {
        string fallback = string.IsNullOrWhiteSpace(fallbackTitle)
            ? cardKey.Entry
            : fallbackTitle.Trim();

        if (!RuntimeSnapshotThreadGuard.IsMainThread)
        {
            return Missing(cardKey, fallback, "RuntimeCardTooltipRequiresMainThread");
        }
        if (!cardKey.IsValid)
        {
            return Missing(cardKey, fallback, "RuntimeCardTooltipInvalidModelKey");
        }
        if (_modelIdConstructor is null ||
            _getCardById is null ||
            _cardModelType is null)
        {
            return Missing(cardKey, fallback, "RuntimeCardTooltipAbiUnavailable");
        }

        try
        {
            object modelId = _modelIdConstructor.Invoke(new object[] { cardKey.Category, cardKey.Entry });
            object? card = _getCardById.Invoke(null, new[] { modelId });
            if (card is null || !_cardModelType.IsInstanceOfType(card))
            {
                return Missing(cardKey, fallback, "RuntimeCardTooltipModelNotFound");
            }

            string displayName = ReadOfficialText(_titleProperty?.GetValue(card));
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = fallback;
            }

            string rarityName = ReadLocalizedEnum(_rarityProperty?.GetValue(card), _cardRarityToLocString);
            string cardTypeName = ReadLocalizedEnum(_cardTypeProperty?.GetValue(card), _cardTypeToLocString);
            string poolEvidence;
            string poolName = ResolvePoolOrCharacterName(card, out poolEvidence);

            (string rulesText, bool exact, string rulesEvidence) = ResolveRulesText(card);
            string evidence = JoinEvidence(rulesEvidence, poolEvidence, "KeywordHoverTipRejected");
            if (!exact || !string.IsNullOrWhiteSpace(poolEvidence))
            {
                LogDiagnosticOnce(cardKey.Serialized, evidence);
            }

            return new CardTooltipSnapshot(
                cardKey,
                displayName,
                poolName,
                rarityName,
                cardTypeName,
                rulesText,
                exact,
                evidence,
                exact ? RulesTextFormatPlain : string.Empty);
        }
        catch (Exception ex)
        {
            string evidence = "RuntimeCardTooltipResolveFailed:" + UnwrapExceptionName(ex);
            LogDiagnosticOnce(cardKey.Serialized, evidence);
            return Missing(cardKey, fallback, evidence);
        }
    }

    private (string RulesText, bool Exact, string Evidence) ResolveRulesText(object card)
    {
        if (_getDescriptionForPile is null || _pileTypeNone is null)
        {
            return (string.Empty, false, "CardRulesTextAbiUnavailable");
        }

        // The ModelDb lookup above returns the canonical CardModel. Do not read
        // CardModel.Owner as a live-state probe here: canonical models deliberately
        // reject that access with CanonicalModelException. The official formatter
        // invocation below remains isolated and fail-closed per card.

        // A mod subclass can execute arbitrary virtual CanonicalVars or
        // AddExtraArgsToDescription code. Without a source audit, fail closed.
        if (_gameAssembly is null || card.GetType().Assembly != _gameAssembly)
        {
            return (string.Empty, false, "CardRulesTextModHookUnknown");
        }

        try
        {
            object? value = _getDescriptionForPile.Invoke(card, new[] { _pileTypeNone, null });
            string plain = ToPlainOfficialRules(value as string ?? string.Empty).Trim();
            if (plain.Length == 0)
            {
                return (string.Empty, false, "CardRulesTextEmpty");
            }

            return (plain, true, "CardModel.GetDescriptionForPile");
        }
        catch (TargetInvocationException ex)
        {
            return (string.Empty, false, "CardRulesTextFormatterFailed:" +
                (ex.InnerException?.GetType().Name ?? ex.GetType().Name));
        }
        catch (Exception ex)
        {
            return (string.Empty, false, "CardRulesTextFormatterFailed:" + ex.GetType().Name);
        }
    }

    private string ResolvePoolOrCharacterName(object card, out string evidence)
    {
        evidence = string.Empty;
        object? pool;
        try
        {
            pool = _poolProperty?.GetValue(card);
        }
        catch (Exception ex)
        {
            evidence = "CardPoolDisplayNameUnknown:" + ex.GetType().Name;
            return string.Empty;
        }

        if (pool is null)
        {
            evidence = "CardPoolDisplayNameUnknown:MissingPool";
            return string.Empty;
        }

        if (_allCharactersProperty?.GetValue(null) is IEnumerable characters &&
            _characterModelType is not null &&
            _characterCardPoolProperty is not null &&
            _characterTitleProperty is not null)
        {
            foreach (object? character in characters)
            {
                if (character is null || !_characterModelType.IsInstanceOfType(character))
                {
                    continue;
                }

                object? characterPool;
                try
                {
                    characterPool = _characterCardPoolProperty.GetValue(character);
                }
                catch
                {
                    continue;
                }

                if (!ReferenceEquals(characterPool, pool) && !Equals(characterPool, pool))
                {
                    continue;
                }

                string characterName = ReadOfficialText(_characterTitleProperty.GetValue(character));
                if (!string.IsNullOrWhiteSpace(characterName))
                {
                    return characterName;
                }
            }
        }

        try
        {
            if (_poolIsColorlessProperty?.GetValue(pool) is true)
            {
                // IsColorless is the runtime authority. This display fallback is
                // deliberately not inferred from a ModelKey, icon or type name.
                evidence = string.Empty;
                return IsChineseLocale() ? "无色" : "Colorless";
            }
        }
        catch
        {
            // Continue to controlled pool-name fallback below.
        }

        string fallback = ReadOfficialText(_poolTitleProperty?.GetValue(pool));
        if (string.IsNullOrWhiteSpace(fallback))
        {
            fallback = pool.GetType().Name;
        }
        evidence = "CardPoolDisplayNameUnknown";
        return fallback;
    }

    private static string ReadLocalizedEnum(object? value, MethodInfo? toLocString)
    {
        if (value is null || toLocString is null)
        {
            return string.Empty;
        }

        try
        {
            object? locString = toLocString.Invoke(null, new[] { value });
            return ReadOfficialText(locString);
        }
        catch
        {
            // Do not expose enum English names as localized UI text.
            return string.Empty;
        }
    }

    private static string ReadOfficialText(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }
        if (value is string text)
        {
            return ToPlainOfficialRules(text).Trim();
        }

        MethodInfo? formatted = value.GetType().GetMethod(
            "GetFormattedText",
            PublicInstance,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        if (formatted?.ReturnType != typeof(string))
        {
            return string.Empty;
        }

        return ToPlainOfficialRules(formatted.Invoke(value, null) as string ?? string.Empty).Trim();
    }

    private static MethodInfo? FindExactGenericModelLookup(
        Type? modelDbType,
        Type? modelIdType,
        Type? modelType)
    {
        if (modelDbType is null || modelIdType is null || modelType is null)
        {
            return null;
        }

        MethodInfo[] candidates = modelDbType
            .GetMethods(PublicStatic)
            .Where(method =>
            {
                if (!string.Equals(method.Name, "GetByIdOrNull", StringComparison.Ordinal) ||
                    !method.IsGenericMethodDefinition ||
                    method.GetGenericArguments().Length != 1)
                {
                    return false;
                }

                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType == modelIdType;
            })
            .ToArray();
        return candidates.Length == 1 ? candidates[0].MakeGenericMethod(modelType) : null;
    }

    private static MethodInfo? FindExactInstanceMethod(
        Type owner,
        string name,
        Type returnType,
        params Type[] parameterTypes)
    {
        MethodInfo[] candidates = owner
            .GetMethods(PublicInstance)
            .Where(method =>
                string.Equals(method.Name, name, StringComparison.Ordinal) &&
                !method.IsGenericMethod &&
                method.ReturnType == returnType &&
                ParametersEqual(method.GetParameters(), parameterTypes))
            .ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static MethodInfo? FindExactStaticMethod(
        Type? owner,
        string name,
        params Type[] parameterTypes)
    {
        if (owner is null)
        {
            return null;
        }

        MethodInfo[] candidates = owner
            .GetMethods(PublicStatic)
            .Where(method =>
                string.Equals(method.Name, name, StringComparison.Ordinal) &&
                !method.IsGenericMethod &&
                ParametersEqual(method.GetParameters(), parameterTypes))
            .ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static bool ParametersEqual(ParameterInfo[] parameters, Type[] expected)
    {
        if (parameters.Length != expected.Length)
        {
            return false;
        }
        for (int index = 0; index < parameters.Length; index++)
        {
            if (parameters[index].ParameterType != expected[index])
            {
                return false;
            }
        }
        return true;
    }

    private static string ToPlainOfficialRules(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        string text = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        text = BreakTag.Replace(text, "\n");
        text = PairedImageTag.Replace(text, string.Empty);
        text = StandaloneImageTag.Replace(text, string.Empty);
        text = AnyBbCodeTag.Replace(text, string.Empty);
        text = ExcessBlankLines.Replace(text, "\n\n");
        return text;
    }

    private static string JoinEvidence(params string[] values) => string.Join(
        ";",
        values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal));

    private static string UnwrapExceptionName(Exception exception) =>
        exception is TargetInvocationException { InnerException: Exception inner }
            ? inner.GetType().Name
            : exception.GetType().Name;

    private static bool IsChineseLocale()
    {
        try
        {
            string locale = TranslationServer.GetLocale();
            return locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void LogDiagnosticOnce(string key, string evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence))
        {
            return;
        }

        string fingerprint = key + "|" + evidence;
        if (_loggedDiagnostics.Add(fingerprint))
        {
            RuntimeLog.Warn($"cardPickerTooltipDiagnostic={evidence};card={key}");
        }
    }

    private CardTooltipSnapshot Missing(ModelKey key, string title, string evidence)
    {
        string combined = JoinEvidence(evidence, "KeywordHoverTipRejected");
        LogDiagnosticOnce(key.IsValid ? key.Serialized : title, combined);
        return new CardTooltipSnapshot(
            key,
            title,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            false,
            combined,
            string.Empty);
    }
}
