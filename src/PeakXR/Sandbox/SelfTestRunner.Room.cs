using System.Collections;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>01b: the item room's props are drawn lit, like the level.</summary>
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
}
