using Godot;
using System.Reflection;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Ui.Icons;

// Presentation only: render the game's visual scenes once, without creating a
// Creature, combat state, RNG or invoking SetUpForCombat/monster effects.
internal sealed partial class EncounterPortraitProvider : Node
{
    private static readonly PropertyInfo VisualsPath = typeof(MonsterModel).GetProperty("VisualsPath",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
    private readonly Dictionary<ModelKey, Task<Texture2D?>> _portraits = [];
    private Task<Texture2D?> _pending = Task.FromResult<Texture2D?>(null);
    private bool _closed;

    internal Task<Texture2D?> Get(ModelKey encounter)
    {
        if (_portraits.TryGetValue(encounter, out var portrait)) return portrait;
        portrait = RenderAfter(_pending, encounter);
        _pending = portrait;
        _portraits[encounter] = portrait;
        return portrait;
    }

    private async Task<Texture2D?> RenderAfter(Task<Texture2D?> previous, ModelKey encounter)
    {
        await previous;
        if (_closed || !IsInsideTree()) return null;
        SubViewport? viewport = null;
        try
        {
            var model = ModelDb.GetById<EncounterModel>(new ModelId(encounter.Category, encounter.Entry));
            var monsters = model.AllPossibleMonsters.DistinctBy(m => m.Id).Take(3).ToArray();
            if (monsters.Length == 0) return null;
            viewport = new SubViewport
            {
                Size = new Vector2I(144, 112), TransparentBg = true, Disable3D = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                GuiDisableInput = true, HandleInputLocally = false
            };
            AddChild(viewport);
            float cellWidth = 144f / monsters.Length;
            for (int i = 0; i < monsters.Length; i++)
            {
                // CreateVisuals uses this same protected resource getter. Loading
                // it directly avoids that method's combat-owned error fallback.
                var path = (string)VisualsPath.GetValue(monsters[i])!;
                var scene = ResourceLoader.Load<PackedScene>(path);
                var visual = scene.Instantiate<NCreatureVisuals>();
                visual.ProcessMode = ProcessModeEnum.Disabled;
                viewport.AddChild(visual);
                // Spine initializes its drawable pose on a frame update. Keep
                // only the body active; no gameplay or surrounding VFX runs.
                visual.GetCurrentBody().ProcessMode = ProcessModeEnum.Always;
                PreparePortraitPose(visual);
                var bounds = visual.Bounds;
                var rect = (visual.GlobalTransform.AffineInverse() * bounds.GetGlobalTransform())
                    * new Rect2(Vector2.Zero, bounds.Size);
                float scale = Math.Min((cellWidth - 6) / Math.Max(1, rect.Size.X), 102 / Math.Max(1, rect.Size.Y));
                visual.Scale = Vector2.One * scale;
                visual.Position = new Vector2(cellWidth * (i + .5f), 56) - rect.GetCenter() * scale;
            }
            // A queued render may begin in the previous viewport's PostDraw
            // continuation. Wait for a new frame before capturing this one.
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_closed || !IsInstanceValid(viewport)) return null;
            viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (_closed || !IsInstanceValid(viewport)) return null;
            using var rendered = viewport.GetTexture().GetImage();
            var used = rendered.GetUsedRect();
            if (used.Size == Vector2I.Zero) throw new InvalidOperationException("Empty monster portrait");
            using var cropped = rendered.GetRegion(used);
            return ImageTexture.CreateFromImage(cropped);
        }
        catch (Exception ex)
        {
            RuntimeLog.Detail($"encounterPortraitUnavailable={encounter.Serialized};issue={ex.Message}");
            return null;
        }
        finally
        {
            if (viewport is not null && IsInstanceValid(viewport)) viewport.QueueFree();
        }
    }

    private static void PreparePortraitPose(Node node)
    {
        // Some scenes also contain SpineSprite-based VFX controllers with no
        // skeleton resource; only authored drawable sprites need a pose.
        if (node.IsClass("SpineSprite") && node.Get("skeleton_data_res").AsGodotObject() is not null)
        {
            var spine = new MegaSprite(node);
            if (spine.GetSkeleton() is { } skeleton)
            {
                // Scene preview skins are authored visual defaults, not applied
                // by Spine at runtime. Use them without calling SetupSkins:
                // some monsters (e.g. ScrollOfBiting) consume Rng.Chaotic there.
                string skin = node.Get("preview_skin").AsString();
                if (!string.IsNullOrEmpty(skin)) skeleton.SetSkinByName(skin);
                skeleton.SetSlotsToSetupPose();
                string previewAnimation = node.Get("preview_animation").AsString();
                var animation = new[] { "idle_loop", "idle", "Idle", previewAnimation }
                    .FirstOrDefault(name => !string.IsNullOrEmpty(name) && spine.HasAnimation(name));
                if (animation is not null)
                {
                    var state = spine.GetAnimationState();
                    state.SetAnimation(animation, true, 0);
                    state.SetTimeScale(0);
                    state.Apply(skeleton);
                }
            }
        }
        // Includes authored attachments such as BowlbugEgg's carried cocoon.
        foreach (var child in node.GetChildren()) PreparePortraitPose(child);
    }

    public override void _ExitTree()
    {
        _closed = true;
        _portraits.Clear();
    }
}
