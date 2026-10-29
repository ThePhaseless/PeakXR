using System;
using System.Collections;
using System.Collections.Generic;
using PeakXR.Core;
using PeakXR.UI.Guide;
using PeakXR.VR;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PeakXR.Sandbox;

/// <summary>01b: the item room's props are drawn lit, like the level; 01c: the full room is laid out as a display.</summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>
    /// 01b: one item room table (<see cref="RoomProps.Table"/>) 2 m ahead and the sandbox test wall past it, then every
    /// mesh on both must use a supported URP/Lit or game (<c>W/</c>) shader (<see cref="RoomProps.Unlit"/>). The room
    /// drew its tables with the unlit UI colour shader: flat slabs a tester took for white blocks (v0.1.1).
    /// </summary>
    IEnumerator RoomPropsCheck()
    {
        var c = Character.localCharacter;
        var forward = Flat(c.data.lookDirection);
        roomProps = new GameObject("PeakXR_SelfTestRoomProps");
        var top = RoomProps.Lit(new Color(0.85f, 0.55f, 0.25f));
        var legs = RoomProps.Lit(new Color(0.18f, 0.18f, 0.2f));
        roomPropMaterials = new[] { top, legs };
        RoomProps.Table(
            roomProps.transform,
            c.data.groundPos + forward * 2f,
            Quaternion.LookRotation(forward),
            1.6f,
            0.8f,
            0.885f,
            top,
            legs
        );
        var wall = SandboxActions.PlaceWall(c.data.groundPos + forward * 4.5f, forward);
        yield return Wait(0.5f);
        yield return Shot("01b-item-room-props");
        var table = RoomProps.Unlit(roomProps.transform);
        var wallNote = wall != null ? RoomProps.Unlit(wall.transform) : "no test wall placed";
        stepNote = F($"table: {table ?? "lit"}; wall: {wallNote ?? "lit"}");
        if (table != null || wallNote != null)
            Fail(stepNote);
    }

    GameObject roomProps;
    Material[] roomPropMaterials;

    /// <summary>After 01b whatever its verdict: a timed-out step is dropped without running its end.</summary>
    void EndRoomProps()
    {
        if (roomProps != null)
            Object.Destroy(roomProps);
        roomProps = null;
        if (roomPropMaterials != null)
            foreach (var m in roomPropMaterials)
                Object.Destroy(m);
        roomPropMaterials = null;
        SandboxActions.RemoveWall();
    }

    // ---------------------------------------------------------------- 01c: the full item room

    /// <summary>The item room's own clearances: legs reach the floor within this, items rest within this over the top.</summary>
    const float RoomTolerance = 0.02f;

    /// <summary>
    /// 01c: the full item room as a player opens it (<see cref="ItemRoom.Toggle"/>, the fixed airport place), every
    /// table's items kept out (<see cref="ItemRoom.KeepAllLoaded"/>). Each table: a top at least 5 cm thick whose surface
    /// is where the room says, and at least four enabled legs from the floor (within 2 cm) to under the top. Each item:
    /// its drawn box (<see cref="ItemRoom.BoxOf"/>, in the room's frame) never below its table's top surface, at most
    /// 2 cm over it, inside the top's edges, and at least 10 cm from every other item on that table. Each orb: its
    /// glyph drawn, on a post from the floor to just under it, its label shown over it, every orb at one height. No
    /// world text in the room is a lone letter, and in VR no game key glyph (<c>InputIcon</c>) is drawn. Every mesh is
    /// lit (<see cref="RoomProps.Unlit"/>). Screenshots from the entrance and over the fullest table. Testers saw items
    /// sunk into 4 cm planks, legs that did not read, a floating "E" and unexplained circles (v0.1.1).
    /// </summary>
    IEnumerator RoomLayoutCheck()
    {
        var c = Character.localCharacter;
        ItemRoom.Toggle(c.Center, false);
        var room = ItemRoom.Current;
        if (room == null)
        {
            Fail("the item room did not open (see the refusal in the log)");
            yield break;
        }
        room.KeepAllLoaded = true;
        float until = Now + 75f;
        while (ItemRoom.Current == room && room.Placing && Now < until)
            yield return null;
        if (ItemRoom.Current != room || room.Placing)
        {
            Fail(
                ItemRoom.Current != room
                    ? "the item room closed while building"
                    : "items still placing after 75 s"
            );
            yield break;
        }
        // Their Start and first draw.
        yield return Wait(1f);

        var plan = room.Layout;
        var frame = Matrix4x4.Rotate(Quaternion.Inverse(room.Facing));
        var problems = new List<string>();
        void Problem(string p)
        {
            if (problems.Count < 6)
                problems.Add(p);
        }
        int missing = 0;
        int items = 0;
        float minGap = float.MaxValue;
        float minThick = float.MaxValue;
        int fullest = 0;
        var area = new Bounds(room.Entrance, Vector3.zero);
        for (int t = 0; t < plan.Tables.Length; t++)
        {
            var table = room.TableRoots[t];
            area.Encapsulate(table.position);
            if (plan.Tables[t].Count > plan.Tables[fullest].Count)
                fullest = t;
            string what =
                $"table {t} ({ItemShelf.Title(ShelfLayout.GroupOf(plan.Tables[t].Group))})";
            MeshRenderer top = null;
            int legs = 0;
            foreach (var r in table.GetComponentsInChildren<MeshRenderer>(true))
                if (r.name == "PeakXR_ItemRoomTableTop")
                    top = r;
            if (top == null || !top.enabled || !top.gameObject.activeInHierarchy)
            {
                Problem($"{what}: no top drawn");
                continue;
            }
            float surface = room.TopOf(t);
            var topBox = top.bounds;
            minThick = Mathf.Min(minThick, topBox.size.y);
            if (topBox.size.y < 0.05f)
                Problem(F($"{what}: top {topBox.size.y * 100f:0.0} cm thick"));
            if (Mathf.Abs(topBox.max.y - surface) > 0.005f)
                Problem(F($"{what}: top surface {topBox.max.y - surface:+0.000;-0.000} m off"));
            foreach (var r in table.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.name != "PeakXR_ItemRoomTableLeg")
                    continue;
                var leg = r.bounds;
                if (!r.enabled || !r.gameObject.activeInHierarchy)
                    Problem($"{what}: a leg not drawn");
                else if (!Terrain(leg.center, Vector3.down, 3f, out var floor))
                    Problem($"{what}: no floor under a leg");
                else if (Mathf.Abs(leg.min.y - floor.point.y) > RoomTolerance)
                    Problem(
                        F(
                            $"{what}: a leg ends {leg.min.y - floor.point.y:+0.00;-0.00} m off the floor"
                        )
                    );
                else if (leg.max.y < topBox.min.y - 0.01f)
                    Problem(
                        F($"{what}: a leg stops {topBox.min.y - leg.max.y:0.00} m under the top")
                    );
                else
                    legs++;
            }
            if (legs < 4)
                Problem($"{what}: {legs} legs reach from the floor to the top");

            ItemRoom.Drawn(top.transform, frame, out var topFrame);
            var boxes = new List<KeyValuePair<int, Bounds>>();
            for (int k = plan.Tables[t].First; k < plan.Tables[t].First + plan.Tables[t].Count; k++)
            {
                var go = room.Placed[k];
                if (go == null || !room.BoxOf(k, out var b))
                {
                    missing++;
                    continue;
                }
                items++;
                string item = $"{what}: {go.name.Replace("(Clone)", "", StringComparison.Ordinal)}";
                if (b.min.y < surface - 0.002f)
                    Problem(F($"{item} {(surface - b.min.y) * 100f:0.0} cm into the top"));
                else if (b.min.y > surface + RoomTolerance)
                    Problem(F($"{item} floats {(b.min.y - surface) * 100f:0.0} cm over the top"));
                if (
                    b.min.x < topFrame.min.x - 0.005f
                    || b.max.x > topFrame.max.x + 0.005f
                    || b.min.z < topFrame.min.z - 0.005f
                    || b.max.z > topFrame.max.z + 0.005f
                )
                    Problem($"{item} hangs over the table's edge");
                foreach (var other in boxes)
                {
                    float gap = new RoomRect(b.min.x, b.max.x, b.min.z, b.max.z).Gap(
                        new RoomRect(
                            other.Value.min.x,
                            other.Value.max.x,
                            other.Value.min.z,
                            other.Value.max.z
                        )
                    );
                    minGap = Mathf.Min(minGap, gap);
                    if (gap < 0.1f - 0.005f)
                        Problem(
                            F(
                                $"{item} {gap * 100f:0.0} cm from {room.Placed[other.Key].name.Replace("(Clone)", "", StringComparison.Ordinal)}"
                            )
                        );
                }
                boxes.Add(new KeyValuePair<int, Bounds>(k, b));
            }
        }

        // The orbs: each a button on a post with its name over it, all at one height.
        float orbHeight = float.NaN;
        foreach (var (orb, post, label) in room.Marks)
        {
            var at = orb.Position;
            string what = $"orb '{(label != null ? label.text.Split('\n')[0] : "?")}'";
            int glyphs = 0;
            foreach (var line in orb.Root.GetComponentsInChildren<LineRenderer>())
                if (line.enabled)
                    glyphs++;
            if (!orb.Root.gameObject.activeInHierarchy || glyphs == 0)
                Problem($"{what}: orb or glyph not drawn");
            if (!Terrain(at, Vector3.down, 3f, out var floor))
            {
                Problem($"{what}: no floor under it");
                continue;
            }
            float height = at.y - floor.point.y;
            if (float.IsNaN(orbHeight))
                orbHeight = height;
            else if (Mathf.Abs(height - orbHeight) > RoomTolerance)
                Problem(F($"{what}: {height:0.00} m up, others {orbHeight:0.00} m"));
            var stand = post != null ? post.bounds : default;
            if (
                post == null
                || !post.enabled
                || Mathf.Abs(stand.min.y - floor.point.y) > RoomTolerance
                || Mathf.Abs(stand.max.y - (at.y - TouchOrb.Radius)) > RoomTolerance
                || FlatDistance(stand.center, at) > 0.03f
            )
                Problem($"{what}: no post from the floor up to it");
            if (
                label == null
                || !label.enabled
                || !label.gameObject.activeInHierarchy
                || label.color.a < 0.5f
                || string.IsNullOrWhiteSpace(label.text)
                || label.transform.position.y < at.y
                || label.transform.position.y > at.y + 0.3f
                || FlatDistance(label.transform.position, at) > 0.05f
            )
                Problem($"{what}: its label is not shown just over it");
        }

        // Lone letters in the room's world text (the floating "E"), and in VR any game key glyph.
        area.Expand(new Vector3(8f, 6f, 8f));
        int words = 0;
        foreach (var text in Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsSortMode.None))
        {
            if (
                !text.enabled
                || !text.gameObject.activeInHierarchy
                || !area.Contains(text.transform.position)
            )
                continue;
            var canvas = text.canvas;
            if (canvas != null && canvas.renderMode != RenderMode.WorldSpace)
                continue;
            words++;
            var shown = text.GetParsedText();
            if (string.IsNullOrEmpty(shown))
                shown = text.text;
            shown = shown?.Trim();
            if (shown != null && shown.Length == 1 && char.IsLetter(shown[0]))
                Problem($"a lone letter '{shown}' in the world ({text.name})");
        }
        int keycaps = 0;
        if (UI.World.UIWorldRuntime.Ready)
            foreach (var icon in Object.FindObjectsByType<InputIcon>(FindObjectsSortMode.None))
                if (
                    icon.TryGetComponent(out TMPro.TMP_Text glyph)
                    && glyph.enabled
                    && glyph.gameObject.activeInHierarchy
                )
                {
                    keycaps++;
                    Problem(
                        $"a key glyph drawn in VR ({icon.name} under {icon.transform.root.name})"
                    );
                }
        var unlit = RoomProps.Unlit(room.TableRoots.Length > 0 ? room.TableRoots[0].parent : null);
        if (unlit != null)
            Problem($"not lit: {unlit}");

        stepNote = F(
            $"{plan.Tables.Length} tables, {items} items ({missing} not out), {room.Marks.Count} orbs at {orbHeight:0.00} m; top {minThick * 100f:0.0} cm; closest items {minGap * 100f:0.0} cm; {words} world texts, {keycaps} key glyphs"
        );
        if (problems.Count > 0)
            Fail(string.Join("; ", problems));

        // From the entrance, behind the start orbs, looking into the room.
        var forward = room.Facing * Vector3.forward;
        yield return WarpTo(room.Entrance - forward * 1.2f);
        FaceRoom(room.Entrance + forward * 6f);
        yield return Wait(1f);
        yield return Shot("01c-item-room-entrance");
        // Over the fullest table, from its front edge, looking down at its items.
        var f = plan.Tables[fullest].Footprint;
        var middle = room.TableRoots[fullest].position;
        middle.y = room.TopOf(fullest);
        yield return WarpTo(middle - forward * (f.Depth * 0.5f + 0.45f));
        FaceRoom(middle);
        if (VRRig.Instance != null)
            LookAtPoint(middle);
        yield return Wait(1.5f);
        yield return Shot("01c-item-room-table");
    }

    /// <summary>Turns the view toward <paramref name="world"/>: the rig in VR, the character's look flat.</summary>
    static void FaceRoom(Vector3 world)
    {
        if (VRRig.Instance != null && VRRig.Instance.Head != null)
        {
            Face(world);
            return;
        }
        var c = Character.localCharacter;
        var dir = Flat(world - c.Center);
        // shortcut: yaw only, as the compass step turns the look; a flat shot over a table stays level.
        if (dir.sqrMagnitude > 1e-4f)
            c.data.lookValues = new Vector2(
                Quaternion.LookRotation(dir).eulerAngles.y,
                c.data.lookValues.y
            );
    }

    /// <summary>After 01c whatever its verdict: the room closes in place (never to the title, the run goes on in the
    /// airport) and the player goes back to spawn.</summary>
    IEnumerator EndRoomLayout()
    {
        EndGlance();
        if (ItemRoom.Current != null)
            Object.Destroy(ItemRoom.Current.gameObject);
        yield return null;
        yield return WarpBack();
    }
}
