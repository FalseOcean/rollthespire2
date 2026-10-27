namespace RolltheSpire2.Presentation.Localization;

public interface IUiTextProvider
{
    string LanguageCode { get; }
    bool TryGet(string key, out string value);
    string Get(string key);
    string Format(string key, params object?[] args);
}
