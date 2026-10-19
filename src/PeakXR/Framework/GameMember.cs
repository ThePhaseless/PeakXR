using System;
using System.Reflection;
using HarmonyLib;

namespace PeakXR.Framework;

/// <summary>
/// Resolves non-public game members by name at runtime. Call these from <see cref="Feature.Install"/>
/// so a renamed member fails that feature cleanly instead of throwing mid-game.
/// </summary>
public static class GameMember
{
    public static AccessTools.FieldRef<T, F> Field<T, F>(string name)
    {
        var field = AccessTools.Field(typeof(T), name)
            ?? throw new MissingFieldException(typeof(T).FullName, name);
        return AccessTools.FieldRefAccess<T, F>(field);
    }

    public static AccessTools.FieldRef<F> StaticField<T, F>(string name)
    {
        var field = AccessTools.Field(typeof(T), name)
            ?? throw new MissingFieldException(typeof(T).FullName, name);
        return AccessTools.StaticFieldRefAccess<F>(field);
    }

    public static MethodInfo Method(Type type, string name, Type[] parameters = null)
        => AccessTools.Method(type, name, parameters)
           ?? throw new MissingMethodException(type.FullName, name);

    public static D Delegate<D>(Type type, string name, Type[] parameters = null) where D : Delegate
        => AccessTools.MethodDelegate<D>(Method(type, name, parameters));

    public static Type Type(string fullName)
        => AccessTools.TypeByName(fullName) ?? throw new TypeLoadException(fullName);
}
