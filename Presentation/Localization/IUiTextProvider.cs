namespace RolltheSpire2.Presentation.Localization;

public interface IUiTextProvider
{
    string LanguageCode { get; }
    string Get(string key);
    string Format(string key, params object?[] args);
}
