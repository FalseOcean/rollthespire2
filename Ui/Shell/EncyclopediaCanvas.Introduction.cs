using System.Text.RegularExpressions;
using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Icons;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class EncyclopediaCanvas
{
    // Authoring tokens: [[article-id|optional label]], [[seed:VISIBLE_SEED|optional label]],
    // {{relic:MODEL_ENTRY}}, {{card:MODEL_ENTRY}}, {{event:MODEL_ENTRY}},
    // {{act:MODEL_ENTRY}}, {{encounter:MODEL_ENTRY}}, and the -icon variants.
    private static readonly Regex InlineToken = new(
        @"\*\*(?<bold>.+?)\*\*|\[\[(?<article>[a-z0-9-]+)(?:\|(?<label>[^\]]+))?\]\]|\[\[seed:(?<seed>[A-Z0-9]+)(?:\|(?<seedLabel>[^\]]+))?\]\]|\{\{(?<kind>relic|card|event|act|encounter)(?<icon>-icon)?:(?<content>[A-Z0-9_]+)\}\}|`(?<code>[^`]+)`",
        RegexOptions.Compiled);

    private readonly HashSet<ModelKey> _mentionedRelics = [];

    private void RenderInline(RichTextLabel view, string text, bool firstRelicIcons = true)
    {
        int consumed = 0;
        foreach (Match match in InlineToken.Matches(text))
        {
            if (match.Index > consumed)
                view.AddText(text[consumed..match.Index]);
            if (match.Groups["bold"].Success)
            {
                view.PushColor(new Color("E2E7EC"));
                RenderInline(view, match.Groups["bold"].Value, firstRelicIcons);
                view.Pop();
            }
            else if (match.Groups["article"].Success)
            {
                string id = match.Groups["article"].Value;
                ArticleDefinition? target = Articles.FirstOrDefault(item => item.Id == id);
                if (target is null)
                    view.AddText(match.Value);
                else
                    AddArticleLink(view, id, match.Groups["label"].Success
                        ? match.Groups["label"].Value : ArticleName(target));
            }
            else if (match.Groups["seed"].Success)
            {
                string seed = match.Groups["seed"].Value;
                AddSeedLink(view, seed, match.Groups["seedLabel"].Success
                    ? match.Groups["seedLabel"].Value : seed);
            }
            else if (match.Groups["content"].Success)
            {
                var (kind, category) = match.Groups["kind"].Value switch
                {
                    "card" => (GameContentKind.Card, BaseGameModelKeys.Categories.Card),
                    "event" => (GameContentKind.Event, BaseGameModelKeys.Categories.Event),
                    "act" => (GameContentKind.Act, BaseGameModelKeys.Categories.Act),
                    "encounter" => (GameContentKind.Encounter, BaseGameModelKeys.Categories.Encounter),
                    _ => (GameContentKind.Relic, BaseGameModelKeys.Categories.Relic)
                };
                var key = new ModelKey(category, match.Groups["content"].Value);
                string name = _contentNames.Resolve(key, kind);
                bool firstRelic = firstRelicIcons && kind == GameContentKind.Relic && _mentionedRelics.Add(key);
                if (match.Groups["icon"].Success || firstRelic)
                {
                    IconDescriptor icon = _entryIcons.Resolve(key, kind, IconVariant.Small);
                    if (!icon.IsMissing && icon.Texture is not null)
                    {
                        view.AddImage(icon.Texture, 22, 22, tooltip: name);
                        view.AddText(" ");
                    }
                }
                view.AddText(name);
            }
            else if (match.Groups["code"].Success)
            {
                view.PushMono();
                view.PushColor(new Color("A7B8CB"));
                view.AddText(match.Groups["code"].Value);
                view.Pop();
                view.Pop();
            }
            consumed = match.Index + match.Length;
        }
        if (consumed < text.Length)
            view.AddText(text[consumed..]);
    }

    private static void AddArticleLink(RichTextLabel view, string id, string text)
    {
        view.PushMeta("article:" + id);
        view.PushColor(new Color("9CC9D8"));
        view.PushUnderline();
        view.AddText(text);
        view.Pop();
        view.Pop();
        view.Pop();
    }

    private static void AddSeedLink(RichTextLabel view, string seed, string text)
    {
        view.PushMeta("seed:" + seed);
        view.PushColor(new Color("9CC9D8"));
        view.PushUnderline();
        view.PushMono();
        view.AddText(text);
        view.Pop();
        view.Pop();
        view.Pop();
        view.Pop();
    }
}
