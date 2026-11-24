using Godot;
using System.Collections;
using System.Reflection;
using System.Numerics;

internal static class MapRouteInteractionChecks
{
    internal static async Task Run(Node host, Control workspace, object[] maps, Texture2D? boss, Texture2D? ancient, string output, string language)
    {
        object? Call(object obj, string name, params object?[] args) => obj.GetType().GetMethod(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(obj,args);
        object? Property(object obj,string name) => obj.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.GetValue(obj);
        string PathKey(object? path) => path is IEnumerable values ? string.Join(";",values.Cast<object>()) : string.Empty;
        void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
        void Press(string name) => ((Button)workspace.FindChild(name,true,false)).EmitSignal(Button.SignalName.Pressed);
        var view = (Control)workspace.FindChild("MapView",true,false);
        var assembly = workspace.GetType().Assembly;
        foreach (string room in new[] { "Event", "Monster", "Shop", "Treasure" })
        {
            string asset = MegaCrit.Sts2.Core.Helpers.ImageHelper.GetRoomIconPath(MegaCrit.Sts2.Core.Map.MapPointType.Unknown,
                Enum.Parse<MegaCrit.Sts2.Core.Rooms.RoomType>(room), null)!;
            Check(ResourceLoader.Load<Texture2D>(asset) is { } texture && texture.GetWidth() > 0, "Official room art unavailable: " + asset);
        }
        object? Preview() => Property(workspace,"PreviewRoute");
        object? Commitment() => Property(workspace,"CurrentCommitment");
        int Overlays() => (int)Property(view,"ResolvedUnknownCount")!;
        object Sequence() => Property(workspace,"DisplayedUnknownSequence")!;
        bool RouteBound() => (bool)Property(Sequence(),"RouteBound")!;
        void CheckOverlays()
        {
            int count = ((IEnumerable)Property(Sequence(),"Steps")!).Cast<object>().Count();
            Check(RouteBound() && Overlays()==count,"Committed Unknown overlay count mismatch");
            var expected = ((IEnumerable)Property(Sequence(),"Steps")!).Cast<object>().Select(step =>
            {
                object position = Property(step,"Position")!;
                return "ResolvedUnknown_" + Property(position,"Column") + "_" + Property(position,"Row");
            }).ToHashSet();
            var actual = view.GetChildren().OfType<TextureRect>().Where(t=>t.Name.ToString().StartsWith("ResolvedUnknown_")).Select(t=>t.Name.ToString()).ToHashSet();
            Check(expected.SetEquals(actual),"Unknown outcomes remained on old route coordinates");
            foreach(var icon in view.GetChildren().OfType<TextureRect>().Where(t=>t.Name.ToString().StartsWith("ResolvedUnknown_")))
                Check(icon.Texture is not null && icon.Texture.ResourcePath.Contains("run_history") && !string.IsNullOrEmpty(icon.TooltipText),"Official resolved-room texture/tooltip missing");
        }
        var set = Activator.CreateInstance(assembly.GetType("RolltheSpire2.Core.Prediction.Maps.MapRouteSet")!, maps[0],null,false)!;
        async Task Frame() => await host.ToSignal(host.GetTree(),SceneTree.SignalName.ProcessFrame);
        async Task Screenshot(string name)
        {
            await Frame(); await host.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            using var image=host.GetViewport().GetTexture().GetImage(); image.SavePng(System.IO.Path.Combine(output,name+"-"+language+".png"));
        }
        Godot.Vector2 Position(double x,double row)
        {
            object sample=Activator.CreateInstance(assembly.GetType("RolltheSpire2.Core.Prediction.Maps.MapStrokePoint")!,x,row)!;
            return view.GetGlobalTransformWithCanvas() * (Godot.Vector2)Call(view,"StrokePosition",sample)!;
        }
        void Mouse(InputEvent input) => host.GetViewport().PushInput(input);
        async Task Draw(double x,bool reverse,bool screenshot=false,bool diagonal=false)
        {
            double first=reverse?16:0, last=reverse?0:16;
            var start=Position(x,first);
            // Begin inside the canvas, then allow the gesture/release to leave it.
            start.X=Math.Clamp(start.X,view.GlobalPosition.X+2,view.GlobalPosition.X+view.Size.X-2);
            Mouse(new InputEventMouseMotion { Position=start }); await Frame();
            Mouse(new InputEventMouseButton { Position=start, ButtonIndex=MouseButton.Right,Pressed=true,ButtonMask=MouseButtonMask.Right });
            Check((bool)Property(view,"IsDrawing")!,"Right press did not enter canvas drawing");
            for(int i=1;i<=12;i++)
            {
                var p=Position(diagonal ? x+i*.8 : x,first+(last-first)*i/12);
                Mouse(new InputEventMouseMotion { Position=p, ButtonMask=MouseButtonMask.Right }); await Frame();
            }
            if(screenshot) await Screenshot("route-stroke");
            Mouse(new InputEventMouseButton { Position=Position(diagonal?x+9.6:x,last),ButtonIndex=MouseButton.Right,Pressed=false });
            await Frame();
            Check(Preview() is not null && (bool)Call(set,"IsLegal",Preview())!,"Stroke did not create legal full preview");
            Check(Commitment() is null,"Stroke silently committed route");
            if(screenshot) await Screenshot("route-preview");
        }
        Check(!RouteBound() && Overlays()==0,"Baseline was applied as actual nodes");
        await Screenshot("unknown-baseline");
        // A current Act 2 commitment alone cannot establish the run-global RNG cursor.
        Call(workspace,"SelectAct",2); Call(workspace,"ShowMap",maps[1],new Texture2D?[]{boss,boss},ancient);
        Press("RouteExtremumUnknown"); Press("ConfirmRoute");
        Check(!RouteBound() && Overlays()==0,"Missing prior Act silently assumed zero RNG calls");
        await Screenshot("unknown-missing-prior");
        Press("ClearRoute"); Call(workspace,"SelectAct",1); Call(workspace,"ShowMap",maps[0],new Texture2D?[]{boss,boss},ancient);
        Mouse(new InputEventMouseButton { Position=Position(3,4),ButtonIndex=MouseButton.Left,Pressed=true });
        Mouse(new InputEventMouseButton { Position=Position(3,4),ButtonIndex=MouseButton.Left,Pressed=false });
        Check(Preview() is null && Commitment() is null,"Left click authored a route");
        foreach(double column in new[]{0d,3d,6d})
        {
            await Draw(column,false,column==3); string forward=PathKey(Preview()); Press("ClearRoute");
            await Draw(column,true); Check(forward==PathKey(Preview()),"Reversed UI stroke differs"); Press("ClearRoute");
        }
        await Draw(-2,false,diagonal:true); Press("ClearRoute");
        // Cancel a gesture without leaving the Workspace.
        Mouse(new InputEventMouseButton {Position=Position(3,0),ButtonIndex=MouseButton.Right,Pressed=true});
        Mouse(new InputEventKey {Keycode=Key.Escape,Pressed=true}); await Frame();
        Check(workspace.Visible && !(bool)Property(view,"IsDrawing")!,"Escape during stroke should cancel only gesture");
        Mouse(new InputEventMouseButton {Position=Position(3,0),ButtonIndex=MouseButton.Right,Pressed=true});
        host.GetWindow().EmitSignal(Window.SignalName.FocusExited);
        Check(!(bool)Property(view,"IsDrawing")!,"Lost window focus retained right drag");
        bool multiple=false;
        foreach(string metric in new[]{"Unknown","Elite","RestSite","Shop","Monster"})
        {
            Press("RouteExtremum"+metric);
            var optimal=workspace.GetType().GetField("_optimalRoutes",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(workspace)!;
            Check((bool)Call(optimal,"IsLegal",Preview())! && Commitment() is null,"Extremum auto-committed or invalid");
            if((BigInteger)Property(optimal,"Count")! <= 1) continue;
            multiple=true; string first=PathKey(Preview());
            await Screenshot("route-extrema");
            Press("NextRoute"); Check(first!=PathKey(Preview()),"Next did not browse distinct optimum");
            Press("PreviousRoute"); Check(first==PathKey(Preview()),"Previous order not stable");
            await Draw(6,false); Check((bool)Call(optimal,"IsLegal",Preview())!,"Optimal stroke left feasible set");
            string selected=PathKey(Preview()); Press("ConfirmRoute");
            Check(Preview() is null && selected==PathKey(Property(Commitment()!,"Coordinates")),"Explicit confirm did not commit current preview");
            CheckOverlays();
            await Screenshot("unknown-route");
            await Screenshot("route-committed");
            Call(workspace,"Close");
            Call(workspace,"Open",1,"000000067C0S",RolltheSpire2.Presentation.Localization.JsonUiTextProvider.Create(language),
                (Control)host.FindChild("MapPreviewButton",true,false));
            Call(workspace,"ShowMap",maps[0],new Texture2D?[]{boss,boss},ancient);
            Check(selected==PathKey(Property(Commitment()!,"Coordinates")),"Back/reopen lost commitment");
            Call(workspace,"SelectAct",2); Call(workspace,"ShowMap",maps[1],new Texture2D?[]{boss,boss},ancient);
            Check(Commitment() is null && Overlays()==0,"Act 1 selection leaked into Act 2");
            Press("RouteExtremumUnknown"); Press("ConfirmRoute"); CheckOverlays();
            Check((int)Property(Sequence(),"PriorDraws")!>0,"Act 2 RNG restarted");
            await Screenshot("unknown-act2-route");
            Call(workspace,"SelectAct",1); Call(workspace,"ShowMap",maps[0],new Texture2D?[]{boss,boss},ancient);
            Check(selected==PathKey(Property(Commitment()!,"Coordinates")),"Act switch lost committed route");
            Press("RouteExtremumUnknown"); Check(Overlays()==0,"Optimal preview retained previous outcomes");
            Press("NextRoute"); Check(Overlays()==0,"Browsing predicted an uncommitted route");
            Press("RedrawRoute"); Check(Overlays()==0,"Redraw retained stale outcomes");
            Check(selected==PathKey(Property(Commitment()!,"Coordinates")),"Redraw altered commitment");
            Press("ClearRoute"); Check(Commitment() is null && Preview() is null && Overlays()==0,"Explicit clear failed");
            break;
        }
        Check(multiple,"Fixture must exercise multiple optimal routes");
        Call(workspace,"ResetRouteContext"); Check(Commitment() is null,"New context retained route");
        Call(workspace,"ShowMap",maps[0],new Texture2D?[]{boss,boss},ancient);
        GD.Print("Map route native interaction PASS: "+language+"; right drag both directions/outside release, left isolation, preview/confirm, extrema rank, restricted drawing, Act isolation, clear.");
    }
}
